"""
BRAX dataset validation, ZIP extraction, and pre-training environment checks.
"""
from __future__ import annotations

import csv
import json
import shutil
import sys
import zipfile
from collections import Counter
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Any

from brax_config import (
    CHEXNET_CHECKPOINT_NAME,
    DICOM_FOLDER_NAME,
    MIN_FREE_DISK_GB,
    REQUIRED_PACKAGES,
    chexnet_checkpoint_path,
    model_versions_dir,
    resolve_brax_data_root,
    resolve_spreadsheet_path,
)
from brax_io_utils import path_is_file, safe_makedirs, safe_open
from brax_labels import build_column_lookup, row_to_chexnet_labels
from brax_path_resolver import resolve_brax_image_path
from model import CLASS_NAMES, N_CLASSES


@dataclass
class ZipExtractResult:
    archive: str
    extracted_files: int
    skipped_existing: int
    success: bool
    message: str = ""


@dataclass
class DatasetValidationReport:
    brax_root: str
    data_root: str
    csv_exists: bool = False
    dicom_folder_exists: bool = False
    structure_valid: bool = False
    total_rows: int = 0
    valid_rows: int = 0
    missing_images: int = 0
    duplicated_paths: int = 0
    duplicate_path_examples: list[str] = field(default_factory=list)
    label_statistics: dict[str, int] = field(default_factory=dict)
    chexnet_label_statistics: dict[str, int] = field(default_factory=dict)
    zip_extractions: list[dict[str, Any]] = field(default_factory=list)
    errors: list[str] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)


@dataclass
class EnvironmentValidationReport:
    packages_ok: bool = False
    package_status: dict[str, str] = field(default_factory=dict)
    chexnet_checkpoint_exists: bool = False
    chexnet_checkpoint_path: str = ""
    cuda_available: bool = False
    cuda_device_name: str | None = None
    device: str = "cpu"
    free_disk_gb: float = 0.0
    disk_ok: bool = False
    model_versions_dir: str = ""
    errors: list[str] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)


def extract_zip_archives(brax_root: Path, *, dry_run: bool = False) -> list[ZipExtractResult]:
    """
    Extract ZIP archives found under brax_root.

    Preserves folder structure. Skips files that already exist on disk.
    """
    results: list[ZipExtractResult] = []
    root = brax_root.resolve()
    if not root.is_dir():
        return results

    for archive in sorted(root.glob("*.zip")):
        extracted = 0
        skipped = 0
        try:
            with zipfile.ZipFile(archive, "r") as zf:
                for member in zf.infolist():
                    if member.is_dir():
                        continue
                    target = root / member.filename
                    if path_is_file(target):
                        skipped += 1
                        continue
                    if dry_run:
                        extracted += 1
                        continue
                    safe_makedirs(target.parent)
                    with zf.open(member) as src, safe_open(target, "wb") as dst:
                        shutil.copyfileobj(src, dst)
                    extracted += 1
            results.append(
                ZipExtractResult(
                    archive=archive.name,
                    extracted_files=extracted,
                    skipped_existing=skipped,
                    success=True,
                    message="extracted" if not dry_run else "dry-run",
                )
            )
        except (zipfile.BadZipFile, OSError) as exc:
            results.append(
                ZipExtractResult(
                    archive=archive.name,
                    extracted_files=0,
                    skipped_existing=0,
                    success=False,
                    message=str(exc),
                )
            )
    return results


def _load_spreadsheet(brax_root: Path, data_root: Path) -> tuple[list[dict[str, str]], dict[str, str], Path]:
    csv_path = resolve_spreadsheet_path(brax_root, data_root)

    with open(csv_path, "r", encoding="utf-8-sig", newline="") as f:
        reader = csv.DictReader(f)
        if not reader.fieldnames:
            raise ValueError(f"{csv_path} has no header row.")
        lookup = build_column_lookup(list(reader.fieldnames))
        rows = [dict(row) for row in reader]
    return rows, lookup, csv_path


def validate_brax_dataset(
    brax_root: Path,
    *,
    extract_zips: bool = True,
    prefer_dicom: bool = True,
) -> DatasetValidationReport:
    """Verify BRAX layout and CSV → DICOM mapping."""
    root = brax_root.resolve()
    report = DatasetValidationReport(brax_root=str(root), data_root=str(root))

    if extract_zips:
        for result in extract_zip_archives(root):
            report.zip_extractions.append(asdict(result))
            if not result.success:
                report.errors.append(f"ZIP extraction failed for {result.archive}: {result.message}")

    data_root = resolve_brax_data_root(root)
    report.data_root = str(data_root)

    csv_path = resolve_spreadsheet_path(root, data_root)
    report.csv_exists = csv_path.is_file()
    dicom_path = data_root / DICOM_FOLDER_NAME
    report.dicom_folder_exists = dicom_path.is_dir()
    report.structure_valid = report.csv_exists and report.dicom_folder_exists

    if not report.csv_exists:
        report.errors.append(f"Missing CSV: {csv_path}")
    if not report.dicom_folder_exists:
        report.errors.append(f"Missing DICOM folder: {dicom_path}")

    if not report.structure_valid:
        return report

    rows, lookup, _ = _load_spreadsheet(root, data_root)
    report.total_rows = len(rows)

    path_counts: Counter[str] = Counter()
    brax_label_stats: Counter[str] = Counter()
    chexnet_stats: Counter[str] = Counter()
    missing_examples: list[str] = []

    for idx, row in enumerate(rows):
        rel_path, _abs_path, _kind = resolve_brax_image_path(
            row, lookup, data_root, prefer_dicom=prefer_dicom
        )
        if not rel_path:
            report.missing_images += 1
            if len(missing_examples) < 5:
                missing_examples.append(f"row {idx}")
            continue

        report.valid_rows += 1
        path_counts[rel_path] += 1

        labels = row_to_chexnet_labels(row, lookup)
        for ci, value in enumerate(labels):
            if value == 1:
                chexnet_stats[CLASS_NAMES[ci]] += 1

        for brax_label in lookup.values():
            raw = row.get(brax_label, "")
            if str(raw).strip() in ("1", "1.0"):
                brax_label_stats[brax_label] += 1

    dup_paths = [p for p, c in path_counts.items() if c > 1]
    report.duplicated_paths = len(dup_paths)
    report.duplicate_path_examples = dup_paths[:10]
    report.label_statistics = dict(sorted(brax_label_stats.items()))
    report.chexnet_label_statistics = {name: chexnet_stats.get(name, 0) for name in CLASS_NAMES}

    if missing_examples:
        report.warnings.append(
            f"First missing-image rows (of {report.missing_images}): {', '.join(missing_examples)}"
        )
    if report.valid_rows == 0:
        report.errors.append("No CSV rows resolve to existing DICOM files.")

    return report


def validate_training_environment(brax_root: Path) -> EnvironmentValidationReport:
    """Check packages, checkpoint, disk space, and compute device."""
    report = EnvironmentValidationReport()
    report.chexnet_checkpoint_path = str(chexnet_checkpoint_path())
    report.chexnet_checkpoint_exists = chexnet_checkpoint_path().is_file()
    report.model_versions_dir = str(model_versions_dir())

    for pkg in REQUIRED_PACKAGES:
        try:
            if pkg == "PIL":
                import PIL  # noqa: F401

                report.package_status[pkg] = "ok"
            elif pkg == "sklearn":
                import sklearn  # noqa: F401

                report.package_status[pkg] = "ok"
            else:
                __import__(pkg)
                report.package_status[pkg] = "ok"
        except ImportError as exc:
            report.package_status[pkg] = f"missing ({exc})"
            report.errors.append(f"Required package missing: {pkg}")

    report.packages_ok = all(v == "ok" for v in report.package_status.values())

    try:
        import torch

        report.cuda_available = torch.cuda.is_available()
        report.device = "cuda" if report.cuda_available else "cpu"
        if report.cuda_available:
            report.cuda_device_name = torch.cuda.get_device_name(0)
    except ImportError:
        report.errors.append("PyTorch is not installed.")

    usage = shutil.disk_usage(brax_root.resolve())
    report.free_disk_gb = round(usage.free / (1024**3), 2)
    report.disk_ok = report.free_disk_gb >= MIN_FREE_DISK_GB
    if not report.disk_ok:
        report.warnings.append(
            f"Low disk space: {report.free_disk_gb} GB free (recommended >= {MIN_FREE_DISK_GB} GB)."
        )

    if not report.chexnet_checkpoint_exists:
        report.warnings.append(
            f"{CHEXNET_CHECKPOINT_NAME} not found — fine-tuning will fall back to ImageNet backbone only."
        )

    model_versions_dir().mkdir(parents=True, exist_ok=True)
    return report


def run_full_validation(
    brax_root: Path,
    *,
    extract_zips: bool = True,
    prefer_dicom: bool = True,
) -> tuple[DatasetValidationReport, EnvironmentValidationReport, bool]:
    """Run dataset + environment validation. Returns (dataset_report, env_report, ready)."""
    dataset_report = validate_brax_dataset(
        brax_root, extract_zips=extract_zips, prefer_dicom=prefer_dicom
    )
    env_report = validate_training_environment(brax_root)

    ready = (
        dataset_report.structure_valid
        and dataset_report.valid_rows > 0
        and env_report.packages_ok
        and env_report.disk_ok
    )
    return dataset_report, env_report, ready


def print_validation_report(
    dataset_report: DatasetValidationReport,
    env_report: EnvironmentValidationReport,
    *,
    ready: bool | None = None,
) -> None:
    """Print a human-readable validation report to stdout."""
    print("=" * 72)
    print("BRAX PRE-TRAINING VALIDATION REPORT")
    print("=" * 72)

    print("\n[Dataset structure]")
    print(f"  BRAX root:     {dataset_report.brax_root}")
    print(f"  Data root:     {dataset_report.data_root}")
    print(f"  CSV exists:    {dataset_report.csv_exists}")
    print(f"  DICOM folder:  {dataset_report.dicom_folder_exists}")
    print(f"  Structure OK:  {dataset_report.structure_valid}")

    if dataset_report.zip_extractions:
        print("\n[ZIP extraction]")
        for item in dataset_report.zip_extractions:
            print(
                f"  {item['archive']}: success={item['success']} "
                f"extracted={item['extracted_files']} skipped={item['skipped_existing']}"
            )

    print("\n[CSV -> DICOM mapping]")
    print(f"  Total rows:        {dataset_report.total_rows}")
    print(f"  Valid rows:        {dataset_report.valid_rows}")
    print(f"  Missing images:    {dataset_report.missing_images}")
    print(f"  Duplicated paths:  {dataset_report.duplicated_paths}")
    if dataset_report.duplicate_path_examples:
        print("  Duplicate examples:")
        for path in dataset_report.duplicate_path_examples[:5]:
            print(f"    - {path}")

    print("\n[CheXNet label counts (mapped positives)]")
    for name in CLASS_NAMES:
        count = dataset_report.chexnet_label_statistics.get(name, 0)
        print(f"  {name:20s} {count}")

    print("\n[Environment]")
    print(f"  Packages OK:       {env_report.packages_ok}")
    for pkg, status in env_report.package_status.items():
        print(f"    {pkg:12s} {status}")
    print(f"  Checkpoint:        {env_report.chexnet_checkpoint_path}")
    print(f"  Checkpoint exists: {env_report.chexnet_checkpoint_exists}")
    print(f"  Device:            {env_report.device}")
    if env_report.cuda_device_name:
        print(f"  GPU:               {env_report.cuda_device_name}")
    print(f"  Free disk (GB):    {env_report.free_disk_gb}")
    print(f"  Disk OK:           {env_report.disk_ok}")
    print(f"  model_versions/:   {env_report.model_versions_dir}")

    all_errors = dataset_report.errors + env_report.errors
    all_warnings = dataset_report.warnings + env_report.warnings

    if all_warnings:
        print("\n[Warnings]")
        for msg in all_warnings:
            print(f"  - {msg}")

    if all_errors:
        print("\n[Errors]")
        for msg in all_errors:
            print(f"  - {msg}")

    if ready is not None:
        print("\n" + "=" * 72)
        print(f"READY FOR TRAINING: {'YES' if ready else 'NO'}")
        print("=" * 72)


def save_validation_report(
    output_path: Path,
    dataset_report: DatasetValidationReport,
    env_report: EnvironmentValidationReport,
    *,
    ready: bool,
) -> None:
    payload = {
        "ready_for_training": ready,
        "dataset": dataset_report.to_dict(),
        "environment": env_report.to_dict(),
    }
    output_path.parent.mkdir(parents=True, exist_ok=True)
    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(payload, f, indent=2)
