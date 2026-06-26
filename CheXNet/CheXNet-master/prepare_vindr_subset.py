#!/usr/bin/env python3
"""
Build fixed-size VinDr-PCXR subsets for CheXNet training.

Usage:
  python prepare_vindr_subset.py --size 100
  python prepare_vindr_subset.py --size 1000
  python prepare_vindr_subset.py --size 3000
"""
from __future__ import annotations

import argparse
import csv
import json
import random
import sys
from pathlib import Path

from vindr_pcxr_config import (
    ALLOWED_SUBSET_SIZES,
    DEFAULT_RANDOM_SEED,
    DEFAULT_VAL_RATIO,
    DEFAULT_VINDR_ROOT,
    IMAGE_LABELS_TRAIN,
    PIPELINE_NAME,
    TRAIN_IMAGES_DIR,
    resolve_vindr_data_root,
    subset_dir,
)
from vindr_pcxr_labels import (
    format_chexnet_list_line,
    group_labels_by_image_id,
    load_image_label_rows,
    vindr_vector_to_chexnet,
)
from vindr_path_resolver import resolve_vindr_image_path


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Prepare a VinDr-PCXR subset for CheXNet training.")
    parser.add_argument(
        "--size",
        type=int,
        required=True,
        choices=ALLOWED_SUBSET_SIZES,
        help="Number of images to include (100, 1000, or 3000).",
    )
    parser.add_argument(
        "--vindr-root",
        type=Path,
        default=DEFAULT_VINDR_ROOT,
        help=f"Root folder containing VinDr-PCXR files (default: {DEFAULT_VINDR_ROOT}).",
    )
    parser.add_argument(
        "--labels-csv",
        type=Path,
        default=None,
        help=f"Override labels CSV (default: {{vindr_root}}/{IMAGE_LABELS_TRAIN}).",
    )
    parser.add_argument(
        "--seed",
        type=int,
        default=DEFAULT_RANDOM_SEED,
        help="Random seed for reproducible sampling.",
    )
    parser.add_argument(
        "--val-ratio",
        type=float,
        default=DEFAULT_VAL_RATIO,
        help="Validation split ratio inside the subset.",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    vindr_root = args.vindr_root.resolve()
    data_root = resolve_vindr_data_root(vindr_root)
    labels_csv = (args.labels_csv or (data_root / IMAGE_LABELS_TRAIN)).resolve()

    if data_root != vindr_root:
        print(f"Using nested VinDr data folder: {data_root}")

    if not labels_csv.is_file():
        raise FileNotFoundError(
            f"Missing {labels_csv}. Place VinDr-PCXR under {vindr_root} with {IMAGE_LABELS_TRAIN}."
        )

    train_dir = data_root / TRAIN_IMAGES_DIR
    if not train_dir.is_dir():
        raise FileNotFoundError(
            f"Missing training images folder: {train_dir}. Expected DICOM files under train/."
        )

    rows, lookup = load_image_label_rows(str(labels_csv))
    labels_by_image = group_labels_by_image_id(rows, lookup)

    usable: list[dict[str, object]] = []
    missing_images = 0
    for image_id, vindr_vector in labels_by_image.items():
        rel_path, abs_path = resolve_vindr_image_path(data_root, image_id)
        if not rel_path:
            missing_images += 1
            continue
        chex_labels = vindr_vector_to_chexnet(vindr_vector)
        usable.append(
            {
                "image_id": image_id,
                "relative_image_path": rel_path,
                "absolute_image_path": abs_path,
                "chexnet_labels": chex_labels,
                "list_line": format_chexnet_list_line(rel_path, chex_labels),
            }
        )

    if not usable:
        raise RuntimeError(
            f"No usable rows found. Check {labels_csv} and DICOM files under {train_dir}."
        )

    random.seed(args.seed)
    random.shuffle(usable)
    selected = usable[: min(args.size, len(usable))]

    val_count = max(1, int(len(selected) * args.val_ratio)) if len(selected) >= 5 else max(0, len(selected) - 1)
    if len(selected) <= 1:
        val_count = 0
    val_rows = selected[:val_count]
    train_rows = selected[val_count:]

    out_dir = subset_dir(vindr_root, args.size)
    out_dir.mkdir(parents=True, exist_ok=True)

    train_list_path = out_dir / "train_list.txt"
    val_list_path = out_dir / "val_list.txt"
    subset_csv_path = out_dir / "subset.csv"
    meta_path = out_dir / "meta.json"

    with open(train_list_path, "w", encoding="utf-8", newline="") as f:
        for item in train_rows:
            f.write(item["list_line"] + "\n")

    with open(val_list_path, "w", encoding="utf-8", newline="") as f:
        for item in val_rows:
            f.write(item["list_line"] + "\n")

    with open(subset_csv_path, "w", encoding="utf-8", newline="") as f:
        writer = csv.DictWriter(
            f,
            fieldnames=["image_id", "relative_image_path", "absolute_image_path", "chexnet_labels"],
        )
        writer.writeheader()
        for item in selected:
            writer.writerow(
                {
                    "image_id": item["image_id"],
                    "relative_image_path": item["relative_image_path"],
                    "absolute_image_path": item["absolute_image_path"],
                    "chexnet_labels": " ".join(str(v) for v in item["chexnet_labels"]),
                }
            )

    meta = {
        "pipeline": PIPELINE_NAME,
        "subset_size_requested": args.size,
        "subset_size_selected": len(selected),
        "train_count": len(train_rows),
        "val_count": len(val_rows),
        "usable_images_in_labels_csv": len(usable),
        "missing_images_in_labels_csv": missing_images,
        "random_seed": args.seed,
        "val_ratio": args.val_ratio,
        "vindr_root": str(vindr_root),
        "data_root": str(data_root),
        "labels_csv": str(labels_csv),
        "train_list": str(train_list_path),
        "val_list": str(val_list_path),
        "subset_csv": str(subset_csv_path),
        "data_dir_for_training": str(data_root),
    }
    with open(meta_path, "w", encoding="utf-8") as f:
        json.dump(meta, f, indent=2)

    print(f"Prepared subset_{args.size} at {out_dir}")
    print(f"  train: {len(train_rows)}  val: {len(val_rows)}  (usable pool: {len(usable)})")
    print(f"  train_list: {train_list_path}")
    print(f"  val_list:   {val_list_path}")
    if len(selected) < args.size:
        print(
            f"WARNING: requested {args.size} but only {len(selected)} usable images were available.",
            file=sys.stderr,
        )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        raise SystemExit(1)
