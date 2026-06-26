"""
Minimal DICOM -> PIL RGB conversion for BRAX CheXNet training.
"""
from __future__ import annotations

import numpy as np
from PIL import Image

from brax_io_utils import as_long_path


def load_brax_image(path: str) -> Image.Image:
    if path.lower().endswith(".dcm"):
        return dicom_to_pil_rgb(path)
    return Image.open(as_long_path(path)).convert("RGB")


def dicom_to_pil_rgb(path: str) -> Image.Image:
    try:
        import pydicom
    except ImportError as exc:  # pragma: no cover
        raise ImportError(
            "Reading .dcm files requires pydicom. Install with: pip install pydicom"
        ) from exc

    ds = pydicom.dcmread(str(as_long_path(path)))
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
