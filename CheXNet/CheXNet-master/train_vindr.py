#!/usr/bin/env python3
"""
Train CheXNet on a prepared VinDr-PCXR subset.

Usage:
  python train_vindr.py --size 100
  python train_vindr.py --size 1000
  python train_vindr.py --size 3000
"""
from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path

from vindr_pcxr_config import (
    ALLOWED_SUBSET_SIZES,
    DEFAULT_BATCH_SIZE_BY_SIZE,
    DEFAULT_EPOCHS_BY_SIZE,
    DEFAULT_VINDR_ROOT,
    resolve_vindr_data_root,
    run_dir,
    subset_dir,
)

from vindr_train_core import train_vindr_subset

DEFAULT_NUM_WORKERS = 0 if os.name == "nt" else 2


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Fine-tune CheXNet on a prepared VinDr-PCXR subset.")
    parser.add_argument(
        "--size",
        type=int,
        required=True,
        choices=ALLOWED_SUBSET_SIZES,
        help="Subset size prepared by prepare_vindr_subset.py (100, 1000, or 3000).",
    )
    parser.add_argument(
        "--vindr-root",
        type=Path,
        default=DEFAULT_VINDR_ROOT,
        help=f"VinDr-PCXR root folder (default: {DEFAULT_VINDR_ROOT}).",
    )
    parser.add_argument(
        "--epochs",
        type=int,
        default=None,
        help="Override epoch count (defaults: 100->2, 1000->5, 3000->8).",
    )
    parser.add_argument(
        "--batch-size",
        type=int,
        default=None,
        help="Override batch size (defaults: 100->8, 1000/3000->16).",
    )
    parser.add_argument(
        "--learning-rate",
        type=float,
        default=1e-4,
        help="Adam learning rate.",
    )
    parser.add_argument(
        "--num-workers",
        type=int,
        default=DEFAULT_NUM_WORKERS,
        help="DataLoader worker processes (default: 0 on Windows, 2 elsewhere).",
    )
    parser.add_argument(
        "--finetune-from",
        type=Path,
        default=None,
        help="Optional checkpoint path (default: ./model.pth.tar if present).",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    vindr_root = args.vindr_root.resolve()
    subset_path = subset_dir(vindr_root, args.size)
    output_path = run_dir(vindr_root, args.size)

    if not (subset_path / "train_list.txt").is_file():
        print(
            f"ERROR: Missing prepared subset at {subset_path}.\n"
            f"Run first: python prepare_vindr_subset.py --size {args.size}",
            file=sys.stderr,
        )
        return 1

    epochs = args.epochs if args.epochs is not None else DEFAULT_EPOCHS_BY_SIZE[args.size]
    batch_size = args.batch_size if args.batch_size is not None else DEFAULT_BATCH_SIZE_BY_SIZE[args.size]

    meta_path = subset_path / "meta.json"
    if meta_path.is_file():
        with open(meta_path, "r", encoding="utf-8") as f:
            data_root = Path(json.load(f).get("data_dir_for_training", resolve_vindr_data_root(vindr_root)))
    else:
        data_root = resolve_vindr_data_root(vindr_root)

    summary = train_vindr_subset(
        vindr_root=data_root,
        subset_dir=subset_path,
        output_dir=output_path,
        epochs=epochs,
        batch_size=batch_size,
        learning_rate=args.learning_rate,
        num_workers=args.num_workers,
        finetune_from=args.finetune_from,
    )

    print(f"Training finished for subset_{args.size}")
    print(f"  best checkpoint: {summary['best_checkpoint']}")
    print(f"  metrics:         {output_path / 'metrics.json'}")
    print(f"  log:             {output_path / 'training.log'}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        raise SystemExit(1)
