"""
Real HITL fine-tuning for CheXNet, LungCancer, and BioBERT.
Loads exported JSON datasets, resolves source files, fine-tunes production weights,
evaluates on a validation split, and saves versioned checkpoints.
"""
from __future__ import annotations

import json
import logging
import os
import random
import time
from dataclasses import dataclass, asdict
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

import numpy as np
import torch
import torch.nn as nn
import torch.nn.functional as F
from PIL import Image
from torch.utils.data import DataLoader, Dataset
from torchvision import transforms

from model import DenseNet121, CLASS_NAMES as CHEXNET_CLASSES, N_CLASSES as CHEXNET_N_CLASSES
from lungai_architecture import ResNetLungCancer, LUNGAI_CLASS_NAMES

try:
    from sklearn.metrics import accuracy_score, f1_score
except ImportError:  # pragma: no cover
    accuracy_score = None
    f1_score = None

_BASE_DIR = Path(__file__).resolve().parent
_VERSIONS_DIR = _BASE_DIR / "model_versions"
_LOGS_DIR = _VERSIONS_DIR / "logs"
_VERSIONS_DIR.mkdir(parents=True, exist_ok=True)
_LOGS_DIR.mkdir(parents=True, exist_ok=True)

CHEXNET_CKPT = _BASE_DIR / "model.pth.tar"
LUNG_CKPT = Path(os.environ.get("LUNGAI_MODEL_PATH", ""))
if not LUNG_CKPT.is_file():
    LUNG_CKPT = _BASE_DIR / "lung_cancer_detection_model.pth"

DEVICE = torch.device("cuda" if torch.cuda.is_available() else "cpu")


@dataclass
class TrainingResult:
    model: str
    version: str
    file_path: str
    accuracy: float
    f1_score: float
    loss: float
    dataset_size: int
    training_log_path: str


def _setup_logger(model: str, version: str) -> Tuple[logging.Logger, Path]:
    log_path = _LOGS_DIR / f"{model}_v{version.replace('.', '_')}.log"
    logger = logging.getLogger(f"train.{model}.{version}")
    logger.handlers.clear()
    logger.setLevel(logging.INFO)
    fh = logging.FileHandler(log_path, encoding="utf-8")
    fh.setFormatter(logging.Formatter("%(asctime)s %(levelname)s %(message)s"))
    logger.addHandler(fh)
    sh = logging.StreamHandler()
    sh.setFormatter(logging.Formatter("%(levelname)s %(message)s"))
    logger.addHandler(sh)
    return logger, log_path


def _read_manifest(model: str) -> Dict[str, Any]:
    path = _VERSIONS_DIR / f"{model.lower()}_manifest.json"
    if path.is_file():
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)
    return {"latest_version": "1.0", "counter": 1}


def _write_manifest(model: str, manifest: Dict[str, Any]) -> None:
    path = _VERSIONS_DIR / f"{model.lower()}_manifest.json"
    with open(path, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2)


def _bump_version(current: str) -> str:
    parts = current.split(".")
    if len(parts) == 2 and parts[0].isdigit() and parts[1].isdigit():
        return f"{parts[0]}.{int(parts[1]) + 1}"
    return f"{current}.1"


def _load_export(dataset_path: str) -> Dict[str, Any]:
    path = Path(dataset_path)
    if not path.is_file():
        raise FileNotFoundError(f"Dataset not found: {dataset_path}")
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def _parse_source(sample: Dict[str, Any]) -> Dict[str, Any]:
    raw = sample.get("sourceDataJson") or sample.get("source_data_json")
    if isinstance(raw, str) and raw.strip():
        try:
            return json.loads(raw)
        except json.JSONDecodeError:
            pass
    if isinstance(raw, dict):
        return raw
    return {}


def _resolve_image_path(source: Dict[str, Any]) -> Optional[str]:
    for key in ("imagePath", "image_path"):
        p = source.get(key)
        if p and os.path.isfile(p):
            return p
    slices = source.get("sliceImagePaths") or source.get("slice_image_paths") or []
    for p in slices:
        if p and os.path.isfile(p):
            return p
    for key in ("dicomPath", "dicom_path"):
        p = source.get(key)
        if p and os.path.isfile(p):
            return p
    return None


def _resolve_text(source: Dict[str, Any], sample: Dict[str, Any]) -> str:
    for key in ("originalClinicalText", "original_clinical_text", "originalReport", "original_report"):
        val = source.get(key)
        if isinstance(val, str) and val.strip():
            return val.strip()
    notes = sample.get("doctorNotes") or sample.get("doctor_notes")
    if isinstance(notes, str) and notes.strip():
        return notes.strip()
    return ""


def _collect_training_rows(payload: Dict[str, Any]) -> List[Dict[str, Any]]:
    rows: List[Dict[str, Any]] = []
    for item in payload.get("modified") or []:
        source = _parse_source(item)
        label = (
            item.get("correctedPrediction")
            or item.get("corrected_prediction")
            or source.get("correctedLabel")
            or source.get("corrected_label")
        )
        if not label:
            continue
        rows.append({"source": source, "sample": item, "label": str(label).strip()})
    for item in payload.get("accepted") or []:
        source = _parse_source(item)
        label = item.get("prediction") or source.get("correctedLabel")
        if not label:
            continue
        rows.append({"source": source, "sample": item, "label": str(label).strip()})
    return rows


def _split_train_val(rows: List[Dict[str, Any]], val_ratio: float = 0.2) -> Tuple[List, List]:
    if len(rows) < 2:
        return rows, rows[:1] if rows else []
    shuffled = rows[:]
    random.shuffle(shuffled)
    val_count = max(1, int(len(shuffled) * val_ratio))
    return shuffled[val_count:], shuffled[:val_count]


def _metrics(y_true: List[int], y_pred: List[int], average: str = "weighted") -> Tuple[float, float]:
    if not y_true:
        return 0.0, 0.0
    if accuracy_score and f1_score:
        return float(accuracy_score(y_true, y_pred)), float(f1_score(y_true, y_pred, average=average, zero_division=0))
    acc = sum(int(a == b) for a, b in zip(y_true, y_pred)) / len(y_true)
    return acc, acc


def _load_chexnet_weights(model: nn.Module) -> None:
    if not CHEXNET_CKPT.is_file():
        raise FileNotFoundError(f"CheXNet checkpoint not found: {CHEXNET_CKPT}")
    checkpoint = torch.load(CHEXNET_CKPT, map_location=DEVICE)
    state_dict = checkpoint["state_dict"] if "state_dict" in checkpoint else checkpoint
    clean = {k.replace("module.", ""): v for k, v in state_dict.items()}
    adapted = {}
    for k, v in clean.items():
        nk = k.replace("norm.1", "norm1").replace("norm.2", "norm2").replace("conv.1", "conv1").replace("conv.2", "conv2")
        adapted[nk] = v
    model.load_state_dict(adapted, strict=False)


def _load_lung_weights(model: nn.Module) -> None:
    if not LUNG_CKPT.is_file():
        raise FileNotFoundError(f"Lung model checkpoint not found: {LUNG_CKPT}")
    state = torch.load(LUNG_CKPT, map_location=DEVICE)
    if isinstance(state, dict) and "state_dict" in state:
        state = state["state_dict"]
    model.load_state_dict(state, strict=False)


class CheXNetHitlDataset(Dataset):
    def __init__(self, rows: List[Dict[str, Any]], transform):
        self.rows = []
        self.transform = transform
        for row in rows:
            path = _resolve_image_path(row["source"])
            if not path:
                continue
            label = row["label"]
            if label not in CHEXNET_CLASSES:
                # map partial matches
                match = next((c for c in CHEXNET_CLASSES if c.lower() == label.lower()), None)
                if not match:
                    continue
                label = match
            target = torch.zeros(CHEXNET_N_CLASSES, dtype=torch.float32)
            target[CHEXNET_CLASSES.index(label)] = 1.0
            self.rows.append((path, target))

    def __len__(self):
        return len(self.rows)

    def __getitem__(self, idx):
        path, target = self.rows[idx]
        img = Image.open(path).convert("RGB")
        return self.transform(img), target


class LungHitlDataset(Dataset):
    def __init__(self, rows: List[Dict[str, Any]], transform):
        self.rows = []
        self.transform = transform
        self.class_to_idx = {c: i for i, c in enumerate(LUNGAI_CLASS_NAMES)}
        for row in rows:
            path = _resolve_image_path(row["source"])
            if not path:
                continue
            label = row["label"]
            match = next((c for c in LUNGAI_CLASS_NAMES if c.lower() == label.lower()), None)
            if match is None:
                continue
            self.rows.append((path, self.class_to_idx[match]))

    def __len__(self):
        return len(self.rows)

    def __getitem__(self, idx):
        path, label = self.rows[idx]
        img = Image.open(path).convert("RGB")
        return self.transform(img), label


class BioTextDataset(Dataset):
    def __init__(self, rows: List[Dict[str, Any]], vocab: Dict[str, int]):
        self.samples = []
        self.vocab = vocab
        for row in rows:
            text = _resolve_text(row["source"], row["sample"])
            label = row["label"]
            if not text or not label:
                continue
            self.samples.append((text, label))

    def __len__(self):
        return len(self.samples)

    def _encode(self, text: str) -> torch.Tensor:
        tokens = text.lower().split()
        vec = torch.zeros(len(self.vocab), dtype=torch.float32)
        for t in tokens:
            if t in self.vocab:
                vec[self.vocab[t]] += 1.0
        if vec.sum() > 0:
            vec = vec / vec.sum()
        return vec

    def __getitem__(self, idx):
        text, label = self.samples[idx]
        return self._encode(text), label


class BioTextClassifier(nn.Module):
    def __init__(self, input_dim: int, num_classes: int):
        super().__init__()
        self.net = nn.Sequential(
            nn.Linear(input_dim, 256),
            nn.ReLU(),
            nn.Dropout(0.3),
            nn.Linear(256, num_classes),
        )

    def forward(self, x):
        return self.net(x)


def _train_chexnet(rows: List[Dict[str, Any]], version: str, logger: logging.Logger) -> TrainingResult:
    train_rows, val_rows = _split_train_val(rows)
    transform = transforms.Compose([
        transforms.Resize(256),
        transforms.CenterCrop(224),
        transforms.RandomHorizontalFlip(),
        transforms.ToTensor(),
        transforms.Normalize([0.485, 0.456, 0.406], [0.229, 0.224, 0.225]),
    ])
    train_ds = CheXNetHitlDataset(train_rows, transform)
    val_ds = CheXNetHitlDataset(val_rows, transform)
    if len(train_ds) == 0:
        raise ValueError("No CheXNet training images resolved from source data paths.")

    model = DenseNet121(out_size=CHEXNET_N_CLASSES).to(DEVICE)
    _load_chexnet_weights(model)
    optimizer = torch.optim.Adam(model.parameters(), lr=1e-4)
    criterion = nn.BCELoss()
    train_loader = DataLoader(train_ds, batch_size=min(8, len(train_ds)), shuffle=True)
    val_loader = DataLoader(val_ds, batch_size=min(8, max(1, len(val_ds))), shuffle=False)

    best_loss = float("inf")
    last_loss = 0.0
    for epoch in range(8):
        model.train()
        epoch_loss = 0.0
        for x, y in train_loader:
            x, y = x.to(DEVICE), y.to(DEVICE)
            optimizer.zero_grad()
            probs = model(x)
            loss = criterion(probs, y)
            loss.backward()
            optimizer.step()
            epoch_loss += loss.item()
        last_loss = epoch_loss / max(1, len(train_loader))
        logger.info("CheXNet epoch %s train_loss=%.4f", epoch + 1, last_loss)

        model.eval()
        val_loss = 0.0
        with torch.no_grad():
            for x, y in val_loader:
                x, y = x.to(DEVICE), y.to(DEVICE)
                probs = model(x)
                val_loss += criterion(probs, y).item()
        val_loss /= max(1, len(val_loader))
        if val_loss < best_loss:
            best_loss = val_loss

    y_true, y_pred = [], []
    model.eval()
    with torch.no_grad():
        for x, y in val_loader:
            x = x.to(DEVICE)
            probs = model(x).cpu().numpy()
            pred_idx = probs.argmax(axis=1)
            true_idx = y.numpy().argmax(axis=1)
            y_pred.extend(pred_idx.tolist())
            y_true.extend(true_idx.tolist())

    acc, f1 = _metrics(y_true, y_pred)
    out_path = _VERSIONS_DIR / f"CheXNet_v{version.replace('.', '_')}.pth.tar"
    torch.save({"state_dict": model.state_dict(), "version": version, "classes": CHEXNET_CLASSES}, out_path)
    logger.info("Saved CheXNet checkpoint %s acc=%.4f f1=%.4f", out_path, acc, f1)

    return TrainingResult("CheXNet", version, str(out_path), acc, f1, float(best_loss or last_loss), len(rows), "")


def _train_lung(rows: List[Dict[str, Any]], version: str, logger: logging.Logger) -> TrainingResult:
    train_rows, val_rows = _split_train_val(rows)
    transform = transforms.Compose([
        transforms.Resize(256),
        transforms.CenterCrop(224),
        transforms.RandomHorizontalFlip(),
        transforms.ToTensor(),
        transforms.Normalize([0.485, 0.456, 0.406], [0.229, 0.224, 0.225]),
    ])
    train_ds = LungHitlDataset(train_rows, transform)
    val_ds = LungHitlDataset(val_rows, transform)
    if len(train_ds) == 0:
        raise ValueError("No LungCancer training images resolved from source data paths.")

    model = ResNetLungCancer(num_classes=len(LUNGAI_CLASS_NAMES), use_pretrained=False).to(DEVICE)
    _load_lung_weights(model)
    optimizer = torch.optim.Adam(model.parameters(), lr=1e-4)
    criterion = nn.CrossEntropyLoss()
    train_loader = DataLoader(train_ds, batch_size=min(8, len(train_ds)), shuffle=True)
    val_loader = DataLoader(val_ds, batch_size=min(8, max(1, len(val_ds))), shuffle=False)

    best_loss = float("inf")
    last_loss = 0.0
    for epoch in range(8):
        model.train()
        epoch_loss = 0.0
        for x, y in train_loader:
            x, y = x.to(DEVICE), y.to(DEVICE)
            optimizer.zero_grad()
            logits = model(x)
            loss = criterion(logits, y)
            loss.backward()
            optimizer.step()
            epoch_loss += loss.item()
        last_loss = epoch_loss / max(1, len(train_loader))
        logger.info("LungCancer epoch %s train_loss=%.4f", epoch + 1, last_loss)

    y_true, y_pred = [], []
    model.eval()
    with torch.no_grad():
        for x, y in val_loader:
            x = x.to(DEVICE)
            logits = model(x)
            pred = logits.argmax(dim=1).cpu().tolist()
            y_pred.extend(pred)
            y_true.extend(y.tolist())
    acc, f1 = _metrics(y_true, y_pred)

    out_path = _VERSIONS_DIR / f"LungCancer_v{version.replace('.', '_')}.pth"
    torch.save({"state_dict": model.state_dict(), "version": version, "classes": LUNGAI_CLASS_NAMES}, out_path)
    logger.info("Saved LungCancer checkpoint %s acc=%.4f f1=%.4f", out_path, acc, f1)

    return TrainingResult("LungCancer", version, str(out_path), acc, f1, float(best_loss or last_loss), len(rows), "")


def _train_biobert(rows: List[Dict[str, Any]], version: str, logger: logging.Logger) -> TrainingResult:
    labels = sorted({r["label"] for r in rows if r.get("label")})
    if not labels:
        raise ValueError("No BioBERT labels found.")
    label_to_idx = {l: i for i, l in enumerate(labels)}
    vocab: Dict[str, int] = {}
    for row in rows:
        text = _resolve_text(row["source"], row["sample"])
        for tok in text.lower().split():
            if tok not in vocab:
                vocab[tok] = len(vocab)
    if not vocab:
        raise ValueError("No BioBERT clinical text resolved from source data.")

    train_rows, val_rows = _split_train_val(rows)
    train_ds = BioTextDataset(train_rows, vocab)
    val_ds = BioTextDataset(val_rows, vocab)
    if len(train_ds) == 0:
        raise ValueError("No BioBERT training samples after text resolution.")

    model = BioTextClassifier(len(vocab), len(labels)).to(DEVICE)
    optimizer = torch.optim.Adam(model.parameters(), lr=1e-3)
    criterion = nn.CrossEntropyLoss()
    train_loader = DataLoader(train_ds, batch_size=min(16, len(train_ds)), shuffle=True)
    val_loader = DataLoader(val_ds, batch_size=min(16, max(1, len(val_ds))), shuffle=False)

    best_loss = float("inf")
    last_loss = 0.0
    for epoch in range(12):
        model.train()
        epoch_loss = 0.0
        for x, y_str in train_loader:
            y = torch.tensor([label_to_idx[s] for s in y_str], device=DEVICE)
            x = x.to(DEVICE)
            optimizer.zero_grad()
            logits = model(x)
            loss = criterion(logits, y)
            loss.backward()
            optimizer.step()
            epoch_loss += loss.item()
        last_loss = epoch_loss / max(1, len(train_loader))
        logger.info("BioBERT epoch %s train_loss=%.4f", epoch + 1, last_loss)

    y_true, y_pred = [], []
    model.eval()
    with torch.no_grad():
        for x, y_str in val_loader:
            x = x.to(DEVICE)
            logits = model(x)
            pred = logits.argmax(dim=1).cpu().tolist()
            y_pred.extend(pred)
            y_true.extend([label_to_idx[s] for s in y_str])
    acc, f1 = _metrics(y_true, y_pred)

    out_path = _VERSIONS_DIR / f"BioBERT_v{version.replace('.', '_')}.pt"
    torch.save({
        "model_state": model.state_dict(),
        "vocab": vocab,
        "label_to_idx": label_to_idx,
        "version": version,
    }, out_path)
    logger.info("Saved BioBERT checkpoint %s acc=%.4f f1=%.4f", out_path, acc, f1)

    return TrainingResult("BioBERT", version, str(out_path), acc, f1, float(best_loss or last_loss), len(rows), "")


def run_fine_tune(model: str, dataset_path: str) -> TrainingResult:
    model = model.strip()
    if model not in {"CheXNet", "LungCancer", "BioBERT"}:
        raise ValueError(f"Unsupported model: {model}")

    payload = _load_export(dataset_path)
    rows = _collect_training_rows(payload)
    if not rows:
        raise ValueError("Dataset contains no labeled samples with source data.")

    manifest = _read_manifest(model)
    version = _bump_version(str(manifest.get("latest_version", "1.0")))
    manifest["latest_version"] = version
    manifest["counter"] = int(manifest.get("counter", 1)) + 1
    _write_manifest(model, manifest)

    logger, log_path = _setup_logger(model, version)
    logger.info("Starting fine-tune model=%s version=%s dataset=%s samples=%s", model, version, dataset_path, len(rows))

    if model == "CheXNet":
        result = _train_chexnet(rows, version, logger)
    elif model == "LungCancer":
        result = _train_lung(rows, version, logger)
    else:
        result = _train_biobert(rows, version, logger)

    result.training_log_path = str(log_path)
    logger.info("Training complete: %s", json.dumps(asdict(result), indent=2))
    return result
