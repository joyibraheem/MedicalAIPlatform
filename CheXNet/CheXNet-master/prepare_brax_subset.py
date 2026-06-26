#!/usr/bin/env python3
"""
Build fixed-size BRAX subsets for CheXNet training.

Usage:
  python prepare_brax_subset.py --size 100
  python prepare_brax_subset.py --size 1000
  python prepare_brax_subset.py --size 3000
"""
from __future__ import annotations

import argparse
import csv
import json
import sys
from pathlib import Path

from brax_config import (
    ALLOWED_SUBSET_SIZES,
    DEFAULT_BRAX_ROOT,
    DEFAULT_RANDOM_SEED,
    DEFAULT_VAL_RATIO,
    DICOM_FOLDER_NAME,
    PIPELINE_NAME,
    resolve_brax_data_root,
    resolve_spreadsheet_path,
    subset_dir,
)
from brax_labels import (
    build_column_lookup,
    format_chexnet_list_line,
    row_to_chexnet_labels,
)
from brax_path_resolver import resolve_brax_image_path
from brax_subset_sampler import (
    SplitValidationError,
    print_split_report,
    select_diverse_subset,
    split_class_statistics,
    split_train_val,
    subset_label_coverage,
    verify_split_coverage,
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Prepare a BRAX subset for CheXNet training.")
    parser.add_argument(
        "--size",
        type=int,
        required=True,
        choices=ALLOWED_SUBSET_SIZES,
        help="Number of studies/images to include (100, 300, 600, 1000, or 3000).",
    )
    parser.add_argument(
        "--brax-root",
        type=Path,
        default=DEFAULT_BRAX_ROOT,
        help=f"Root folder containing BRAX files (default: {DEFAULT_BRAX_ROOT}).",
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
    parser.add_argument(
        "--prefer-dicom",
        action=argparse.BooleanOptionalAction,
        default=True,
        help="Prefer DicomPath over PngPath (default: true).",
    )
    return parser.parse_args()


def load_spreadsheet(brax_root: Path, data_root: Path) -> tuple[list[dict[str, str]], dict[str, str]]:
    csv_path = resolve_spreadsheet_path(brax_root, data_root)

    with open(csv_path, "r", encoding="utf-8-sig", newline="") as f:
        reader = csv.DictReader(f)
        if not reader.fieldnames:
            raise ValueError(f"{csv_path} has no header row.")
        lookup = build_column_lookup(list(reader.fieldnames))
        rows = [dict(row) for row in reader]

    if not rows:
        raise ValueError(f"{csv_path} contains no data rows.")

    return rows, lookup


def main() -> int:
    args = parse_args()
    brax_root = args.brax_root.resolve()
    data_root = resolve_brax_data_root(brax_root)
    out_dir = subset_dir(brax_root, args.size)
    out_dir.mkdir(parents=True, exist_ok=True)

    if data_root != brax_root:
        print(f"Using nested BRAX data folder: {data_root}")

    rows, lookup = load_spreadsheet(brax_root, data_root)

    usable: list[dict[str, object]] = []
    missing_images = 0
    for idx, row in enumerate(rows):
        rel_path, abs_path, _kind = resolve_brax_image_path(
            row,
            lookup,
            data_root,
            prefer_dicom=args.prefer_dicom,
        )
        if not rel_path:
            missing_images += 1
            continue
        labels = row_to_chexnet_labels(row, lookup)
        usable.append(
            {
                "row_index": idx,
                "relative_image_path": rel_path,
                "absolute_image_path": abs_path,
                "labels": labels,
                "list_line": format_chexnet_list_line(rel_path, labels),
                "source_row": row,
            }
        )

    if not usable:
        raise RuntimeError(
            "No usable rows found. Ensure DICOM files exist under "
            f"the BRAX spreadsheet and DicomPath values are correct."
        )

    selected = select_diverse_subset(usable, min(args.size, len(usable)), seed=args.seed)
    train_rows, val_rows, split_method = split_train_val(selected, args.val_ratio, seed=args.seed)
    verify_split_coverage(train_rows, val_rows)
    print_split_report(train_rows, val_rows)

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

    fieldnames = list(rows[0].keys()) + ["relative_image_path", "absolute_image_path"]
    with open(subset_csv_path, "w", encoding="utf-8", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames, extrasaction="ignore")
        writer.writeheader()
        for item in selected:
            merged = dict(item["source_row"])
            merged["relative_image_path"] = item["relative_image_path"]
            merged["absolute_image_path"] = item["absolute_image_path"]
            writer.writerow(merged)

    meta = {
        "pipeline": PIPELINE_NAME,
        "subset_size_requested": args.size,
        "subset_size_selected": len(selected),
        "train_count": len(train_rows),
        "val_count": len(val_rows),
        "usable_rows_in_master": len(usable),
        "missing_images_in_master": missing_images,
        "random_seed": args.seed,
        "val_ratio": args.val_ratio,
        "sampling": "class_diverse_round_robin",
        "split_method": split_method,
        "split_verified": True,
        "split_class_statistics": split_class_statistics(train_rows, val_rows),
        "train_label_coverage": subset_label_coverage(train_rows),
        "val_label_coverage": subset_label_coverage(val_rows),
        "brax_root": str(brax_root),
        "data_root": str(data_root),
        "train_list": str(train_list_path),
        "val_list": str(val_list_path),
        "subset_csv": str(subset_csv_path),
        "data_dir_for_training": str(data_root),
    }
    with open(meta_path, "w", encoding="utf-8") as f:
        json.dump(meta, f, indent=2)

    print(f"Prepared subset_{args.size} at {out_dir}")
    print(f"  Selected images:  {len(selected)}")
    print(f"  Selected studies: {len({Path(item['relative_image_path']).parts[2] for item in selected if len(Path(item['relative_image_path']).parts) > 2})}")
    print(f"  Train images:     {len(train_rows)}")
    print(f"  Val images:       {len(val_rows)}")
    print(f"  Usable pool:      {len(usable)}")
    print(f"  Output folder:    {out_dir}")
    print("  Class distribution (CheXNet positives in subset):")
    coverage = subset_label_coverage(selected)
    for name, count in coverage.items():
        if count > 0:
            print(f"    {name}: {count}")
    print(f"  train_list: {train_list_path}")
    print(f"  val_list:   {val_list_path}")
    if len(selected) < args.size:
        print(
            f"WARNING: requested {args.size} but only {len(selected)} usable rows were available.",
            file=sys.stderr,
        )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except SplitValidationError as exc:
        print(f"ABORT: {exc}", file=sys.stderr)
        raise SystemExit(2)
    except Exception as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        raise SystemExit(1)
