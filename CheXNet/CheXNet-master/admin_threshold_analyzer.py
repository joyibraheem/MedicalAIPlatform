#!/usr/bin/env python3
"""Admin-only threshold analysis from a trained checkpoint (no retraining)."""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--checkpoint", type=Path, required=True)
    parser.add_argument("--metrics", type=Path, required=True)
    parser.add_argument("--threshold", type=float, default=0.5)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    from brax_train_core import compute_multilabel_metrics, evaluate_loader, build_model_for_training
    from brax_dataloader import build_brax_dataloaders
    from brax_config import resolve_brax_data_root
    import torch

    metrics_doc = json.loads(args.metrics.read_text(encoding="utf-8"))
    subset_dir = Path(metrics_doc.get("subset_dir", ""))
    brax_root = Path(metrics_doc.get("brax_root", resolve_brax_data_root()))
    if not subset_dir.exists():
        print(f"Subset dir missing: {subset_dir}", file=sys.stderr)
        return 1

    device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
    model, _ = build_model_for_training(
        checkpoint_path=args.checkpoint,
        unfreeze_last_block=bool(metrics_doc.get("unfreeze_last_block")),
        device=device,
    )
    _, val_loader = build_brax_dataloaders(
        subset_dir=subset_dir,
        brax_root=brax_root,
        batch_size=int(metrics_doc.get("batch_size", 8)),
        num_workers=0,
    )
    y_true, y_prob = evaluate_loader(model, val_loader, device)
    curve = []
    for t in [i / 100.0 for i in range(1, 91)]:
        m = compute_multilabel_metrics(y_true, y_prob, threshold=t)
        curve.append({"epoch": int(t * 100), "value": float(m.get("f1_micro", 0) or 0)})

    m = compute_multilabel_metrics(y_true, y_prob, threshold=args.threshold)
    y_pred = (y_prob >= args.threshold).astype(np.int32)
    tp = int(((y_pred == 1) & (y_true == 1)).sum())
    fp = int(((y_pred == 1) & (y_true == 0)).sum())
    fn = int(((y_pred == 0) & (y_true == 1)).sum())
    tn = int(((y_pred == 0) & (y_true == 0)).sum())
    sens = tp / (tp + fn) if (tp + fn) else 0.0
    spec = tn / (tn + fp) if (tn + fp) else 0.0

    out = {
        "threshold": args.threshold,
        "accuracy": m.get("accuracy"),
        "precision_micro": m.get("precision_micro"),
        "recall_micro": m.get("recall_micro"),
        "f1_micro": m.get("f1_micro"),
        "sensitivity": sens,
        "specificity": spec,
        "predicted_positives": int((y_pred == 1).sum()),
        "false_positives": fp,
        "false_negatives": fn,
        "f1_curve": curve,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(out, indent=2), encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
