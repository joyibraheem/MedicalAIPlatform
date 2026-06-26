"""
Convert BRAX spreadsheet rows into CheXNet multi-label vectors.
"""
from __future__ import annotations

from typing import Any, Mapping

from brax_config import (
    BRAX_LABEL_COLUMNS,
    BRAX_TO_CHEXNET,
    chexnet_class_index,
    chexnet_label_vector,
)


def _normalize_key(key: str) -> str:
    return " ".join(key.strip().lower().replace("_", " ").replace(".", " ").split())


def build_column_lookup(fieldnames: list[str]) -> dict[str, str]:
    """Map normalized header → actual CSV header."""
    lookup: dict[str, str] = {}
    for name in fieldnames:
        lookup[_normalize_key(name)] = name
    return lookup


def resolve_column(lookup: Mapping[str, str], *candidates: str) -> str | None:
    for candidate in candidates:
        key = _normalize_key(candidate)
        if key in lookup:
            return lookup[key]
    return None


def brax_value_to_int(raw: Any) -> int | None:
    if raw is None:
        return None
    text = str(raw).strip()
    if not text:
        return None
    try:
        return int(float(text))
    except ValueError:
        return None


def row_to_chexnet_labels(row: Mapping[str, Any], lookup: Mapping[str, str]) -> list[int]:
    labels = chexnet_label_vector()

    for brax_label in BRAX_LABEL_COLUMNS:
        col = resolve_column(lookup, brax_label)
        if not col:
            continue
        value = brax_value_to_int(row.get(col))
        if value != 1:
            continue
        for chex_name in BRAX_TO_CHEXNET.get(brax_label, []):
            labels[chexnet_class_index(chex_name)] = 1

    return labels


def format_chexnet_list_line(relative_image_path: str, labels: list[int]) -> str:
    rel = relative_image_path.replace("\\", "/").lstrip("/")
    return " ".join([rel, *[str(v) for v in labels]])
