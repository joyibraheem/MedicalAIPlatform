"""Prepare stat card radiology images from real clinical sources (never upscale)."""
from __future__ import annotations

from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "wwwroot" / "images"
MAX_W = 1920
ASPECT = 16 / 9


def center_crop_cover(im: Image.Image) -> Image.Image:
    w, h = im.size
    target_ratio = ASPECT
    current_ratio = w / h

    if current_ratio > target_ratio:
        new_w = int(h * target_ratio)
        left = (w - new_w) // 2
        return im.crop((left, 0, left + new_w, h))

    new_h = int(w / target_ratio)
    top = (h - new_h) // 2
    return im.crop((0, top, w, top + new_h))


def prepare_radiology(src: Path, dest: Path) -> None:
    im = Image.open(src)
    if im.mode in ("RGBA", "LA"):
        bg = Image.new("RGB", im.size, (0, 0, 0))
        bg.paste(im, mask=im.split()[-1])
        im = bg
    else:
        im = im.convert("RGB")

    cropped = center_crop_cover(im)
    if cropped.width > MAX_W:
        out_h = int(MAX_W / ASPECT)
        out = cropped.resize((MAX_W, out_h), Image.Resampling.LANCZOS)
    else:
        out = cropped

    out.save(dest, format="PNG", compress_level=2)
    print(f"{dest.name}: {src.name} {im.size} -> {out.size}")


def main() -> None:
    mapping = {
        "dl-2C10A413-AABE-4807-8CCE-6A2025594067.jpeg": "stat-chest-xray.png",
        "dl-3ED3C0E1-4FE0-4238-8112-DDFF9E20B471.jpeg": "stat-chest-ct.png",
        "dl-5083A6B7-8983-472E-A427-570A3E03DDEE.jpeg": "stat-lung-ct.png",
        "dl-FC230FE2-1DDF-40EB-AA0D-21F950933289.jpeg": "stat-thorax-ct.png",
    }

    for src_name, dest_name in mapping.items():
        src = OUT / src_name
        if not src.exists():
            raise FileNotFoundError(src)
        prepare_radiology(src, OUT / dest_name)

    print("done")


if __name__ == "__main__":
    main()
