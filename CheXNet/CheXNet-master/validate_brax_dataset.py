#!/usr/bin/env python3
"""
Validate BRAX dataset layout, CSV→DICOM mapping, and training environment.

Usage:
  python validate_brax_dataset.py
  python validate_brax_dataset.py --brax-root datasets/BRAX
  python validate_brax_dataset.py --no-extract-zips
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

from brax_config import DEFAULT_BRAX_ROOT
from brax_data_validation import (
    print_validation_report,
    run_full_validation,
    save_validation_report,
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Validate BRAX dataset and training prerequisites.")
    parser.add_argument(
        "--brax-root",
        type=Path,
        default=DEFAULT_BRAX_ROOT,
        help=f"BRAX root folder (default: {DEFAULT_BRAX_ROOT}).",
    )
    parser.add_argument(
        "--no-extract-zips",
        action="store_true",
        help="Skip automatic ZIP extraction under brax-root.",
    )
    parser.add_argument(
        "--save-report",
        type=Path,
        default=None,
        help="Optional JSON path for the validation report.",
    )
    parser.add_argument(
        "--prefer-dicom",
        action=argparse.BooleanOptionalAction,
        default=True,
        help="Resolve DicomPath before PNG fallback (default: true).",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    brax_root = args.brax_root.resolve()

    dataset_report, env_report, ready = run_full_validation(
        brax_root,
        extract_zips=not args.no_extract_zips,
        prefer_dicom=args.prefer_dicom,
    )
    print_validation_report(dataset_report, env_report, ready=ready)

    if args.save_report:
        save_validation_report(args.save_report, dataset_report, env_report, ready=ready)
        print(f"\nSaved report: {args.save_report}")

    return 0 if ready else 1


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        raise SystemExit(1)
