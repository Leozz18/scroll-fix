from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

out = Path(__file__).resolve().parent / "demo.gif"
W, H = 720, 320


def font(size: int) -> ImageFont.ImageFont:
    for name in (
        r"C:\Windows\Fonts\segoeui.ttf",
        r"C:\Windows\Fonts\arial.ttf",
        r"C:\Windows\Fonts\calibri.ttf",
    ):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            pass
    return ImageFont.load_default()


title_f = font(28)
small_f = font(16)


def draw_panel(label: str, y_positions: list[int], accent: tuple[int, int, int]) -> Image.Image:
    img = Image.new("RGB", (W, H), (18, 22, 28))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((24, 24, W - 24, H - 24), radius=16, fill=(28, 34, 42), outline=(60, 70, 82), width=2)
    d.text((48, 40), label, fill=(240, 240, 240), font=title_f)
    for y in y_positions:
        d.rounded_rectangle((80, y, W - 80, y + 18), radius=4, fill=(70, 80, 95))
    if y_positions:
        cy = y_positions[min(2, len(y_positions) - 1)]
        d.ellipse((52, cy + 2, 68, cy + 18), fill=accent)
    d.text((48, H - 52), "Scroll Fix demo (conceptual)", fill=(140, 150, 160), font=small_f)
    return img


base = list(range(90, 230, 22))
positions = base[:]
seq_before: list[Image.Image] = []
for _ in range(8):
    positions = [p - 10 for p in positions]
    seq_before.append(draw_panel("BEFORE: ghost reverse jump", positions, (220, 80, 70)))
for _ in range(3):
    positions = [p + 18 for p in positions]
    seq_before.append(draw_panel("BEFORE: ghost reverse jump", positions, (220, 80, 70)))
for _ in range(5):
    positions = [p - 10 for p in positions]
    seq_before.append(draw_panel("BEFORE: ghost reverse jump", positions, (220, 80, 70)))

positions = base[:]
seq_after: list[Image.Image] = []
for _ in range(16):
    positions = [p - 10 for p in positions]
    seq_after.append(draw_panel("AFTER: Scroll Fix blocks ghosts", positions, (46, 160, 67)))

frames = seq_before + seq_after
frames[0].save(
    out,
    save_all=True,
    append_images=frames[1:],
    duration=[90] * len(seq_before) + [80] * len(seq_after),
    loop=0,
    optimize=True,
)
print(out, out.stat().st_size)
