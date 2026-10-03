"""Render the supplied SVG exactly, without redrawing it. Requires cairosvg and Pillow."""
from pathlib import Path

import cairosvg
from PIL import Image

assets = Path(__file__).resolve().parents[1] / "Assets"
cairosvg.svg2png(
    url=str(assets / "WatchdogLogo.svg"),
    write_to=str(assets / "WatchdogLogo.png"),
    output_width=512,
    output_height=512,
)
with Image.open(assets / "WatchdogLogo.png") as image:
    image.save(
        assets / "WatchdogIcon.ico",
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    )
