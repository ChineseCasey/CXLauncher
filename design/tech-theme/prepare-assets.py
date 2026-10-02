"""Copy generated masters and export smaller PNG/ICO assets without changing their artwork."""
from pathlib import Path
import json
import shutil
from PIL import Image

workspace = Path(__file__).resolve().parents[2]
design = Path(__file__).resolve().parent
masters = design / "generated"
runtime = workspace / "src/CXLauncher/Assets/Icons"
preview = workspace / "design/motion-preview/assets"
for directory in (masters, runtime, preview):
    directory.mkdir(parents=True, exist_ok=True)
manifest = json.loads((design / "image-assets.json").read_text(encoding="utf-8"))
report = []
for asset in manifest["assets"]:
    name = asset["name"]
    master = masters / f"{name}-v1.png"
    if not master.exists():
        shutil.copyfile(asset["source"], master)
    with Image.open(master) as source:
        image = source.convert("RGBA")
        alpha_range = image.getchannel("A").getextrema()
        if alpha_range != (0, 255):
            raise ValueError(f"Expected genuine transparent alpha: {name} {alpha_range}")
        exported = image.resize((256, 256), Image.Resampling.LANCZOS)
        exported.save(runtime / f"{name}.png", optimize=True)
        shutil.copyfile(runtime / f"{name}.png", preview / f"{name}.png")
        if name == "cx-emblem":
            exported.save(runtime / "cx-launcher.ico", sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
        report.append({"name":name,"master":str(master),"masterSize":image.size,"alpha":alpha_range,"runtimeSize":[256,256],"runtimeBytes":(runtime / f"{name}.png").stat().st_size})
(design / "asset-verification.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps(report, indent=2))
