"""
Resolve BRAX spreadsheet rows to image files under brax_root.

Primary use case: DicomPath -> Anonymized_DICOMs/.../*.dcm
Optional fallback: PngPath -> images/.../*.png
"""
from __future__ import annotations

from pathlib import Path

from brax_config import DICOM_FOLDER_NAME, FALLBACK_IMAGE_PATH_COLUMNS, IMAGE_PATH_COLUMNS, PNG_FOLDER_NAME
from brax_io_utils import path_is_file
from brax_labels import resolve_column


def _normalize_rel(raw: str) -> str:
    return raw.replace("\\", "/").strip().lstrip("/")


def _try_paths(brax_root: Path, rel: str) -> tuple[str, str] | None:
    """Return (relative_to_brax_root, absolute_path) when a file exists."""
    root = brax_root.resolve()
    rel = _normalize_rel(rel)
    if not rel:
        return None

    candidates: list[Path] = [root / rel]

    if not rel.lower().startswith(f"{DICOM_FOLDER_NAME.lower()}/"):
        candidates.append(root / DICOM_FOLDER_NAME / rel)

    if rel.lower().startswith("id_"):
        candidates.append(root / DICOM_FOLDER_NAME / rel)

    if not rel.lower().startswith(f"{PNG_FOLDER_NAME.lower()}/"):
        candidates.append(root / PNG_FOLDER_NAME / rel)

    seen: set[Path] = set()
    for candidate in candidates:
        resolved = candidate.resolve()
        if resolved in seen:
            continue
        seen.add(resolved)
        if path_is_file(resolved):
            rel_from_root = resolved.relative_to(root).as_posix()
            return rel_from_root, str(resolved)

    return None


def collect_path_candidates(
    row: dict[str, str],
    lookup: dict[str, str],
    *,
    prefer_dicom: bool,
) -> list[str]:
    dicom_cols = []
    png_cols = []
    for col_name in FALLBACK_IMAGE_PATH_COLUMNS:
        col = resolve_column(lookup, col_name)
        if col and row.get(col, "").strip():
            dicom_cols.append(row[col].strip())
    for col_name in IMAGE_PATH_COLUMNS:
        col = resolve_column(lookup, col_name)
        if col and row.get(col, "").strip():
            png_cols.append(row[col].strip())

    if prefer_dicom:
        return dicom_cols + png_cols
    return png_cols + dicom_cols


def resolve_brax_image_path(
    row: dict[str, str],
    lookup: dict[str, str],
    brax_root: Path,
    *,
    prefer_dicom: bool = True,
) -> tuple[str | None, str | None, str | None]:
    """
    Resolve one spreadsheet row to an on-disk image.

    Returns:
        relative_path, absolute_path, source_column_kind ('dicom'|'png'|None)
    """
    dicom_cols = {resolve_column(lookup, name) for name in FALLBACK_IMAGE_PATH_COLUMNS}
    dicom_cols.discard(None)

    for raw in collect_path_candidates(row, lookup, prefer_dicom=prefer_dicom):
        resolved = _try_paths(brax_root, raw)
        if not resolved:
            continue
        rel_from_root, abs_path = resolved
        col_used = None
        for col_name in FALLBACK_IMAGE_PATH_COLUMNS:
            col = resolve_column(lookup, col_name)
            if col and row.get(col, "").strip() == raw:
                col_used = col
                break
        kind = "dicom" if col_used in dicom_cols else "png"
        if rel_from_root.lower().endswith(".dcm"):
            kind = "dicom"
        return rel_from_root, abs_path, kind

    return None, None, None
