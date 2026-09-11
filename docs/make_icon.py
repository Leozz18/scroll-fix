"""Generate src/ScrollFix/app.ico (same glyph as the tray icon)."""

from pathlib import Path

from PIL import Image, ImageDraw

OUT = Path(__file__).resolve().parent.parent / "src" / "ScrollFix" / "app.ico"
SIZES = [16, 24, 32, 48, 64, 128, 256]


def render(size: int) -> Image.Image:
    scale = 8  # draw large, downsample for anti-aliasing
    s = size * scale
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    pad = s * 2 // 32
    d.ellipse((pad, pad, s - pad, s - pad), fill=(46, 160, 67, 255))

    stroke = max(1, s * 2 // 32)
    # wheel stem
    x0, x1 = s * 14 // 32, s * 18 // 32
    y0, y1 = s * 6 // 32, s * 26 // 32
    d.rounded_rectangle((x0, y0, x1, y1), radius=stroke, outline=(255, 255, 255, 255), width=stroke)
    # wheel ring
    d.ellipse((s * 10 // 32, s * 11 // 32, s * 22 // 32, s * 23 // 32), outline=(255, 255, 255, 255), width=stroke)
    return img.resize((size, size), Image.LANCZOS)


# Pillow derives every requested size from the image being saved, so save the
# largest frame and let it downscale.
render(SIZES[-1]).save(OUT, format="ICO", sizes=[(sz, sz) for sz in SIZES])
print(OUT, OUT.stat().st_size)
