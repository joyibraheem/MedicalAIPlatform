"""
Windows-safe path helpers for long BRAX DICOM paths.
"""
from __future__ import annotations

import os
from pathlib import Path


def as_long_path(path: Path | str) -> Path:
    """Return a path usable for long Windows paths (>260 chars)."""
    text = str(path)
    if os.name != "nt":
        return Path(text)
    resolved = str(Path(text).resolve())
    if resolved.startswith("\\\\?\\"):
        return Path(resolved)
    return Path("\\\\?\\" + resolved)


def path_exists(path: Path) -> bool:
    try:
        return as_long_path(path).exists()
    except OSError:
        return False


def path_is_file(path: Path) -> bool:
    try:
        return as_long_path(path).is_file()
    except OSError:
        return False


def safe_makedirs(path: Path) -> None:
    as_long_path(path).mkdir(parents=True, exist_ok=True)


def safe_open(path: Path, mode: str):
    return open(as_long_path(path), mode)
