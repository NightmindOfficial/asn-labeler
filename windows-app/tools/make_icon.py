from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "assets" / "app.ico"
OUT.parent.mkdir(parents=True, exist_ok=True)


def font(size: int, bold: bool = False) -> ImageFont.ImageFont:
    candidates = [
        "C:/Windows/Fonts/segoeuib.ttf" if bold else "C:/Windows/Fonts/segoeui.ttf",
        "C:/Windows/Fonts/consolab.ttf" if bold else "C:/Windows/Fonts/consola.ttf",
    ]
    for candidate in candidates:
        path = Path(candidate)
        if path.exists():
            return ImageFont.truetype(str(path), size)
    return ImageFont.load_default()


def make(size: int) -> Image.Image:
    scale = size / 256
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    def box(values):
        return tuple(int(v * scale) for v in values)

    d.rounded_rectangle(box((24, 28, 232, 228)), radius=int(42 * scale), fill=(20, 82, 204, 255))
    d.rounded_rectangle(box((50, 58, 206, 198)), radius=int(20 * scale), fill=(255, 255, 255, 255))
    d.rounded_rectangle(box((64, 72, 192, 118)), radius=int(10 * scale), fill=(225, 239, 255, 255))

    x = int(67 * scale)
    widths = [4, 2, 6, 3, 2, 5, 7, 2, 4, 5, 2, 7, 3, 3, 6, 2, 5]
    for i, w in enumerate(widths):
        if i % 2 == 0:
            d.rectangle((x, int(132 * scale), x + max(1, int(w * scale)), int(166 * scale)), fill=(17, 24, 39, 255))
        x += max(1, int((w + 3) * scale))

    d.text(box((72, 174)), "ASN", fill=(17, 24, 39, 255), font=font(max(12, int(28 * scale)), True))
    d.line(box((145, 188, 164, 207, 205, 158)), fill=(16, 185, 129, 255), width=max(4, int(13 * scale)), joint="curve")
    return img


icons = [make(s) for s in (16, 24, 32, 48, 64, 128, 256)]
icons[-1].save(OUT, sizes=[(im.width, im.height) for im in icons], append_images=icons[:-1])
print(OUT)

