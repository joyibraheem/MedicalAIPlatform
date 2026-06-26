"""
CheXNet training loop for prepared BRAX subsets.

Fine-tunes from model.pth.tar when present. Each run writes timestamped
artifacts under model_versions/ (never overwrites prior runs).
"""
from __future__ import annotations

import csv
import json
import logging
import time
from pathlib import Path
from typing import Any

import numpy as np
import torch
import torch.nn as nn
from torch.utils.data import DataLoader

from model import CLASS_NAMES, N_CLASSES, CKPT_PATH
from brax_config import chexnet_checkpoint_path
from brax_dataloader import create_brax_dataloaders
from brax_model import (
    BraxTransferModel,
    configure_transfer_learning,
    load_brax_checkpoint_state,
    load_pretrained_backbone,
    trainable_parameters,
)

try:
    from sklearn.metrics import (
        accuracy_score,
        f1_score,
        precision_score,
        recall_score,
        roc_auc_score,
    )
except ImportError:  # pragma: no cover
    accuracy_score = f1_score = precision_score = recall_score = roc_auc_score = None

DEVICE = torch.device("cuda" if torch.cuda.is_available() else "cpu")


def setup_logger(log_path: Path) -> logging.Logger:
    log_path.parent.mkdir(parents=True, exist_ok=True)
    logger = logging.getLogger(f"brax_train.{log_path.stem}")
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


def _adapt_state_dict(state_dict: dict) -> dict:
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
    return adapted


def _unwrap_model(model: nn.Module) -> nn.Module:
    return model.module if isinstance(model, nn.DataParallel) else model


def count_trainable_parameters(model: nn.Module) -> int:
    return sum(p.numel() for p in model.parameters() if p.requires_grad)


def load_checkpoint_file(checkpoint_path: Path) -> dict[str, Any]:
    if not checkpoint_path.is_file():
        raise FileNotFoundError(f"Checkpoint not found at {checkpoint_path}")
    checkpoint = torch.load(checkpoint_path, map_location=DEVICE)
    if isinstance(checkpoint, dict):
        return checkpoint
    return {"state_dict": checkpoint}


def load_resume_checkpoint(
    model: BraxTransferModel,
    optimizer: torch.optim.Optimizer,
    checkpoint_path: Path,
    logger: logging.Logger,
    *,
    learning_rate: float,
) -> dict[str, Any]:
    """
    Restore optimizer state and epoch metadata from a prior BRAX checkpoint.

    Model weights must already be loaded via load_brax_checkpoint_state().
    """
    checkpoint = load_checkpoint_file(checkpoint_path)

    optimizer_restored = False
    if "optimizer_state_dict" in checkpoint:
        try:
            optimizer.load_state_dict(checkpoint["optimizer_state_dict"])
            optimizer_restored = True
        except Exception as exc:
            logger.warning("Could not restore optimizer state: %s", exc)

    completed_epoch = int(checkpoint.get("epoch", 0))
    start_epoch = completed_epoch + 1 if completed_epoch > 0 else 1
    resume_lr = float(checkpoint.get("learning_rate", learning_rate))
    if not optimizer_restored:
        for group in optimizer.param_groups:
            group["lr"] = learning_rate
        current_lr = learning_rate
    else:
        current_lr = float(optimizer.param_groups[0]["lr"])

    return {
        "checkpoint_path": str(checkpoint_path.resolve()),
        "completed_epoch": completed_epoch,
        "start_epoch": start_epoch,
        "optimizer_restored": optimizer_restored,
        "learning_rate": current_lr,
        "resume_lr_from_checkpoint": resume_lr,
        "val_metrics": checkpoint.get("val_metrics", {}),
    }


def print_transfer_learning_banner(
    *,
    pretrained_loaded: bool,
    transfer_config: dict[str, Any],
    train_samples: int,
    val_samples: int,
    learning_rate: float,
    batch_size: int,
    epochs: int,
    start_epoch: int,
    end_epoch: int,
    checkpoint_path: Path,
    resumed: bool,
) -> None:
    print("\n" + "-" * 40)
    print(f"Pretrained checkpoint loaded : {'YES' if pretrained_loaded else 'NO'}")
    print(f"Backbone frozen             : {'YES' if transfer_config.get('backbone_frozen') else 'NO'}")
    print(f"Classifier replaced         : {'YES' if transfer_config.get('classifier_replaced') else 'NO'}")
    if transfer_config.get("unfreeze_last_block"):
        status = "YES" if transfer_config.get("last_block_unfrozen") else "NO"
        print(f"Last dense block unfrozen   : {status}")
    print()
    print(f"Total parameters            : {transfer_config['total_parameters']:,}")
    print(f"Frozen parameters           : {transfer_config['frozen_parameters']:,}")
    print(f"Trainable parameters        : {transfer_config['trainable_parameters']:,}")
    print()
    print(f"Train subset                : {train_samples}")
    print(f"Validation subset           : {val_samples}")
    print()
    print(f"Learning rate               : {learning_rate}")
    print(f"Batch size                  : {batch_size}")
    print(f"Epochs (this run)           : {epochs}  ({start_epoch} -> {end_epoch})")
    if resumed:
        print(f"Resume checkpoint           : {checkpoint_path}")
    else:
        print(f"Starting checkpoint         : {checkpoint_path}")
    print("-" * 40 + "\n")


def print_training_start_info(
    *,
    checkpoint_path: Path,
    resume_info: dict[str, Any] | None,
    learning_rate: float,
    trainable_params: int,
    resumed: bool,
) -> None:
    print("\n" + "=" * 72)
    print("BRAX TRAINING START")
    print("=" * 72)
    if resumed and resume_info:
        print(f"  Mode:                  Resume")
        print(f"  Starting checkpoint:   {resume_info['checkpoint_path']}")
        print(f"  Starting epoch:        {resume_info['start_epoch']}")
        print(f"  Current learning rate: {resume_info['learning_rate']:.6g}")
        if not resume_info["optimizer_restored"]:
            print("  Optimizer state:       not found in checkpoint (fresh optimizer)")
    else:
        print(f"  Mode:                  Fine-tune from base checkpoint")
        print(f"  Starting checkpoint:   {checkpoint_path.resolve()}")
        print(f"  Starting epoch:        1")
        print(f"  Current learning rate: {learning_rate:.6g}")
    print(f"  Total trainable params: {trainable_params:,}")
    print("=" * 72 + "\n")


def compute_multilabel_metrics(
    y_true: np.ndarray,
    y_prob: np.ndarray,
    *,
    threshold: float = 0.5,
) -> dict[str, float]:
    """Compute accuracy, precision, recall, F1, and ROC-AUC for multi-label outputs."""
    y_pred = (y_prob >= threshold).astype(np.int32)

    if y_true.size == 0:
        return {
            "accuracy": 0.0,
            "precision_micro": 0.0,
            "recall_micro": 0.0,
            "f1_micro": 0.0,
            "precision_macro": 0.0,
            "recall_macro": 0.0,
            "f1_macro": 0.0,
            "roc_auc_macro": 0.0,
        }

    if accuracy_score is not None:
        accuracy = float(accuracy_score(y_true, y_pred))
        precision_micro = float(precision_score(y_true, y_pred, average="micro", zero_division=0))
        recall_micro = float(recall_score(y_true, y_pred, average="micro", zero_division=0))
        f1_micro = float(f1_score(y_true, y_pred, average="micro", zero_division=0))
        precision_macro = float(precision_score(y_true, y_pred, average="macro", zero_division=0))
        recall_macro = float(recall_score(y_true, y_pred, average="macro", zero_division=0))
        f1_macro = float(f1_score(y_true, y_pred, average="macro", zero_division=0))
    else:
        matches = (y_true == y_pred).sum()
        accuracy = float(matches / y_true.size)
        precision_micro = recall_micro = f1_micro = accuracy
        precision_macro = recall_macro = f1_macro = accuracy

    roc_scores: list[float] = []
    if roc_auc_score is not None:
        for i in range(y_true.shape[1]):
            col = y_true[:, i]
            if col.min() == col.max():
                continue
            try:
                roc_scores.append(float(roc_auc_score(col, y_prob[:, i])))
            except ValueError:
                continue
    roc_auc_macro = float(np.mean(roc_scores)) if roc_scores else 0.0

    return {
        "accuracy": accuracy,
        "precision_micro": precision_micro,
        "recall_micro": recall_micro,
        "f1_micro": f1_micro,
        "precision_macro": precision_macro,
        "recall_macro": recall_macro,
        "f1_macro": f1_macro,
        "roc_auc_macro": roc_auc_macro,
    }


def evaluate(
    model: nn.Module,
    loader: DataLoader,
    criterion: nn.Module,
) -> tuple[float, dict[str, float]]:
    model.eval()
    total_loss = 0.0
    all_true: list[torch.Tensor] = []
    all_prob: list[torch.Tensor] = []

    with torch.no_grad():
        for images, targets in loader:
            images = images.to(DEVICE)
            targets = targets.to(DEVICE)
            outputs = model(images)
            loss = criterion(outputs, targets)
            total_loss += float(loss.item()) * images.size(0)
            all_true.append(targets)
            all_prob.append(outputs)

    if not all_true:
        return 0.0, compute_multilabel_metrics(np.zeros((0, N_CLASSES)), np.zeros((0, N_CLASSES)))

    y_true = torch.cat(all_true, dim=0).cpu().numpy()
    y_prob = torch.cat(all_prob, dim=0).cpu().numpy()
    avg_loss = total_loss / len(loader.dataset)
    metrics = compute_multilabel_metrics(y_true, y_prob)
    return avg_loss, metrics


def write_loss_csv(output_dir: Path, history: list[dict[str, Any]]) -> Path:
    path = output_dir / "loss.csv"
    fieldnames = [
        "epoch",
        "train_loss",
        "val_loss",
        "accuracy",
        "precision_micro",
        "recall_micro",
        "f1_micro",
        "roc_auc_macro",
    ]
    with open(path, "w", encoding="utf-8", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        for row in history:
            writer.writerow({k: row.get(k, "") for k in fieldnames})
    return path


def write_training_summary(output_dir: Path, summary: dict[str, Any]) -> Path:
    path = output_dir / "training_summary.txt"
    final = summary.get("final_metrics", {})
    lines = [
        "BRAX CheXNet Fine-Tuning Summary",
        "=" * 40,
        f"Output folder:     {summary.get('output_dir', output_dir)}",
        f"Training size:     {summary.get('train_samples', 'n/a')}",
        f"Validation size:   {summary.get('val_samples', 'n/a')}",
        f"Epochs:            {summary.get('epochs', 'n/a')}",
        f"Learning rate:     {summary.get('learning_rate', 'n/a')}",
        f"Best epoch:        {summary.get('best_epoch', 'n/a')}",
        f"Best val loss:     {summary.get('best_val_loss', 'n/a')}",
        f"Training time (s): {summary.get('training_time_seconds', 'n/a')}",
        "",
        "Best validation metrics:",
        f"  Accuracy:          {final.get('accuracy', 0.0):.4f}",
        f"  Precision (micro): {final.get('precision_micro', 0.0):.4f}",
        f"  Recall (micro):    {final.get('recall_micro', 0.0):.4f}",
        f"  F1 (micro):        {final.get('f1_micro', 0.0):.4f}",
        f"  ROC-AUC (macro):   {final.get('roc_auc_macro', 0.0):.4f}",
        "",
        f"Best checkpoint:   {summary.get('best_checkpoint', 'n/a')}",
    ]
    transfer = summary.get("transfer_learning")
    if transfer:
        lines.extend(
            [
                "",
                "Transfer learning:",
                f"  Backbone frozen:     {transfer.get('backbone_frozen', 'n/a')}",
                f"  Trainable params:    {transfer.get('trainable_parameters', 'n/a')}",
                f"  Unfreeze last block: {summary.get('unfreeze_last_block', False)}",
            ]
        )
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return path


def _format_eta(seconds: float) -> str:
    if seconds < 60:
        return f"{seconds:.0f}s"
    return f"{seconds / 60:.1f} min ({seconds:.0f}s)"


def _log_epoch_progress(
    logger: logging.Logger,
    *,
    epoch: int,
    epochs: int,
    train_loss: float,
    val_loss: float,
    val_metrics: dict[str, float],
    eta_seconds: float,
) -> None:
    lines = [
        f"--- Epoch {epoch}/{epochs} ---",
        f"  Train loss:     {train_loss:.4f}",
        f"  Val loss:       {val_loss:.4f}",
        f"  Accuracy:       {val_metrics.get('accuracy', 0.0):.4f}",
        f"  Precision:      {val_metrics.get('precision_micro', 0.0):.4f}",
        f"  Recall:         {val_metrics.get('recall_micro', 0.0):.4f}",
        f"  F1-score:       {val_metrics.get('f1_micro', 0.0):.4f}",
        f"  ETA remaining:  {_format_eta(eta_seconds)}",
    ]
    block = "\n".join(lines)
    logger.info("\n%s", block)
    print(block)


def print_post_training_summary(summary: dict[str, Any]) -> None:
    final = summary.get("final_metrics", {})
    print("\n" + "=" * 72)
    print("BRAX TRAINING COMPLETE")
    print("=" * 72)
    print(f"  Total training time:   {summary.get('training_time_seconds', 0.0):.1f}s")
    print(f"  Best epoch:            {summary.get('best_epoch', 'n/a')}")
    print(f"  Best validation loss:  {summary.get('best_val_loss', 'n/a')}")
    print(f"  Precision (micro):     {final.get('precision_micro', 0.0):.4f}")
    print(f"  Recall (micro):        {final.get('recall_micro', 0.0):.4f}")
    print(f"  F1-score (micro):      {final.get('f1_micro', 0.0):.4f}")
    print(f"  ROC-AUC (macro):       {final.get('roc_auc_macro', 0.0):.4f}")
    if summary.get("early_stopped"):
        print(f"  Early stopping:        YES (patience={summary.get('early_stopping_patience')})")
    print(f"  Output folder:         {summary.get('output_dir', 'n/a')}")
    print(f"  Best checkpoint:       {summary.get('best_checkpoint', 'n/a')}")
    print("=" * 72)


def train_brax_subset(
    *,
    brax_root: Path,
    subset_dir: Path,
    output_dir: Path,
    epochs: int,
    batch_size: int,
    learning_rate: float = 1e-4,
    num_workers: int = 2,
    finetune_from: Path | None = None,
    resume_from: Path | None = None,
    require_checkpoint: bool = False,
    unfreeze_last_block: bool = False,
    early_stopping_patience: int | None = None,
) -> dict[str, Any]:
    train_list = subset_dir / "train_list.txt"
    val_list = subset_dir / "val_list.txt"
    meta_path = subset_dir / "meta.json"

    if not train_list.is_file():
        raise FileNotFoundError(f"Missing {train_list}. Run prepare_brax_subset.py first.")
    if not val_list.is_file():
        raise FileNotFoundError(f"Missing {val_list}. Run prepare_brax_subset.py first.")

    output_dir.mkdir(parents=True, exist_ok=True)
    logger = setup_logger(output_dir / "training.log")

    train_loader, val_loader, train_ds, val_ds = create_brax_dataloaders(
        brax_root,
        train_list,
        val_list,
        batch_size=batch_size,
        num_workers=num_workers,
    )
    if len(train_ds) == 0:
        raise ValueError("Training list loaded zero samples. Check image paths in train_list.txt.")

    model = BraxTransferModel(N_CLASSES).to(DEVICE)
    if torch.cuda.device_count() > 1:
        model = nn.DataParallel(model)
    core_model = _unwrap_model(model)

    resume_info: dict[str, Any] | None = None
    start_epoch = 1
    pretrained_loaded = False
    ckpt_path = finetune_from or chexnet_checkpoint_path() or Path(CKPT_PATH)

    if resume_from is not None:
        resume_path = resume_from.resolve()
        load_brax_checkpoint_state(core_model, resume_path)
        ckpt_path = resume_path
        pretrained_loaded = True
        logger.info("Loaded resume checkpoint weights: %s", resume_path)
    else:
        pretrained_loaded = load_pretrained_backbone(
            core_model,
            ckpt_path,
            logger,
            require_checkpoint=require_checkpoint,
        )

    transfer_config = configure_transfer_learning(
        core_model,
        unfreeze_last_block=unfreeze_last_block,
    )

    optimizer = torch.optim.Adam(trainable_parameters(core_model), lr=learning_rate)
    criterion = nn.BCELoss()

    if resume_from is not None:
        resume_path = resume_from.resolve()
        resume_info = load_resume_checkpoint(
            core_model,
            optimizer,
            resume_path,
            logger,
            learning_rate=learning_rate,
        )
        start_epoch = int(resume_info["start_epoch"])
        logger.info("Resuming from checkpoint: %s (starting epoch %s)", resume_path, start_epoch)

    end_epoch = start_epoch + epochs - 1
    print_transfer_learning_banner(
        pretrained_loaded=pretrained_loaded,
        transfer_config=transfer_config,
        train_samples=len(train_ds),
        val_samples=len(val_ds),
        learning_rate=learning_rate,
        batch_size=batch_size,
        epochs=epochs,
        start_epoch=start_epoch,
        end_epoch=end_epoch,
        checkpoint_path=ckpt_path,
        resumed=resume_from is not None,
    )

    logger.info("Device: %s", DEVICE)
    logger.info("Pretrained loaded: %s", pretrained_loaded)
    logger.info("Transfer learning: %s", transfer_config)
    logger.info("Train samples: %s  Val samples: %s", len(train_ds), len(val_ds))
    logger.info(
        "Epochs this run: %s  Start epoch: %s  End epoch: %s  Batch size: %s  LR: %s",
        epochs,
        start_epoch,
        end_epoch,
        batch_size,
        optimizer.param_groups[0]["lr"],
    )

    if early_stopping_patience is not None:
        logger.info(
            "Early stopping enabled: monitor=val_loss  patience=%s",
            early_stopping_patience,
        )

    history: list[dict[str, Any]] = []
    use_val_loss_for_best = early_stopping_patience is not None
    best_f1 = -1.0
    best_val_loss = float("inf")
    best_epoch = 0
    best_metrics: dict[str, float] = {}
    best_state: dict[str, torch.Tensor] | None = None
    epochs_without_improvement = 0
    early_stopped = False
    best_path = output_dir / "best_model.pth.tar"
    last_path = output_dir / "last_model.pth.tar"

    start_time = time.perf_counter()
    epoch_durations: list[float] = []

    for epoch in range(start_epoch, end_epoch + 1):
        epoch_start = time.perf_counter()
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
        val_loss, val_metrics = evaluate(model, val_loader, criterion)
        record: dict[str, Any] = {
            "epoch": epoch,
            "train_loss": train_loss,
            "val_loss": val_loss,
            **val_metrics,
        }
        history.append(record)

        epoch_elapsed = time.perf_counter() - epoch_start
        epoch_durations.append(epoch_elapsed)
        remaining_epochs = end_epoch - epoch
        avg_epoch = sum(epoch_durations) / len(epoch_durations)
        eta_seconds = avg_epoch * remaining_epochs
        _log_epoch_progress(
            logger,
            epoch=epoch,
            epochs=end_epoch,
            train_loss=train_loss,
            val_loss=val_loss,
            val_metrics=val_metrics,
            eta_seconds=eta_seconds,
        )

        state = _unwrap_model(model).state_dict()
        checkpoint_payload = {
            "epoch": epoch,
            "state_dict": state,
            "optimizer_state_dict": optimizer.state_dict(),
            "class_names": CLASS_NAMES,
            "val_metrics": val_metrics,
            "learning_rate": float(optimizer.param_groups[0]["lr"]),
            "train_samples": len(train_ds),
            "val_samples": len(val_ds),
            "transfer_learning": transfer_config,
            "model_type": "BraxTransferModel",
        }
        torch.save(checkpoint_payload, last_path)

        improved = False
        if use_val_loss_for_best:
            if val_loss < best_val_loss:
                best_val_loss = val_loss
                best_epoch = epoch
                best_metrics = dict(val_metrics)
                best_f1 = val_metrics["f1_micro"]
                best_state = {k: v.cpu().clone() for k, v in state.items()}
                torch.save(checkpoint_payload, best_path)
                improved = True
        elif val_metrics["f1_micro"] >= best_f1:
            best_f1 = val_metrics["f1_micro"]
            best_epoch = epoch
            best_metrics = dict(val_metrics)
            best_val_loss = val_loss
            best_state = {k: v.cpu().clone() for k, v in state.items()}
            torch.save(checkpoint_payload, best_path)
            improved = True

        if early_stopping_patience is not None:
            if improved:
                epochs_without_improvement = 0
            else:
                epochs_without_improvement += 1
                if epochs_without_improvement >= early_stopping_patience:
                    logger.info(
                        "Early stopping triggered at epoch %s (no val_loss improvement for %s epochs).",
                        epoch,
                        early_stopping_patience,
                    )
                    print(
                        f"\nEarly stopping at epoch {epoch} "
                        f"(patience={early_stopping_patience}, best epoch={best_epoch})."
                    )
                    early_stopped = True
                    break

    if best_state is not None:
        core_model.load_state_dict(best_state)
        logger.info("Restored best weights from epoch %s (val_loss=%.4f).", best_epoch, best_val_loss)

    training_time = time.perf_counter() - start_time
    actual_end_epoch = history[-1]["epoch"] if history else start_epoch

    summary: dict[str, Any] = {
        "pipeline": "BRAX",
        "brax_root": str(brax_root),
        "subset_dir": str(subset_dir),
        "output_dir": str(output_dir),
        "epochs": epochs,
        "start_epoch": start_epoch,
        "end_epoch": end_epoch,
        "actual_end_epoch": actual_end_epoch,
        "early_stopping_patience": early_stopping_patience,
        "early_stopped": early_stopped,
        "batch_size": batch_size,
        "learning_rate": float(optimizer.param_groups[0]["lr"]),
        "train_samples": len(train_ds),
        "val_samples": len(val_ds),
        "checkpoint_loaded": pretrained_loaded,
        "checkpoint_source": str(ckpt_path),
        "resumed": resume_from is not None,
        "resume_info": resume_info,
        "transfer_learning": transfer_config,
        "unfreeze_last_block": unfreeze_last_block,
        "model_type": "BraxTransferModel",
        "best_epoch": best_epoch,
        "best_val_loss": best_val_loss if best_val_loss != float("inf") else None,
        "best_val_f1_micro": best_f1,
        "final_metrics": best_metrics,
        "training_time_seconds": round(training_time, 2),
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

    write_loss_csv(output_dir, history)
    write_training_summary(output_dir, summary)

    logger.info(
        "Training complete in %.1fs. Best epoch=%s  F1=%.4f  checkpoint=%s",
        training_time,
        best_epoch,
        best_f1,
        best_path,
    )
    return summary
