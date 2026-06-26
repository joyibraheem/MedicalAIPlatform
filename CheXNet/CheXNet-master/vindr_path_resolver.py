"""
Resolve VinDr-PCXR image_id values to files under train/.
"""
from __future__ import annotations

from pathlib import Path

from vindr_pcxr_config import IMAGE_ID_EXTENSIONS, TRAIN_IMAGES_DIR


def resolve_vindr_image_path(vindr_root: Path, image_id: str) -> tuple[str | None, str | None]:
    """
    Return (path relative to vindr_root, absolute path) for a VinDr image_id.

    Expected layout:
      {vindr_root}/train/{image_id}.dicom
    Also tries .dcm, .png, and rglob fallback for nested train folders.
    """
    root = vindr_root.resolve()
    image_id = image_id.strip()
    if not image_id:
        return None, None

    train_dir = root / TRAIN_IMAGES_DIR
    if not train_dir.is_dir():
        return None, None

    candidates: list[Path] = []

    # If image_id already includes an extension.
    candidates.append(train_dir / image_id)

    stem = Path(image_id).stem if "." in image_id else image_id
    for ext in IMAGE_ID_EXTENSIONS:
        candidates.append(train_dir / f"{stem}{ext}")
        candidates.append(train_dir / f"{image_id}{ext}")

    seen: set[Path] = set()
    for candidate in candidates:
        resolved = candidate.resolve()
        if resolved in seen:
            continue
        seen.add(resolved)
        if resolved.is_file():
            rel = resolved.relative_to(root).as_posix()
            return rel, str(resolved)

    # Fallback: some downloads nest DICOM files in subfolders under train/.
    patterns = (f"{stem}.dicom", f"{stem}.dcm", f"{image_id}.dicom", f"{image_id}.dcm", stem, image_id)
    for pattern in patterns:
        for match in train_dir.rglob(pattern):
            if match.is_file():
                rel = match.resolve().relative_to(root).as_posix()
                return rel, str(match.resolve())

    return None, None
