#!/usr/bin/env python3
"""Admin-only single-image inference from a fine-tuned checkpoint (does not modify production API)."""
from __future__ import annotations

import argparse
import base64
import json
import sys
import time
from io import BytesIO
from pathlib import Path

import torch
from PIL import Image
from torchvision import transforms

ROOT = Path(__file__).resolve().parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from brax_config import CHEXNET_CLASS_NAMES, chexnet_checkpoint_path  # noqa: E402


def load_model(checkpoint: Path):
    from model import DenseNet121  # existing module — not modified

    model = DenseNet121(14)
    ckpt = torch.load(checkpoint, map_location="cpu", weights_only=False)
    state = ckpt.get("state_dict", ckpt)
    model.load_state_dict(state, strict=False)
    model.eval()
    return model


def predict(model, image_path: Path, heatmap_class: str | None):
    tfm = transforms.Compose([
        transforms.Resize((224, 224)),
        transforms.ToTensor(),
        transforms.Normalize([0.485, 0.456, 0.406], [0.229, 0.224, 0.225]),
    ])
    img = Image.open(image_path).convert("RGB")
    tensor = tfm(img).unsqueeze(0)
    t0 = time.perf_counter()
    with torch.no_grad():
        logits = model(tensor)
        probs = torch.sigmoid(logits)[0]
    ms = (time.perf_counter() - t0) * 1000.0
    pairs = sorted(
        [(CHEXNET_CLASS_NAMES[i], float(probs[i])) for i in range(len(CHEXNET_CLASS_NAMES))],
        key=lambda x: x[1],
        reverse=True,
    )
    top_label = pairs[0][0] if pairs else ""
    out = {
        "top_label": top_label,
        "inference_ms": ms,
        "predictions": [{"label": n, "confidence": p} for n, p in pairs[:14]],
        "heatmap_base64": None,
    }
    return out


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--checkpoint", type=Path, required=True)
    parser.add_argument("--image", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--heatmap-class", type=str, default=None)
    args = parser.parse_args()

    ckpt = args.checkpoint if args.checkpoint.exists() else chexnet_checkpoint_path()
    if not ckpt.exists():
        print(f"Checkpoint not found: {ckpt}", file=sys.stderr)
        return 1
    if not args.image.exists():
        print(f"Image not found: {args.image}", file=sys.stderr)
        return 1

    model = load_model(ckpt)
    result = predict(model, args.image, args.heatmap_class)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2), encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
