"""Create synthetic, offline performance fixtures inside .artifacts only (never real game data)."""
import json
from datetime import datetime, timezone
from pathlib import Path
from uuid import uuid4
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parent.parent / ".artifacts" / "performance" / uuid4().hex
for name in ("covers", "cache/thumbnails", "fixtures"):
    (root / name).mkdir(parents=True)
now = datetime.now(timezone.utc).isoformat()
games = []
for index in range(500):
    cover_id = uuid4().hex
    color = ((index * 13) % 130 + 25, (index * 23) % 110 + 35, (index * 37) % 130 + 30)
    image = Image.new("RGB", (256, 384), color)
    draw = ImageDraw.Draw(image)
    draw.rectangle((24, 24, 231, 359), outline=(170, 190, 190), width=2)
    draw.text((40, 175), f"LOCAL FIXTURE {index:04d}", fill=(240, 240, 240))
    image.save(root / "covers" / f"{cover_id}.png")
    image.save(root / "cache/thumbnails" / f"{cover_id}.png")
    target = root / "fixtures" / f"game-{index:04d}.exe"
    target.write_bytes(b"Performance fixture; never execute.")
    games.append({"id": str(uuid4()), "name": f"Game {index:04d}", "targetType": "Executable", "targetPath": str(target),
                  "arguments": "", "workingDirectory": "", "coverId": cover_id, "category": "Benchmark", "favorite": False,
                  "addedAt": now, "lastLaunchedAt": None})
(root / "library.json").write_text(json.dumps({"schemaVersion": 1, "games": games}), encoding="utf-8")
print(root)
