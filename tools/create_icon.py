from pathlib import Path
from PIL import Image

# This image is derived from the Darkwave+CD icon supplied for the build.
SOURCE = Path("NEONWAVE/Assets/DarkwaveCDIcon.png")
OUT = Path("NEONWAVE/app.ico")
with Image.open(SOURCE) as uploaded:
    image = uploaded.convert("RGBA")
    canvas = Image.new("RGBA", (256, 256), (0, 0, 0, 255))
    image.thumbnail((256, 256), Image.Resampling.LANCZOS)
    canvas.alpha_composite(image, ((256 - image.width) // 2, (256 - image.height) // 2))
    OUT.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(OUT, format="ICO", sizes=[(s, s) for s in (16, 24, 32, 48, 64, 128, 256)])
print(f"Created Darkwave+CD application icon: {OUT}")
