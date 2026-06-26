"""
Parse VinDr-PCXR image_labels CSV rows and map to CheXNet 14-label vectors.

VinDr-PCXR image_labels_*.csv columns:
  - image_id
  - rad_id (aliases: rad_ID)
  - labels  (binary vector as {0,1,0,...} or [0,1,0,...])

The label vector has 51 entries: 36 local findings + 15 global diagnoses
(PediCXR / VinDr-PCXR paper Table 3 order).
"""
from __future__ import annotations

import ast
import csv
import re
from typing import Any, Mapping

from vindr_pcxr_config import chexnet_class_index, chexnet_label_vector

# Official 51-label order (local findings 1-36, then global diagnoses).
VINDR_PCXR_LABEL_NAMES: tuple[str, ...] = (
    "Boot-shaped heart",
    "Peribronchovascular interstitial opacity or PIO",
    "Reticulonodular opacity",
    "Bronchial thickening",
    "Enlarged PA",
    "Cardiomegaly",
    "Other opacity",
    "Intrathoracic digestive structure",
    "Diffuse alveolar opacity",
    "Other lesion",
    "Consolidation",
    "Mediastinal shift",
    "Anterior mediastinal mass",
    "Other nodule/mass",
    "Dextro cardia",
    "Aortic enlargement",
    "Pleural effusion",
    "Stomach on the right side",
    "Atelectasis",
    "Calcification",
    "Interstitial lung disease - ILD",
    "Lung hyperinflation",
    "Egg on string sign",
    "Pulmonary fibrosis",
    "Infiltration",
    "Lung cavity",
    "Pneumothorax",
    "Edema",
    "Pleural thickening",
    "Clavicle fracture",
    "Chest wall mass",
    "Lung cyst",
    "Emphysema",
    "Bronchiectasis",
    "Expanded edges of the anterior ribs",
    "Paravertebral mass",
    "No finding",
    "Bronchitis",
    "Broncho-pneumonia",
    "Other diseases",
    "Bronchiolitis",
    "Situs inversus",
    "Pneumonia",
    "Pleuro-pneumonia",
    "Diaphragmatic hernia",
    "Tuberculosis",
    "Congenital emphysema",
    "CPAM",
    "Hyaline membrane disease",
    "Mediastinal tumor",
    "Lung tumor",
)

# Map VinDr vector index -> CheXNet class name(s). Only positive (1) values apply.
# CheXNet labels without a VinDr equivalent stay 0 (e.g. Hernia).
VINDR_INDEX_TO_CHEXNET: dict[int, list[str]] = {
    1: ["Infiltration"],  # PIO
    2: ["Infiltration"],  # Reticulonodular opacity
    5: ["Cardiomegaly"],
    6: ["Infiltration"],  # Other opacity
    8: ["Infiltration"],  # Diffuse alveolar opacity
    9: ["Mass"],
    10: ["Consolidation"],
    12: ["Mass"],  # Anterior mediastinal mass
    13: ["Mass", "Nodule"],
    16: ["Effusion"],  # Pleural effusion
    18: ["Atelectasis"],
    23: ["Fibrosis"],
    24: ["Infiltration"],
    26: ["Pneumothorax"],
    27: ["Edema"],
    28: ["Pleural_Thickening"],
    30: ["Mass"],  # Chest wall mass
    32: ["Emphysema"],
    38: ["Pneumonia"],  # Broncho-pneumonia
    42: ["Pneumonia"],
    43: ["Pneumonia"],  # Pleuro-pneumonia
    50: ["Mass"],  # Lung tumor
}


def _normalize_key(key: str) -> str:
    return " ".join(key.strip().lower().replace("_", " ").split())


def build_column_lookup(fieldnames: list[str]) -> dict[str, str]:
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


def parse_labels_field(raw: Any) -> list[int]:
    if raw is None:
        return []
    text = str(raw).strip()
    if not text:
        return []

    if text.startswith("{") and text.endswith("}"):
        text = "[" + text[1:-1] + "]"

    try:
        parsed = ast.literal_eval(text)
        if isinstance(parsed, (list, tuple)):
            return [int(float(x)) for x in parsed]
    except (SyntaxError, ValueError, TypeError):
        pass

    parts = re.split(r"[,\s]+", text.strip("[]{} "))
    out: list[int] = []
    for part in parts:
        part = part.strip()
        if not part:
            continue
        out.append(int(float(part)))
    return out


def aggregate_label_vectors(vectors: list[list[int]]) -> list[int]:
    if not vectors:
        return [0] * len(VINDR_PCXR_LABEL_NAMES)

    length = max(len(v) for v in vectors)
    if length != len(VINDR_PCXR_LABEL_NAMES):
        # Tolerate minor export differences; pad/truncate to expected length.
        length = max(length, len(VINDR_PCXR_LABEL_NAMES))

    merged = [0] * length
    for vec in vectors:
        for i, value in enumerate(vec):
            if value == 1:
                merged[i] = 1
    return merged


def vindr_vector_to_chexnet(vindr_vector: list[int]) -> list[int]:
    labels = chexnet_label_vector()
    for idx, value in enumerate(vindr_vector):
        if value != 1:
            continue
        for chex_name in VINDR_INDEX_TO_CHEXNET.get(idx, []):
            labels[chexnet_class_index(chex_name)] = 1
    return labels


def format_chexnet_list_line(relative_image_path: str, labels: list[int]) -> str:
    rel = relative_image_path.replace("\\", "/").lstrip("/")
    return " ".join([rel, *[str(v) for v in labels]])


def load_image_label_rows(csv_path: str) -> tuple[list[dict[str, str]], dict[str, str]]:
    with open(csv_path, "r", encoding="utf-8-sig", newline="") as f:
        reader = csv.DictReader(f)
        if not reader.fieldnames:
            raise ValueError(f"{csv_path} has no header row.")
        lookup = build_column_lookup(list(reader.fieldnames))
        rows = [dict(row) for row in reader]
    return rows, lookup


def group_labels_by_image_id(
    rows: list[dict[str, str]],
    lookup: Mapping[str, str],
) -> dict[str, list[int]]:
    image_col = resolve_column(lookup, "image_id", "Image ID", "image id")
    labels_col = resolve_column(lookup, "labels", "Labels")
    if not image_col or not labels_col:
        raise ValueError("CSV must contain image_id and labels columns.")

    grouped: dict[str, list[list[int]]] = {}
    for row in rows:
        image_id = (row.get(image_col) or "").strip()
        if not image_id:
            continue
        vec = parse_labels_field(row.get(labels_col))
        grouped.setdefault(image_id, []).append(vec)

    return {image_id: aggregate_label_vectors(vecs) for image_id, vecs in grouped.items()}
