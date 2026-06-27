"""FastAPI-facing RAD-DINO inference helpers (does not modify production CheXNet code)."""
from __future__ import annotations

import io
import json
import os
import tempfile
import time
from typing import Any, Dict, List, Tuple

import numpy as np
from PIL import Image

from inference import LABELS, load_model, val_transform

_BASE_DIR = os.path.dirname(os.path.abspath(__file__))
_CONFIG_PATH = os.path.join(_BASE_DIR, "model_config.json")
_WEIGHTS_PATH = os.path.join(_BASE_DIR, "best_model.pth")


def _load_config() -> Dict[str, Any]:
    if os.path.isfile(_CONFIG_PATH):
        with open(_CONFIG_PATH, "r", encoding="utf-8") as fh:
            return json.load(fh)
    return {
        "display_name": "BRAX Fine-Tuned (RAD-DINO)",
        "version": "v1",
        "dataset": "BRAX",
        "training_date": "",
    }


def _tensor_from_pil(image: Image.Image):
    import torch

    rgb = image.convert("RGB")
    return val_transform(rgb).unsqueeze(0)


def _run_model(image: Image.Image) -> Tuple[np.ndarray, int]:
    import torch

    if not os.path.isfile(_WEIGHTS_PATH):
        raise FileNotFoundError(f"RAD-DINO weights not found at {_WEIGHTS_PATH}")

    model = load_model(_WEIGHTS_PATH)
    device = next(model.parameters()).device
    x = _tensor_from_pil(image).to(device)

    t0 = time.perf_counter()
    with torch.no_grad():
        logits = model(x)
        probs = torch.sigmoid(logits).cpu().numpy()[0]
    elapsed_ms = int((time.perf_counter() - t0) * 1000)
    return probs.astype(np.float32), elapsed_ms


def _build_response(probs: np.ndarray, elapsed_ms: int) -> Dict[str, Any]:
    cfg = _load_config()
    probabilities = {LABELS[i]: float(probs[i]) for i in range(len(LABELS))}
    top_indices = probs.argsort()[::-1]
    topk = [
        {"class_name": LABELS[int(i)], "probability": float(probs[int(i)])}
        for i in top_indices[:5]
    ]
    top_idx = int(top_indices[0])
    predicted_class = LABELS[top_idx]
    confidence = float(probs[top_idx])

    import torch

    device = torch.device("cuda" if torch.cuda.is_available() else "cpu")

    return {
        "class_names": LABELS,
        "probabilities": probabilities,
        "topk": topk,
        "predicted_class": predicted_class,
        "confidence": confidence,
        "pneumonia_probability": probabilities.get("Pneumonia", 0.0),
        "model_used": cfg.get("display_name", "BRAX Fine-Tuned (RAD-DINO)"),
        "model_version": cfg.get("version", "v1"),
        "dataset": cfg.get("dataset", "BRAX"),
        "training_date": cfg.get("training_date", ""),
        "scan_type": "X-ray",
        "device": str(device),
        "inference_ms": elapsed_ms,
    }


def predict_image_bytes(image_bytes: bytes) -> Dict[str, Any]:
    image = Image.open(io.BytesIO(image_bytes))
    probs, elapsed_ms = _run_model(image)
    return _build_response(probs, elapsed_ms)


def predict_dicom_bytes(dicom_bytes: bytes) -> Dict[str, Any]:
    suffix = ".dcm"
    with tempfile.NamedTemporaryFile(suffix=suffix, delete=False) as tmp:
        tmp.write(dicom_bytes)
        tmp_path = tmp.name
    try:
        from inference import predict_dicom

        sorted_results = predict_dicom(tmp_path, weights_path=_WEIGHTS_PATH)
        probs = np.array([sorted_results.get(label, 0.0) for label in LABELS], dtype=np.float32)
        return _build_response(probs, 0)
    finally:
        try:
            os.remove(tmp_path)
        except OSError:
            pass
