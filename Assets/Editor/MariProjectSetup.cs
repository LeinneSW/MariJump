using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class MariProjectSetup
{
    private const string ModelFolder = "Assets/Mari/Models/";
    private const string MaterialFolder = "Assets/Mari/Materials/";
    private const string PrefabFolder = "Assets/Mari/Prefabs/";
    private const string ScenePath = "Assets/Scenes/MariPreview.unity";

    [Serializable]
    private sealed class SourceGroup
    {
        public string name;
        public string material;
        public int triangles;
    }

    [Serializable]
    private sealed class SourceReport
    {
        public SourceGroup[] groups;
    }

    [Serializable]
    private sealed class ImportedModelReport
    {
        public string asset;
        public int renderers;
        public int vertices;
        public int triangles;
        public int skinnedMeshRenderers;
        public int bones;
        public int blendShapes;
        public int animationClips;
        public Vector3 size;
        public string[] meshNames;
    }

    [Serializable]
    private sealed class ImportReport
    {
        public string unityVersion;
        public string renderPipeline;
        public string scene;
        public ImportedModelReport character;
        public ImportedModelReport stage;
        public string[] renders;
        public string validation;
    }

    [MenuItem("마리 점프/모델과 미리보기 씬 다시 만들기")]
    public static void Build()
    {
        Directory.CreateDirectory(MaterialFolder);
        Directory.CreateDirectory(PrefabFolder);
        Directory.CreateDirectory("Assets/Scenes");
        Directory.CreateDirectory("docs");
        AssetDatabase.Refresh();

        PlayerSettings.companyName = "MariJump";
        PlayerSettings.productName = "MariJump";
        PlayerSettings.defaultScreenWidth = 1280;
        PlayerSettings.defaultScreenHeight = 720;
        PlayerSettings.colorSpace = ColorSpace.Gamma;
        ConfigureRendering();
        EditorSettings.serializationMode = SerializationMode.ForceText;

        var source = JsonUtility.FromJson<SourceReport>(File.ReadAllText("docs/model-analysis.json", Encoding.UTF8));
        var materials = CreateMaterials();
        ConfigureModel("MariIdol");
        ConfigureModel("IdolStage");

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var character = CreatePrefab("MariIdol", source.groups, materials);
        var stage = CreatePrefab("IdolStage", source.groups, materials);
        character.name = "마리";
        stage.name = "아이돌 무대";

        var cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        var camera = cameraObject.AddComponent<Camera>();
        camera.GetUniversalAdditionalCameraData();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.12f, 0.13f, 0.19f);
        camera.orthographic = true;
        camera.orthographicSize = 0.76f;
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = 50f;
        SetCamera(camera, new Vector3(0f, 0.78f, 4f), new Vector3(0f, 0.59f, 0f));
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = Color.white;

        var report = new ImportReport
        {
            unityVersion = Application.unityVersion,
            renderPipeline = GraphicsSettings.currentRenderPipeline.GetType().Name,
            scene = ScenePath,
            character = Inspect(character, ModelFolder + "MariIdol.obj"),
            stage = Inspect(stage, ModelFolder + "IdolStage.obj"),
            renders = new[] { "docs/mari-preview.png", "docs/mari-stage-preview.png", "docs/mari-side-preview.png" },
            validation = "Unity에서 메시, 재질, 텍스처, 크기, 뼈대 및 클립 수를 검사하고 카메라로 렌더링했습니다."
        };
        Require(report.character.triangles == 30172, "캐릭터 삼각형 수가 원본과 다릅니다.");
        Require(report.stage.triangles == 7055, "무대 삼각형 수가 원본과 다릅니다.");
        Require(Mathf.Abs(report.character.size.y - 1.20119f) < 0.005f, "캐릭터 크기를 확인해 주세요.");
        Require(report.character.bones == 0 && report.character.animationClips == 0, "예상하지 못한 리깅 또는 클립이 있습니다.");

        RenderPreviews(camera, stage, report.renders);

        EditorSceneManager.SaveScene(cameraObject.scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.LookAt(new Vector3(0f, 0.6f, 0f), Quaternion.Euler(5f, 180f, 0f), 1.6f);
        Selection.activeGameObject = character;
        AssetDatabase.SaveAssets();
        WriteText("docs/unity-import-report.json", JsonUtility.ToJson(report, true));
        Debug.Log("마리 모델 변환 및 Unity 렌더 검증을 완료했습니다.");
    }

    [MenuItem("마리 점프/URP로 전환")]
    public static void MigrateToUrp()
    {
        ConfigureRendering();
        CreateMaterials();
        var scene = EditorSceneManager.OpenScene(ScenePath);
        var camera = Camera.main;
        Require(camera != null, "미리보기 카메라를 찾을 수 없습니다.");
        camera.GetUniversalAdditionalCameraData();
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        VerifyUrp();
    }

    public static void VerifyUrp()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath);
        var camera = Camera.main;
        Require(camera != null, "미리보기 카메라를 찾을 수 없습니다.");
        var report = JsonUtility.FromJson<ImportReport>(File.ReadAllText("docs/unity-import-report.json", Encoding.UTF8));
        report.renderPipeline = GraphicsSettings.currentRenderPipeline.GetType().Name;
        Require(GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset, "기본 URP 설정을 확인해 주세요.");
        for (int i = 0; i < QualitySettings.names.Length; i++)
            Require(QualitySettings.GetRenderPipelineAssetAt(i) is UniversalRenderPipelineAsset,
                "품질 단계에 URP가 적용되지 않았습니다: " + QualitySettings.names[i]);
        foreach (var renderer in Object.FindObjectsByType<MeshRenderer>())
            foreach (var material in renderer.sharedMaterials)
                Require(material.shader.name == "Universal Render Pipeline/Unlit" && material.mainTexture != null,
                    "URP 재질을 확인해 주세요: " + renderer.name);
        var stage = scene.GetRootGameObjects().FirstOrDefault(root =>
            PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root) == PrefabFolder + "IdolStage.prefab");
        Require(stage != null, "무대 프리팹을 찾을 수 없습니다.");
        RenderPreviews(camera, stage, report.renders);
        WriteText("docs/unity-import-report.json", JsonUtility.ToJson(report, true));
        Debug.Log("URP 전환과 기본 그래픽 설정, 전체 품질 단계, 모델 재질 및 렌더 검증을 완료했습니다.");
    }

    private static void ConfigureRendering()
    {
        const string settingsFolder = "Assets/Settings";
        const string rendererPath = settingsFolder + "/MariRenderer.asset";
        const string pipelinePath = settingsFolder + "/MariURP.asset";
        Directory.CreateDirectory(settingsFolder);
        AssetDatabase.Refresh();
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
        if (renderer == null)
        {
            renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            renderer.renderingMode = RenderingMode.Forward;
            AssetDatabase.CreateAsset(renderer, rendererPath);
        }
        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
        if (pipeline == null)
        {
            pipeline = UniversalRenderPipelineAsset.Create(renderer);
            pipeline.msaaSampleCount = 4;
            pipeline.supportsHDR = false;
            pipeline.supportsCameraDepthTexture = false;
            pipeline.supportsCameraOpaqueTexture = false;
            AssetDatabase.CreateAsset(pipeline, pipelinePath);
        }
        GraphicsSettings.defaultRenderPipeline = pipeline;
        int previousQuality = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = pipeline;
        }
        QualitySettings.SetQualityLevel(previousQuality, false);
        AssetDatabase.SaveAssets();
    }

    private static Dictionary<string, Material> CreateMaterials()
    {
        var textures = new Dictionary<string, string>
        {
            { "Texture_0", "ch0273_body.png" },
            { "outline", "outline.png" },
            { "mouth", "mouth.png" },
            { "face", "ch0273_face.png" },
            { "eye_brow", "ch0273_face.png" },
            { "eye", "ch0273_eyes.png" },
            { "hair", "ch0273_hair.png" },
            { "halo", "ch0273_halo.png" },
            { "stage", "my_event073_idolstage.png" }
        };
        var materials = new Dictionary<string, Material>();
        foreach (var entry in textures)
        {
            string texturePath = "Assets/Mari/Textures/" + entry.Value;
            var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            Require(importer != null, "텍스처를 찾을 수 없습니다: " + texturePath);
            importer.textureType = TextureImporterType.Default;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = entry.Key == "mouth";
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.SaveAndReimport();

            // 원본의 몸체와 눈 알파는 불투명도용이 아니므로 RGB를 불투명하게 표시합니다.
            const string shaderName = "Universal Render Pipeline/Unlit";
            var shader = Shader.Find(shaderName);
            Require(shader != null && shader.isSupported, "셰이더를 사용할 수 없습니다: " + shaderName);
            string path = MaterialFolder + entry.Key + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_AlphaClip", entry.Key == "mouth" ? 1f : 0f);
            material.SetFloat("_Cutoff", 0.2f);
            BaseShaderGUI.SetMaterialKeywords(material);
            EditorUtility.SetDirty(material);
            materials.Add(entry.Key, material);
        }
        return materials;
    }

    private static void ConfigureModel(string name)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelFolder + name + ".obj");
        Require(importer != null, "OBJ를 불러올 수 없습니다: " + name);
        importer.globalScale = 1f;
        importer.useFileScale = false;
        importer.animationType = ModelImporterAnimationType.None;
        importer.importAnimation = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importNormals = ModelImporterNormals.Import;
        importer.importTangents = ModelImporterTangents.None;
        importer.meshCompression = ModelImporterMeshCompression.Off;
        importer.isReadable = false;
        importer.SaveAndReimport();
    }

    private static GameObject CreatePrefab(string name, SourceGroup[] groups, Dictionary<string, Material> materials)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelFolder + name + ".obj");
        Require(model != null, "Unity 모델을 생성하지 못했습니다: " + name);
        var root = new GameObject(name);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        instance.name = "Model";
        instance.transform.SetParent(root.transform, false);
        foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>())
        {
            var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            var group = groups.FirstOrDefault(g => g.name == renderer.name || g.name == mesh.name);
            Require(group != null, "재질을 연결할 그룹이 없습니다: " + renderer.name + " / " + mesh.name);
            renderer.sharedMaterials = Enumerable.Repeat(materials[group.material], mesh.subMeshCount).ToArray();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + name + ".prefab");
        Object.DestroyImmediate(root);
        return (GameObject)PrefabUtility.InstantiatePrefab(prefab);
    }

    private static ImportedModelReport Inspect(GameObject root, string asset)
    {
        var renderers = root.GetComponentsInChildren<Renderer>();
        Require(renderers.Length > 0, "모델에 렌더러가 없습니다.");
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers)
        {
            bounds.Encapsulate(renderer.bounds);
            foreach (var material in renderer.sharedMaterials)
                Require(material != null && material.mainTexture != null && material.shader.isSupported,
                    "재질 또는 텍스처 연결을 확인해 주세요: " + renderer.name);
        }
        var meshes = root.GetComponentsInChildren<MeshFilter>().Select(f => f.sharedMesh).ToArray();
        var skins = root.GetComponentsInChildren<SkinnedMeshRenderer>();
        return new ImportedModelReport
        {
            asset = asset,
            renderers = renderers.Length,
            vertices = meshes.Sum(m => m.vertexCount),
            triangles = meshes.Sum(m => Enumerable.Range(0, m.subMeshCount).Sum(i => (int)m.GetIndexCount(i) / 3)),
            skinnedMeshRenderers = skins.Length,
            bones = skins.SelectMany(s => s.bones).Distinct().Count(),
            blendShapes = meshes.Sum(m => m.blendShapeCount),
            animationClips = AssetDatabase.LoadAllAssetsAtPath(asset).OfType<AnimationClip>().Count(),
            size = bounds.size,
            meshNames = meshes.Select(m => m.name).ToArray()
        };
    }

    private static void SetCamera(Camera camera, Vector3 position, Vector3 target)
    {
        camera.transform.position = position;
        camera.transform.LookAt(target);
    }

    private static void RenderPreviews(Camera camera, GameObject stage, string[] paths)
    {
        Vector3 originalPosition = camera.transform.position;
        Quaternion originalRotation = camera.transform.rotation;
        float originalSize = camera.orthographicSize;
        bool stageWasActive = stage.activeSelf;
        try
        {
            Render(camera, paths[0]);
            camera.orthographicSize = 1.8f;
            SetCamera(camera, new Vector3(0f, 1.8f, 5f), new Vector3(0f, 1.32f, 0f));
            Render(camera, paths[1]);
            stage.SetActive(false);
            camera.orthographicSize = 0.76f;
            SetCamera(camera, new Vector3(3f, 0.9f, 2.8f), new Vector3(0f, 0.59f, 0f));
            Render(camera, paths[2]);
        }
        finally
        {
            stage.SetActive(stageWasActive);
            camera.orthographicSize = originalSize;
            camera.transform.SetPositionAndRotation(originalPosition, originalRotation);
        }
    }

    private static void Render(Camera camera, string path)
    {
        var previous = RenderTexture.active;
        var previousTarget = camera.targetTexture;
        var target = RenderTexture.GetTemporary(1280, 960, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, 4);
        var texture = new Texture2D(1280, 960, TextureFormat.RGB24, false);
        try
        {
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            Object.DestroyImmediate(texture);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void WriteText(string path, string content)
    {
        File.WriteAllText(path, content.Replace("\r\n", "\n").Replace("\n", "\r\n") + "\r\n", new UTF8Encoding(false));
    }
}
