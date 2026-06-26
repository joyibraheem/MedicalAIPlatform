"""Export sharp stat-card radiology images (uses cached MedMNIST + workstation photo)."""
from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image, ImageEnhance, ImageFilter, ImageOps

from medmnist import ChestMNIST, OrganCMNIST, INFO

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "wwwroot" / "images"
TARGET = (1280, 720)


def as_label(label) -> int:
    return int(label.item()) if hasattr(label, "item") else int(label)


def enhance_rgb(pil: Image.Image) -> Image.Image:
    pil = ImageOps.autocontrast(pil.convert("RGB"), cutoff=1)
    pil = ImageEnhance.Contrast(pil).enhance(1.14)
    pil = ImageEnhance.Sharpness(pil).enhance(1.4)
    pil = pil.resize(TARGET, Image.Resampling.LANCZOS)
    return pil.filter(ImageFilter.UnsharpMask(radius=1.1, percent=150, threshold=2))


def save_array(arr: np.ndarray, path: Path) -> None:
    if arr.ndim == 2:
        pil = Image.fromarray(arr.astype(np.uint8), mode="L")
    else:
        pil = Image.fromarray(arr.astype(np.uint8))
    out = enhance_rgb(pil)
    out.save(path, format="PNG", optimize=True)
    print(f"saved {path.name} {out.size}")


def pick_by_label(dataset, target: int, start: int = 0) -> np.ndarray:
    for i in range(start, len(dataset)):
        _, label = dataset[i]
        if as_label(label) == target:
            img, _ = dataset[i]
            return np.array(img)
    raise RuntimeError(f"label {target} not found")


def save_workstation(src: Path, dest: Path) -> None:
    img = Image.open(src).convert("RGB")
    # Focus on monitors / reading area
    w, h = img.size
    crop = img.crop((int(w * 0.05), int(h * 0.12), int(w * 0.95), int(h * 0.88)))
    out = enhance_rgb(crop)
    out.save(dest, format="PNG", optimize=True)
    print(f"saved {dest.name} {out.size}")


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)

    cxr = ChestMNIST(split="train", download=False, size=224)
    img, _ = cxr[1024]
    save_array(np.array(img), OUT / "stat-chest-xray.png")

    org = OrganCMNIST(split="train", download=False, size=128)
    labels = INFO["organcmnist"]["label"]
    heart_id = next(int(k) for k, v in labels.items() if v == "heart")
    lung_id = next(int(k) for k, v in labels.items() if v == "lung")

    save_array(pick_by_label(org, heart_id, 200), OUT / "stat-chest-ct.png")
    save_array(pick_by_label(org, lung_id, 400), OUT / "stat-lung-ct.png")

    ws_src = OUT / "stat-workstation-src.jpg"
    if ws_src.exists():
        save_workstation(ws_src, OUT / "stat-workstation.png")
    else:
        save_array(pick_by_label(org, lung_id, 900), OUT / "stat-workstation.png")

    print("done")


if __name__ == "__main__":
    main()
