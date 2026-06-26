"""
Shared paths and defaults for VinDr-PCXR -> CheXNet training.
"""
from __future__ import annotations

from pathlib import Path

from model import CLASS_NAMES as CHEXNET_CLASS_NAMES, N_CLASSES as CHEXNET_N_CLASSES

_BASE_DIR = Path(__file__).resolve().parent

DEFAULT_VINDR_ROOT = _BASE_DIR / "datasets" / "VINDR_PCXR"

IMAGE_LABELS_TRAIN = "image_labels_train.csv"
IMAGE_LABELS_TEST = "image_labels_test.csv"
ANNOTATIONS_TRAIN = "annotations_train.csv"

TRAIN_IMAGES_DIR = "train"
TEST_IMAGES_DIR = "test"

SUBSETS_DIR_NAME = "subsets"
RUNS_DIR_NAME = "runs"

ALLOWED_SUBSET_SIZES = (100, 1000, 3000)
DEFAULT_RANDOM_SEED = 42
DEFAULT_VAL_RATIO = 0.2

DEFAULT_EPOCHS_BY_SIZE = {
    100: 2,
    1000: 5,
    3000: 8,
}

DEFAULT_BATCH_SIZE_BY_SIZE = {
    100: 8,
    1000: 16,
    3000: 16,
}

# Try these extensions under train/ for each image_id (VinDr-PCXR ships DICOM).
IMAGE_ID_EXTENSIONS = (".dicom", ".dcm", ".DCM", ".png", ".jpg", ".jpeg")

PIPELINE_NAME = "VINDR_PCXR"


def has_vindr_layout(path: Path) -> bool:
    """True when path contains the VinDr-PCXR labels CSV and train/ image folder."""
    return (path / IMAGE_LABELS_TRAIN).is_file() and (path / TRAIN_IMAGES_DIR).is_dir()


def resolve_vindr_data_root(vindr_root: Path) -> Path:
    """
    Return the folder that actually holds image_labels_train.csv and train/.

    Handles a common PhysioNet mistake: extracting the version folder inside
    datasets/VINDR_PCXR/ (e.g. datasets/VINDR_PCXR/vindr-pcxr-1.0.0/...).
    """
    root = vindr_root.resolve()
    if has_vindr_layout(root):
        return root
    if root.is_dir():
        for child in sorted(root.iterdir()):
            if child.is_dir() and has_vindr_layout(child):
                return child.resolve()
    return root


def subset_dir(vindr_root: Path, size: int) -> Path:
    return vindr_root / SUBSETS_DIR_NAME / f"subset_{size}"


def run_dir(vindr_root: Path, size: int) -> Path:
    return vindr_root / RUNS_DIR_NAME / f"subset_{size}"


def chexnet_label_vector() -> list[int]:
    return [0] * CHEXNET_N_CLASSES


def chexnet_class_index(name: str) -> int:
    return CHEXNET_CLASS_NAMES.index(name)
