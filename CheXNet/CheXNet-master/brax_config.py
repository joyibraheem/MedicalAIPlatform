"""
Shared paths and label mapping for BRAX → CheXNet training.
"""
from __future__ import annotations

from pathlib import Path

from model import CLASS_NAMES as CHEXNET_CLASS_NAMES, N_CLASSES as CHEXNET_N_CLASSES

_BASE_DIR = Path(__file__).resolve().parent

# Default location where the full BRAX download is placed on the college machine.
DEFAULT_BRAX_ROOT = _BASE_DIR / "datasets" / "BRAX"

MASTER_SPREADSHEET_NAME = "master_spreadsheet.csv"
SPREADSHEET_ALIASES = (MASTER_SPREADSHEET_NAME, "master_spreadsheet_update.csv")
PNG_FOLDER_NAME = "images"
DICOM_FOLDER_NAME = "Anonymized_DICOMs"

SUBSETS_DIR_NAME = "subsets"
RUNS_DIR_NAME = "runs"

# BRAX spreadsheet column aliases (case/spacing tolerant lookup).
# Primary path for DICOM-based BRAX: DicomPath
DICOM_PATH_COLUMNS = FALLBACK_IMAGE_PATH_COLUMNS = ("DicomPath", "DICOMPath", "dicom_path")
PNG_PATH_COLUMNS = IMAGE_PATH_COLUMNS = ("PngPath", "PNGPath", "png_path", "ImagePath", "image_path")

BRAX_LABEL_COLUMNS = (
    "No Finding",
    "Enlarged Cardiomediastinum",
    "Cardiomegaly",
    "Lung Lesion",
    "Lung Opacity",
    "Edema",
    "Consolidation",
    "Pneumonia",
    "Atelectasis",
    "Pneumothorax",
    "Pleural Effusion",
    "Pleural Other",
    "Fracture",
    "Support Devices",
)

# Map BRAX labels onto CheXNet's 14-label space (ChestX-ray14 / CheXNet order).
# Only value == 1 in BRAX is treated as positive; 0 / -1 / empty → negative.
BRAX_TO_CHEXNET: dict[str, list[str]] = {
    "Atelectasis": ["Atelectasis"],
    "Cardiomegaly": ["Cardiomegaly"],
    "Pleural Effusion": ["Effusion"],
    "Consolidation": ["Consolidation"],
    "Pneumonia": ["Pneumonia"],
    "Pneumothorax": ["Pneumothorax"],
    "Edema": ["Edema"],
    "Lung Opacity": ["Infiltration"],
    "Lung Lesion": ["Mass", "Nodule"],
    "Pleural Other": ["Pleural_Thickening"],
}

ALLOWED_SUBSET_SIZES = (100, 300, 600, 1000, 3000)
DEFAULT_RANDOM_SEED = 42
DEFAULT_VAL_RATIO = 0.2

# Epoch defaults tuned for college runs (override with --epochs).
DEFAULT_EPOCHS_BY_SIZE = {
    100: 2,
    300: 3,
    600: 4,
    1000: 5,
    3000: 8,
}

DEFAULT_BATCH_SIZE_BY_SIZE = {
    100: 8,
    300: 12,
    600: 16,
    1000: 16,
    3000: 16,
}

# Fine-tuning checkpoints (never overwrite prior runs).
MODEL_VERSIONS_DIR_NAME = "model_versions"
CHEXNET_CHECKPOINT_NAME = "model.pth.tar"
MIN_FREE_DISK_GB = 2.0
REQUIRED_PACKAGES = ("torch", "torchvision", "numpy", "PIL", "pydicom", "sklearn")

PIPELINE_NAME = "BRAX"


def find_spreadsheet(path: Path) -> Path | None:
    """Return the first existing BRAX spreadsheet under path."""
    for name in SPREADSHEET_ALIASES:
        candidate = path / name
        if candidate.is_file():
            return candidate
    return None


def has_dicom_folder(path: Path) -> bool:
    return (path / DICOM_FOLDER_NAME).is_dir()


def has_brax_layout(path: Path) -> bool:
    """True when path contains a spreadsheet and image folders."""
    return find_spreadsheet(path) is not None and (
        has_dicom_folder(path) or (path / PNG_FOLDER_NAME).is_dir()
    )


def resolve_brax_data_root(brax_root: Path) -> Path:
    """
    Return the folder that holds Anonymized_DICOMs (and optionally the CSV).

    Handles nested PhysioNet extract folders inside datasets/BRAX/, including
    CSV at the parent level and DICOMs under datasets/BRAX/brax/.
    """
    root = brax_root.resolve()
    if has_brax_layout(root):
        return root
    if root.is_dir():
        for child in sorted(root.iterdir()):
            if child.is_dir() and has_brax_layout(child):
                return child.resolve()
        nested = root / "brax"
        if find_spreadsheet(root) and has_dicom_folder(nested):
            return nested.resolve()
    return root


def resolve_spreadsheet_path(brax_root: Path, data_root: Path | None = None) -> Path:
    """Locate master_spreadsheet.csv or master_spreadsheet_update.csv."""
    root = brax_root.resolve()
    data = (data_root or resolve_brax_data_root(root)).resolve()
    for base in (data, root):
        found = find_spreadsheet(base)
        if found:
            return found
    names = ", ".join(SPREADSHEET_ALIASES)
    raise FileNotFoundError(
        f"Missing spreadsheet ({names}) under {root} or {data}."
    )


def subset_dir(brax_root: Path, size: int) -> Path:
    return brax_root / SUBSETS_DIR_NAME / f"subset_{size}"


def run_dir(brax_root: Path, size: int) -> Path:
    return brax_root / RUNS_DIR_NAME / f"subset_{size}"


def model_versions_dir() -> Path:
    return _BASE_DIR / MODEL_VERSIONS_DIR_NAME


def chexnet_checkpoint_path() -> Path:
    return _BASE_DIR / CHEXNET_CHECKPOINT_NAME


def make_run_output_dir(*, subset_size: int, epochs: int, learning_rate: float) -> Path:
    """Create a unique timestamped folder under model_versions/ (never overwrites)."""
    from datetime import datetime

    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    lr_tag = f"{learning_rate:.0e}".replace("+", "")
    name = f"BRAX_{stamp}_size{subset_size}_ep{epochs}_lr{lr_tag}"
    out = model_versions_dir() / name
    out.mkdir(parents=True, exist_ok=False)
    return out


def chexnet_label_vector() -> list[int]:
    return [0] * CHEXNET_N_CLASSES


def chexnet_class_index(name: str) -> int:
    return CHEXNET_CLASS_NAMES.index(name)
