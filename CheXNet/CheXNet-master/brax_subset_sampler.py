"""
Class-diverse subset sampling for BRAX → CheXNet fine-tuning.

The original master spreadsheet is never modified; sampling only affects
generated list files under datasets/BRAX/subsets/.
"""
from __future__ import annotations

import random
from typing import Any

import numpy as np

from model import CLASS_NAMES, N_CLASSES


class SplitValidationError(RuntimeError):
    """Raised when train/validation split fails multilabel coverage checks."""


def _positive_classes(labels: list[int]) -> list[int]:
    return [i for i, v in enumerate(labels) if v == 1]


def _labels_matrix(items: list[dict[str, Any]]) -> np.ndarray:
    return np.array([item["labels"] for item in items], dtype=np.int8)


def select_diverse_subset(
    usable: list[dict[str, Any]],
    size: int,
    seed: int,
) -> list[dict[str, Any]]:
    """
    Select up to `size` rows while preserving CheXNet class diversity.

    Uses round-robin sampling across active label buckets, then fills
    remaining slots from the unused pool.
    """
    if size >= len(usable):
        return list(usable)

    rng = random.Random(seed)
    class_buckets: dict[int, list[int]] = {i: [] for i in range(N_CLASSES)}
    unlabeled: list[int] = []

    for idx, item in enumerate(usable):
        labels = item["labels"]
        active = _positive_classes(labels)
        if not active:
            unlabeled.append(idx)
            continue
        for ci in active:
            class_buckets[ci].append(idx)

    selected_indices: set[int] = set()
    selected: list[dict[str, Any]] = []

    active_classes = [ci for ci in range(N_CLASSES) if class_buckets[ci]]
    rng.shuffle(active_classes)

    while len(selected) < size and active_classes:
        progressed = False
        for ci in list(active_classes):
            if len(selected) >= size:
                break
            pool = [i for i in class_buckets[ci] if i not in selected_indices]
            if not pool:
                continue
            pick = rng.choice(pool)
            selected_indices.add(pick)
            selected.append(usable[pick])
            progressed = True
        if not progressed:
            break

    remaining = [i for i in range(len(usable)) if i not in selected_indices]
    rng.shuffle(remaining)
    for idx in remaining:
        if len(selected) >= size:
            break
        selected_indices.add(idx)
        selected.append(usable[idx])

    return selected


def _val_count_for_label(
    label_matrix: np.ndarray,
    assigned_split: np.ndarray,
    label_idx: int,
) -> int:
    val_mask = assigned_split == 1
    if not val_mask.any():
        return 0
    return int(label_matrix[val_mask, label_idx].sum())


def _can_assign_to_val(
    idx: int,
    label_matrix: np.ndarray,
    assigned_split: np.ndarray,
) -> bool:
    """True if assigning sample idx to validation leaves train positives for every label."""
    for label_idx in range(label_matrix.shape[1]):
        if label_matrix[idx, label_idx] != 1:
            continue
        train_pos_remaining = 0
        for j in range(label_matrix.shape[0]):
            if j == idx:
                continue
            if label_matrix[j, label_idx] == 1 and assigned_split[j] != 1:
                train_pos_remaining += 1
        if train_pos_remaining == 0:
            return False
    return True


def greedy_multilabel_stratified_split_indices(
    label_matrix: np.ndarray,
    val_ratio: float,
    seed: int,
) -> tuple[list[int], list[int]]:
    """
    Greedy iterative multilabel stratification (Sechidis-style).

    Assigns samples label-by-label (rarest first) so each active class is
    represented in train and validation whenever the subset has enough positives.
    """
    rng = np.random.RandomState(seed)
    n_samples, _n_labels = label_matrix.shape
    val_count = int(round(n_samples * val_ratio))
    val_count = max(1, min(val_count, n_samples - 1))

    label_totals = label_matrix.sum(axis=0)
    val_target_per_label = np.round(label_totals * val_ratio).astype(int)

    assigned_split = np.full(n_samples, -1, dtype=int)  # -1 unassigned, 0 train, 1 val
    label_order = np.argsort(label_totals)

    for label_idx in label_order:
        if label_totals[label_idx] == 0:
            continue

        candidates = np.where((label_matrix[:, label_idx] == 1) & (assigned_split == -1))[0]
        rng.shuffle(candidates)

        already_val = _val_count_for_label(label_matrix, assigned_split, label_idx)
        need_val = max(0, int(val_target_per_label[label_idx]) - already_val)

        if len(candidates) > 1 and need_val >= len(candidates):
            need_val = len(candidates) - 1
        if len(candidates) == 1:
            need_val = 0

        for i, idx in enumerate(candidates):
            if i < need_val:
                assigned_split[idx] = 1
            else:
                assigned_split[idx] = 0

    unassigned = np.where(assigned_split == -1)[0]
    rng.shuffle(unassigned)
    current_val = int((assigned_split == 1).sum())
    val_slots = val_count - current_val

    val_candidates = [idx for idx in unassigned if _can_assign_to_val(idx, label_matrix, assigned_split)]
    train_candidates = [idx for idx in unassigned if idx not in val_candidates]

    for idx in val_candidates:
        if val_slots <= 0:
            break
        assigned_split[idx] = 1
        val_slots -= 1

    for idx in train_candidates:
        if val_slots <= 0:
            break
        assigned_split[idx] = 1
        val_slots -= 1

    for idx in unassigned:
        if assigned_split[idx] != -1:
            continue
        assigned_split[idx] = 0 if val_slots <= 0 else 1
        if assigned_split[idx] == 1:
            val_slots -= 1

    train_indices = np.where(assigned_split == 0)[0].tolist()
    val_indices = np.where(assigned_split == 1)[0].tolist()
    return train_indices, val_indices


def _split_with_iterative_stratification(
    label_matrix: np.ndarray,
    val_ratio: float,
    seed: int,
) -> tuple[list[int], list[int]] | None:
    """Use iterative-stratification package when installed."""
    try:
        from iterative_stratification import MultilabelStratifiedShuffleSplit
    except ImportError:
        return None

    n_samples = label_matrix.shape[0]
    dummy_x = np.arange(n_samples).reshape(-1, 1)
    splitter = MultilabelStratifiedShuffleSplit(
        n_splits=1,
        test_size=val_ratio,
        random_state=seed,
    )
    train_indices, val_indices = next(splitter.split(dummy_x, label_matrix))
    return train_indices.tolist(), val_indices.tolist()


def multilabel_stratified_split_indices(
    label_matrix: np.ndarray,
    val_ratio: float,
    seed: int,
) -> tuple[list[int], list[int]]:
    """Multilabel stratified split; prefers iterative-stratification when available."""
    result = _split_with_iterative_stratification(label_matrix, val_ratio, seed)
    if result is not None:
        return result
    return greedy_multilabel_stratified_split_indices(label_matrix, val_ratio, seed)


def split_train_val(
    selected: list[dict[str, Any]],
    val_ratio: float,
    seed: int,
) -> tuple[list[dict[str, Any]], list[dict[str, Any]], str]:
    """
    Split a prepared subset into train/val using multilabel stratification.

    Returns (train_rows, val_rows, split_method).
    """
    if len(selected) <= 1:
        return selected, [], "trivial"

    label_matrix = _labels_matrix(selected)
    iter_result = _split_with_iterative_stratification(label_matrix, val_ratio, seed)
    if iter_result is not None:
        train_indices, val_indices = iter_result
        split_method = "iterative_stratification"
    else:
        train_indices, val_indices = greedy_multilabel_stratified_split_indices(
            label_matrix,
            val_ratio,
            seed,
        )
        split_method = "greedy_multilabel_stratification"

    train_rows = [selected[i] for i in train_indices]
    val_rows = [selected[i] for i in val_indices]

    if not train_rows and val_rows:
        moved = val_rows.pop()
        train_rows = [moved]

    return train_rows, val_rows, split_method


def subset_label_coverage(items: list[dict[str, Any]]) -> dict[str, int]:
    """Count CheXNet positives in a subset (for meta.json)."""
    counts = {name: 0 for name in CLASS_NAMES}
    for item in items:
        for ci, value in enumerate(item["labels"]):
            if value == 1:
                counts[CLASS_NAMES[ci]] += 1
    return counts


def split_class_statistics(
    train_rows: list[dict[str, Any]],
    val_rows: list[dict[str, Any]],
) -> list[dict[str, Any]]:
    """Per-class train/val positive counts and percentages."""
    train_cov = subset_label_coverage(train_rows)
    val_cov = subset_label_coverage(val_rows)
    train_n = len(train_rows)
    val_n = len(val_rows)

    rows: list[dict[str, Any]] = []
    for name in CLASS_NAMES:
        train_pos = train_cov[name]
        val_pos = val_cov[name]
        rows.append(
            {
                "class": name,
                "train_positives": train_pos,
                "val_positives": val_pos,
                "train_pct": round(100.0 * train_pos / train_n, 2) if train_n else 0.0,
                "val_pct": round(100.0 * val_pos / val_n, 2) if val_n else 0.0,
            }
        )
    return rows


def verify_split_coverage(
    train_rows: list[dict[str, Any]],
    val_rows: list[dict[str, Any]],
) -> None:
    """
    Abort if any class with validation positives has zero training positives.
    """
    stats = split_class_statistics(train_rows, val_rows)
    failures = [
        row
        for row in stats
        if row["val_positives"] > 0 and row["train_positives"] == 0
    ]
    if failures:
        lines = [
            f"  {row['class']}: train={row['train_positives']}, val={row['val_positives']}"
            for row in failures
        ]
        raise SplitValidationError(
            "Invalid multilabel split: class(es) appear in validation but not in training:\n"
            + "\n".join(lines)
        )


def print_split_report(
    train_rows: list[dict[str, Any]],
    val_rows: list[dict[str, Any]],
) -> None:
    """Print per-class train/val positive counts and percentages."""
    stats = split_class_statistics(train_rows, val_rows)
    train_n = len(train_rows)
    val_n = len(val_rows)

    print("")
    print("Train/validation split report (multilabel stratified):")
    print(f"  Train samples: {train_n}  |  Validation samples: {val_n}")
    print("")
    print(f"  {'Class':<22} {'Train+':>7} {'Val+':>7} {'Train%':>8} {'Val%':>8}")
    print(f"  {'-' * 22} {'-' * 7} {'-' * 7} {'-' * 8} {'-' * 8}")

    for row in stats:
        if row["train_positives"] == 0 and row["val_positives"] == 0:
            continue
        print(
            f"  {row['class']:<22} "
            f"{row['train_positives']:>7} "
            f"{row['val_positives']:>7} "
            f"{row['train_pct']:>7.2f}% "
            f"{row['val_pct']:>7.2f}%"
        )

    train_total = sum(row["train_positives"] for row in stats)
    val_total = sum(row["val_positives"] for row in stats)
    print("")
    print(f"  Total label positives: train={train_total}, val={val_total}")
    print(f"  Train images with >=1 label: {sum(1 for item in train_rows if any(item['labels']))}")
    print(f"  Val images with >=1 label:   {sum(1 for item in val_rows if any(item['labels']))}")
