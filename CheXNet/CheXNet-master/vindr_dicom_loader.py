"""
Load VinDr-PCXR images (DICOM primary) for CheXNet training.
"""
from __future__ import annotations

import numpy as np
from PIL import Image


def load_vindr_image(path: str) -> Image.Image:
    lower = path.lower()
    if lower.endswith(".dcm") or lower.endswith(".dicom"):
        return dicom_to_pil_rgb(path)
    return Image.open(path).convert("RGB")


def dicom_to_pil_rgb(path: str) -> Image.Image:
    try:
        import pydicom
    except ImportError as exc:  # pragma: no cover
        raise ImportError(
            "Reading VinDr-PCXR DICOM files requires pydicom. Install with: pip install pydicom"
        ) from exc

    ds = pydicom.dcmread(path)
    pixel = ds.pixel_array.astype(np.float32)

    slope = float(getattr(ds, "RescaleSlope", 1.0) or 1.0)
    intercept = float(getattr(ds, "RescaleIntercept", 0.0) or 0.0)
    pixel = pixel * slope + intercept

    if pixel.ndim > 2:
        pixel = pixel[..., 0]

    p_min = float(np.min(pixel))
    p_max = float(np.max(pixel))
    if p_max > p_min:
        pixel = (pixel - p_min) / (p_max - p_min) * 255.0
    else:
        pixel = np.zeros_like(pixel)

    gray = pixel.astype(np.uint8)
    rgb = np.stack([gray, gray, gray], axis=-1)
    return Image.fromarray(rgb, mode="RGB")
