"""Independent image checks. Test dependencies only: Pillow and zxing-cpp."""
import sys
from pathlib import Path

from PIL import Image, ImageEnhance, ImageFilter
import zxingcpp


def verify(directory):
    samples = sorted(Path(directory).glob("*.png"))
    if not samples:
        raise AssertionError("No barcode samples found")
    cases = 0
    for path in samples:
        expected = path.stem.split("-")[0]
        with Image.open(path) as original:
            variants = {
                "native": original.copy(),
                "rotated": original.rotate(90, expand=True),
                "scan": ImageEnhance.Contrast(
                    original.resize((original.width * 2, original.height * 2), Image.Resampling.BICUBIC)
                    .filter(ImageFilter.GaussianBlur(0.5))
                ).enhance(0.7),
            }
            for name, variant in variants.items():
                try:
                    decoded = [result.text for result in zxingcpp.read_barcodes(variant)]
                    assert decoded == [expected], (path.name, name, decoded)
                    cases += 1
                finally:
                    variant.close()
    print(f"{cases} independent barcode decoding checks passed.")


if __name__ == "__main__":
    verify(sys.argv[1])
