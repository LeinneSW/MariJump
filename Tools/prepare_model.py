import hashlib
import io
import json
from pathlib import Path
import zipfile

ROOT = Path(__file__).resolve().parents[1]
ARCHIVE = ROOT / "blue-archive-mari-idol-chibi-stage.zip"
DEST = ROOT / "Assets/Mari"
MATERIALS = {
    "Texture_0": "ch0273_body.png",
    "outline": "outline.png",
    "mouth": "mouth.png",
    "face": "ch0273_face.png",
    "eye_brow": "ch0273_face.png",
    "eye": "ch0273_eyes.png",
    "hair": "ch0273_hair.png",
    "halo": "ch0273_halo.png",
    "stage": "my_event073_idolstage.png",
}

def write_text(path, content):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(content.replace("\r\n", "\n").replace("\n", "\r\n").encode("utf-8"))

def main():
    with zipfile.ZipFile(ARCHIVE) as outer:
        with zipfile.ZipFile(io.BytesIO(outer.read("source/CH0273.zip"))) as inner:
            obj = inner.read("CH0273.obj").decode("utf-8-sig")
            texture_names = list(dict.fromkeys([*MATERIALS.values(), "ch0273_eyes2.png"]))
            for name in texture_names:
                target = DEST / "Textures" / name
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(inner.read(name))

    vertices, normals, uvs, groups = [], [], [], []
    material = None
    for line in obj.splitlines():
        parts = line.split()
        if not parts:
            continue
        tag = parts[0]
        if tag == "v":
            vertices.append(tuple(map(float, parts[1:4])))
        elif tag == "vn":
            normals.append(tuple(map(float, parts[1:4])))
        elif tag == "vt":
            uvs.append(tuple(map(float, parts[1:3])))
        elif tag == "g":
            groups.append({"name": line[2:].replace("/", "_"), "material": material, "faces": []})
        elif tag == "usemtl":
            material = line[7:].replace(" ", "_")
            groups[-1]["material"] = material
        elif tag == "f":
            groups[-1]["faces"].append(tuple(tuple(int(n) - 1 for n in corner.split("/")) for corner in parts[1:]))
        elif tag not in {"#", "mtllib", "s"}:
            raise ValueError(f"지원하지 않는 OBJ 항목: {tag}")

    character = [g for g in groups if not g["name"].startswith("OL_")]
    stage = [g for g in groups if g["name"].startswith("OL_")]
    body = next(g for g in character if g["material"] == "Texture_0")
    ground = min(vertices[c[0]][2] for f in body["faces"] for c in f)
    top = max(vertices[c[0]][2] for g in character for f in g["faces"] for c in f)
    scale = 1.2 / (top - ground)

    def export(name, selected):
        pool = {}
        faces = []
        for group in selected:
            mapped = []
            for face in group["faces"]:
                ids = []
                for v, vt, vn in face:
                    # 위치가 같아도 UV 또는 노멀이 다르면 경계 정점을 유지합니다.
                    key = (vertices[v], uvs[vt], normals[vn])
                    if key not in pool:
                        pool[key] = len(pool) + 1
                    ids.append(pool[key])
                mapped.append(ids)
            faces.append((group, mapped))
        lines = ["# Unity용 좌표, 크기, 재질 경로를 정리한 OBJ입니다.", "mtllib MariMaterials.mtl"]
        for (x, y, z), uv, normal in pool:
            lines.append(f"v {x * scale:.9f} {(z - ground) * scale:.9f} {-y * scale:.9f}")
        for position, (u, v), normal in pool:
            lines.append(f"vt {u:.9f} {v:.9f}")
        for position, uv, (x, y, z) in pool:
            lines.append(f"vn {x:.9f} {z:.9f} {-y:.9f}")
        for group, mapped in faces:
            lines += [f"g {group['name']}", f"usemtl {group['material']}"]
            lines += ["f " + " ".join(f"{i}/{i}/{i}" for i in face) for face in mapped]
        write_text(DEST / "Models" / f"{name}.obj", "\n".join(lines) + "\n")
        return {"vertices": len(pool), "triangles": sum(len(f) - 2 for g in selected for f in g["faces"]), "mesh_groups": len(selected)}

    report = {
        "archive": ARCHIVE.name,
        "sha256": hashlib.sha256(ARCHIVE.read_bytes()).hexdigest(),
        "source_format": "OBJ",
        "source_vertices": len(vertices),
        "source_triangles": sum(len(f) - 2 for g in groups for f in g["faces"]),
        "source_bones": 0,
        "source_skin_weights": 0,
        "source_animation_clips": 0,
        "source_blend_shapes": 0,
        "coordinate_conversion": "(x, y, z) -> (x, z - ground, -y) * scale",
        "source_ground": ground,
        "scale": scale,
        "character_height_with_halo_m": 1.2,
        "character": export("MariIdol", character),
        "stage": export("IdolStage", stage),
        "groups": [{"name": g["name"], "material": g["material"], "triangles": sum(len(f)-2 for f in g["faces"])} for g in groups],
        "materials": MATERIALS,
    }
    lines = []
    for name, texture in MATERIALS.items():
        lines += [f"newmtl {name}", "Ka 1 1 1", "Kd 1 1 1", "Ks 0 0 0", "d 1", f"map_Kd ../Textures/{texture}", ""]
    write_text(DEST / "Models/MariMaterials.mtl", "\n".join(lines))
    write_text(ROOT / "docs/model-analysis.json", json.dumps(report, ensure_ascii=False, indent=2) + "\n")
    print(json.dumps({k: report[k] for k in ("character", "stage", "scale", "source_bones", "source_animation_clips")}, ensure_ascii=False))

if __name__ == "__main__":
    main()
