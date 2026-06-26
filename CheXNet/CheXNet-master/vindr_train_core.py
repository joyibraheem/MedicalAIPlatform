"""
CheXNet training loop for prepared VinDr-PCXR subsets.
"""
from __future__ import annotations

import json
import logging
from pathlib import Path
from typing import Tuple

import torch
import torch.nn as nn
from torch.utils.data import DataLoader
from torchvision import transforms

from model import DenseNet121, CLASS_NAMES, N_CLASSES, CKPT_PATH
from vindr_dataset import VindrChexNetDataset

try:
    from sklearn.metrics import f1_score
except ImportError:  # pragma: no cover
    f1_score = None

DEVICE = torch.device("cuda" if torch.cuda.is_available() else "cpu")


def setup_logger(log_path: Path) -> logging.Logger:
    log_path.parent.mkdir(parents=True, exist_ok=True)
    logger = logging.getLogger(f"vindr_train.{log_path.stem}")
    logger.handlers.clear()
    logger.setLevel(logging.INFO)
    fmt = logging.Formatter("%(asctime)s %(levelname)s %(message)s")
    fh = logging.FileHandler(log_path, encoding="utf-8")
    fh.setFormatter(fmt)
    sh = logging.StreamHandler()
    sh.setFormatter(fmt)
    logger.addHandler(fh)
    logger.addHandler(sh)
    return logger


def load_chexnet_checkpoint(model: nn.Module, checkpoint_path: Path, logger: logging.Logger) -> None:
    if not checkpoint_path.is_file():
        logger.warning("Checkpoint not found at %s — training from ImageNet backbone only.", checkpoint_path)
        return

    logger.info("Loading CheXNet checkpoint: %s", checkpoint_path)
    checkpoint = torch.load(checkpoint_path, map_location=DEVICE)
    state_dict = checkpoint["state_dict"] if isinstance(checkpoint, dict) and "state_dict" in checkpoint else checkpoint
    clean = {k.replace("module.", ""): v for k, v in state_dict.items()}
    adapted = {}
    for k, v in clean.items():
        nk = (
            k.replace("norm.1", "norm1")
            .replace("norm.2", "norm2")
            .replace("conv.1", "conv1")
            .replace("conv.2", "conv2")
        )
        adapted[nk] = v
    model.load_state_dict(adapted, strict=False)


def _batch_f1(y_true: torch.Tensor, y_pred: torch.Tensor) -> float:
    if y_true.numel() == 0:
        return 0.0
    yt = y_true.cpu().numpy()
    yp = y_pred.cpu().numpy()
    if f1_score is not None:
        return float(f1_score(yt, yp, average="micro", zero_division=0))
    matches = (yt == yp).sum()
    return float(matches / yt.size)


def evaluate(model: nn.Module, loader: DataLoader, criterion: nn.Module) -> Tuple[float, float]:
    model.eval()
    total_loss = 0.0
    all_true = []
    all_pred = []
    with torch.no_grad():
        for images, targets in loader:
            images = images.to(DEVICE)
            targets = targets.to(DEVICE)
            outputs = model(images)
            loss = criterion(outputs, targets)
            total_loss += float(loss.item()) * images.size(0)
            preds = (outputs >= 0.5).float()
            all_true.append(targets)
            all_pred.append(preds)
    if not all_true:
        return 0.0, 0.0
    y_true = torch.cat(all_true, dim=0)
    y_pred = torch.cat(all_pred, dim=0)
    avg_loss = total_loss / len(loader.dataset)
    f1 = _batch_f1(y_true, y_pred)
    return avg_loss, f1


def train_vindr_subset(
    *,
    vindr_root: Path,
    subset_dir: Path,
    output_dir: Path,
    epochs: int,
    batch_size: int,
    learning_rate: float = 1e-4,
    num_workers: int = 2,
    finetune_from: Path | None = None,
) -> dict:
    train_list = subset_dir / "train_list.txt"
    val_list = subset_dir / "val_list.txt"
    meta_path = subset_dir / "meta.json"

    if not train_list.is_file():
        raise FileNotFoundError(f"Missing {train_list}. Run prepare_vindr_subset.py first.")
    if not val_list.is_file():
        raise FileNotFoundError(f"Missing {val_list}. Run prepare_vindr_subset.py first.")

    output_dir.mkdir(parents=True, exist_ok=True)
    logger = setup_logger(output_dir / "training.log")

    transform_train = transforms.Compose(
        [
            transforms.Resize(256),
            transforms.RandomResizedCrop(224, scale=(0.9, 1.0)),
            transforms.RandomHorizontalFlip(),
            transforms.ToTensor(),
            transforms.Normalize([0.485, 0.456, 0.406], [0.229, 0.224, 0.225]),
        ]
    )
    transform_val = transforms.Compose(
        [
            transforms.Resize(256),
            transforms.CenterCrop(224),
            transforms.ToTensor(),
            transforms.Normalize([0.485, 0.456, 0.406], [0.229, 0.224, 0.225]),
        ]
    )

    train_ds = VindrChexNetDataset(str(vindr_root), str(train_list), transform=transform_train)
    val_ds = VindrChexNetDataset(str(vindr_root), str(val_list), transform=transform_val)
    if len(train_ds) == 0:
        raise ValueError("Training list loaded zero samples. Check image paths in train_list.txt.")

    train_loader = DataLoader(
        train_ds,
        batch_size=min(batch_size, len(train_ds)),
        shuffle=True,
        num_workers=num_workers,
        pin_memory=torch.cuda.is_available(),
    )
    val_loader = DataLoader(
        val_ds,
        batch_size=min(batch_size, max(1, len(val_ds))),
        shuffle=False,
        num_workers=num_workers,
        pin_memory=torch.cuda.is_available(),
    )

    model = DenseNet121(N_CLASSES).to(DEVICE)
    if torch.cuda.device_count() > 1:
        model = nn.DataParallel(model)

    ckpt_path = finetune_from or Path(CKPT_PATH)
    load_chexnet_checkpoint(model.module if isinstance(model, nn.DataParallel) else model, ckpt_path, logger)

    optimizer = torch.optim.Adam(model.parameters(), lr=learning_rate)
    criterion = nn.BCELoss()

    logger.info("Device: %s", DEVICE)
    logger.info("Train samples: %s  Val samples: %s", len(train_ds), len(val_ds))
    logger.info("Epochs: %s  Batch size: %s  LR: %s", epochs, batch_size, learning_rate)

    history = []
    best_f1 = -1.0
    best_path = output_dir / "best_model.pth.tar"
    last_path = output_dir / "last_model.pth.tar"

    for epoch in range(1, epochs + 1):
        model.train()
        running_loss = 0.0
        seen = 0
        for images, targets in train_loader:
            images = images.to(DEVICE)
            targets = targets.to(DEVICE)
            optimizer.zero_grad(set_to_none=True)
            outputs = model(images)
            loss = criterion(outputs, targets)
            loss.backward()
            optimizer.step()
            running_loss += float(loss.item()) * images.size(0)
            seen += images.size(0)

        train_loss = running_loss / max(1, seen)
        val_loss, val_f1 = evaluate(model, val_loader, criterion)
        record = {
            "epoch": epoch,
            "train_loss": train_loss,
            "val_loss": val_loss,
            "val_f1_micro": val_f1,
        }
        history.append(record)
        logger.info(
            "Epoch %s/%s  train_loss=%.4f  val_loss=%.4f  val_f1=%.4f",
            epoch,
            epochs,
            train_loss,
            val_loss,
            val_f1,
        )

        state = model.module.state_dict() if isinstance(model, nn.DataParallel) else model.state_dict()
        torch.save(
            {
                "epoch": epoch,
                "state_dict": state,
                "class_names": CLASS_NAMES,
                "val_f1_micro": val_f1,
            },
            last_path,
        )
        if val_f1 >= best_f1:
            best_f1 = val_f1
            torch.save(
                {
                    "epoch": epoch,
                    "state_dict": state,
                    "class_names": CLASS_NAMES,
                    "val_f1_micro": val_f1,
                },
                best_path,
            )

    summary = {
        "pipeline": "VINDR_PCXR",
        "vindr_root": str(vindr_root),
        "subset_dir": str(subset_dir),
        "output_dir": str(output_dir),
        "epochs": epochs,
        "batch_size": batch_size,
        "learning_rate": learning_rate,
        "train_samples": len(train_ds),
        "val_samples": len(val_ds),
        "best_val_f1_micro": best_f1,
        "best_checkpoint": str(best_path),
        "last_checkpoint": str(last_path),
        "history": history,
        "class_names": CLASS_NAMES,
    }

    if meta_path.is_file():
        with open(meta_path, "r", encoding="utf-8") as f:
            summary["subset_meta"] = json.load(f)

    with open(output_dir / "metrics.json", "w", encoding="utf-8") as f:
        json.dump(summary, f, indent=2)

    logger.info("Training complete. Best val F1=%.4f  checkpoint=%s", best_f1, best_path)
    return summary
