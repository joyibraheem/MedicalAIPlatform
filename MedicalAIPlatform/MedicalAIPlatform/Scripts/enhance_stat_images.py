"""Re-export stat card images with sharpening (no dataset downloads)."""
from pathlib import Path

from PIL import Image, ImageEnhance, ImageFilter, ImageOps

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "wwwroot" / "images"
TARGET = (1280, 720)


def enhance(pil: Image.Image) -> Image.Image:
    pil = ImageOps.autocontrast(pil.convert("RGB"), cutoff=1)
    pil = ImageEnhance.Contrast(pil).enhance(1.16)
    pil = ImageEnhance.Sharpness(pil).enhance(1.45)
    pil = pil.resize(TARGET, Image.Resampling.LANCZOS)
    return pil.filter(ImageFilter.UnsharpMask(radius=1.0, percent=160, threshold=2))


def save_enhanced(src: Path, dest: Path) -> None:
    img = enhance(Image.open(src))
    img.save(dest, format="PNG", optimize=True)
    print(f"saved {dest.name} {img.size}")


def save_workstation(src: Path, dest: Path) -> None:
    im = Image.open(src).convert("RGB")
    w, h = im.size
    crop = im.crop((int(w * 0.05), int(h * 0.10), int(w * 0.95), int(h * 0.90)))
    img = enhance(crop)
    img.save(dest, format="PNG", optimize=True)
    print(f"saved {dest.name} {img.size}")


def main() -> None:
    mapping = {
        "stat-chest-xray.png": "stat-chest-xray.png",
        "stat-chest-ct.png": "stat-chest-ct.png",
        "stat-lung-ct.png": "stat-lung-ct.png",
    }
    for src_name, dest_name in mapping.items():
        src = OUT / src_name
        if src.exists() and src.stat().st_size > 1000:
            save_enhanced(src, OUT / dest_name)

    ws = OUT / "stat-workstation-src.jpg"
    if ws.exists():
        save_workstation(ws, OUT / "stat-workstation.png")

    print("done")


if __name__ == "__main__":
    main()
