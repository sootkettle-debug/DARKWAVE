from collections import deque
from pathlib import Path
from PIL import Image

# Keep the emblem black inside its orange ring; remove only the exterior
# black background connected to the edges of the image.
SOURCE = Path("NEONWAVE/Assets/DarkwaveCDIcon.png")
OUT = Path("NEONWAVE/app.ico")

image = Image.open(SOURCE).convert("RGBA")
width, height = image.size
pixels = image.load()
seen = bytearray(width * height)
queue = deque()
for x in range(width):
    queue.extend(((x, 0), (x, height - 1)))
for y in range(height):
    queue.extend(((0, y), (width - 1, y)))

while queue:
    x, y = queue.popleft()
    i = y * width + x
    if seen[i]:
        continue
    seen[i] = 1
    r, g, b, a = pixels[x, y]
    if a == 0:
        pass
    elif max(r, g, b) <= 52:
        pixels[x, y] = (r, g, b, 0)
    else:
        continue
    if x > 0:
        queue.append((x - 1, y))
    if x + 1 < width:
        queue.append((x + 1, y))
    if y > 0:
        queue.append((x, y - 1))
    if y + 1 < height:
        queue.append((x, y + 1))

bbox = image.getbbox()
if not bbox:
    raise RuntimeError("Icon source became fully transparent")
image = image.crop(bbox)

# Full-sized icon frames with true transparency, no black square or margins.
image = image.resize((256, 256), Image.Resampling.LANCZOS)
OUT.parent.mkdir(parents=True, exist_ok=True)
image.save(OUT, format="ICO", sizes=[
    (16, 16), (24, 24), (32, 32), (48, 48),
    (64, 64), (128, 128), (256, 256)
])
print(f"Created full-size transparent Darkwave+CD icon: {OUT}")
