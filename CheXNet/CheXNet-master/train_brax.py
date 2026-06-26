#!/usr/bin/env python3
"""
Train CheXNet on a prepared BRAX subset (offline fine-tuning).

Usage:
  python train_brax.py --size 100
  python train_brax.py --size 300
  python train_brax.py --size 300 --resume model_versions/BRAX_xxx/best_model.pth.tar
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from brax_config import (
    ALLOWED_SUBSET_SIZES,
    DEFAULT_BATCH_SIZE_BY_SIZE,
    DEFAULT_BRAX_ROOT,
    DEFAULT_EPOCHS_BY_SIZE,
    make_run_output_dir,
    resolve_brax_data_root,
    subset_dir,
)
from brax_data_validation import print_validation_report, run_full_validation
from brax_train_core import print_post_training_summary, train_brax_subset
from brax_dataloader import default_num_workers

import torch


def auto_batch_size_for_device(subset_size: int) -> int:
    """Pick a practical batch size for CPU vs GPU."""
    default = DEFAULT_BATCH_SIZE_BY_SIZE[subset_size]
    if torch.cuda.is_available():
        return default
    # CPU: smaller batches reduce memory pressure and often train more stably.
    cpu_caps = {100: 8, 300: 8, 600: 8, 1000: 8, 3000: 4}
    return min(default, cpu_caps.get(subset_size, 8))


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Fine-tune CheXNet on a prepared BRAX subset.")
    parser.add_argument(
        "--size",
        type=int,
        required=True,
        choices=ALLOWED_SUBSET_SIZES,
        help="Subset size prepared by prepare_brax_subset.py (100, 300, 600, 1000, or 3000).",
    )
    parser.add_argument(
        "--brax-root",
        type=Path,
        default=DEFAULT_BRAX_ROOT,
        help=f"BRAX root folder (default: {DEFAULT_BRAX_ROOT}).",
    )
    parser.add_argument(
        "--epochs",
        type=int,
        default=None,
        help="Number of epochs to run in this session (defaults depend on subset size).",
    )
    parser.add_argument(
        "--batch-size",
        type=int,
        default=None,
        help="Override batch size (defaults depend on subset size).",
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
        default=default_num_workers(),
        help="DataLoader worker processes (default: 0 on Windows, 2 elsewhere).",
    )
    parser.add_argument(
        "--finetune-from",
        type=Path,
        default=None,
        help="Optional checkpoint path (default: ./model.pth.tar if present). Ignored when --resume is set.",
    )
    parser.add_argument(
        "--resume",
        type=Path,
        default=None,
        help="Resume from a prior BRAX run checkpoint (e.g. model_versions/BRAX_xxx/best_model.pth.tar).",
    )
    parser.add_argument(
        "--unfreeze-last-block",
        action="store_true",
        help="Unfreeze DenseNet denseblock4 plus the classifier (backbone otherwise frozen).",
    )
    parser.add_argument(
        "--early-stopping-patience",
        type=int,
        default=None,
        help="Stop when validation loss stops improving for N epochs (restores best weights).",
    )
    parser.add_argument(
        "--skip-validation",
        action="store_true",
        help="Skip automatic pre-training validation (not recommended).",
    )
    parser.add_argument(
        "--require-checkpoint",
        action="store_true",
        help="Abort if model.pth.tar is missing instead of using ImageNet backbone.",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    brax_root = args.brax_root.resolve()
    subset_path = subset_dir(brax_root, args.size)

    if not (subset_path / "train_list.txt").is_file():
        print(
            f"ERROR: Missing prepared subset at {subset_path}.\n"
            f"Run first: python prepare_brax_subset.py --size {args.size}",
            file=sys.stderr,
        )
        return 1

    if args.resume is not None and not args.resume.is_file():
        print(f"ERROR: Resume checkpoint not found: {args.resume.resolve()}", file=sys.stderr)
        return 1

    if not args.skip_validation:
        dataset_report, env_report, ready = run_full_validation(brax_root, extract_zips=True)
        print_validation_report(dataset_report, env_report, ready=ready)
        if not ready:
            print("\nPre-training validation failed. Fix issues above before training.", file=sys.stderr)
            return 1

    epochs = args.epochs if args.epochs is not None else DEFAULT_EPOCHS_BY_SIZE[args.size]
    batch_size = (
        args.batch_size
        if args.batch_size is not None
        else auto_batch_size_for_device(args.size)
    )

    meta_path = subset_path / "meta.json"
    if meta_path.is_file():
        with open(meta_path, "r", encoding="utf-8") as f:
            data_root = Path(json.load(f).get("data_dir_for_training", resolve_brax_data_root(brax_root)))
    else:
        data_root = resolve_brax_data_root(brax_root)

    output_path = make_run_output_dir(
        subset_size=args.size,
        epochs=epochs,
        learning_rate=args.learning_rate,
    )

    summary = train_brax_subset(
        brax_root=data_root,
        subset_dir=subset_path,
        output_dir=output_path,
        epochs=epochs,
        batch_size=batch_size,
        learning_rate=args.learning_rate,
        num_workers=args.num_workers,
        finetune_from=args.finetune_from,
        resume_from=args.resume,
        require_checkpoint=args.require_checkpoint and args.resume is None,
        unfreeze_last_block=args.unfreeze_last_block,
        early_stopping_patience=args.early_stopping_patience,
    )

    print_post_training_summary(summary)
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        raise SystemExit(1)
