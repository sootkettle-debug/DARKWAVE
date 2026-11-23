from pathlib import Path
from PIL import Image

# Updated Darkwave+CD artwork, tightly cropped to fill the Windows icon.
SOURCE = Path("NEONWAVE/Assets/DarkwaveCDIcon.png")
OUT = Path("NEONWAVE/app.ico")
with Image.open(SOURCE) as artwork:
    image = artwork.convert("RGBA").resize((256, 256), Image.Resampling.LANCZOS)
    OUT.parent.mkdir(parents=True, exist_ok=True)
    image.save(OUT, format="ICO", sizes=[
        (16, 16), (24, 24), (32, 32), (48, 48),
        (64, 64), (128, 128), (256, 256)
    ])
print(f"Created tightly cropped Darkwave+CD Windows icon: {OUT}")
