"""
BRAX transfer-learning model: frozen DenseNet121 backbone + trainable classifier.

Loads backbone weights from model.pth.tar (CheXNet checkpoint). Does NOT modify model.py.
"""
from __future__ import annotations

import logging
from pathlib import Path
from typing import Any

import torch
import torch.nn as nn
import torchvision

from model import CLASS_NAMES, N_CLASSES, CKPT_PATH

FEATURE_DIM = 1024
CLASSIFIER_HIDDEN_DIM = 512
CLASSIFIER_DROPOUT = 0.3
LAST_DENSE_BLOCK = "denseblock4"


class BraxTransferModel(nn.Module):
    """
    DenseNet121 with a replaced multi-label classifier head for BRAX fine-tuning.

    Classifier: Linear(1024->512) -> ReLU -> Dropout(0.3) -> Linear(512->14) -> Sigmoid
    """

    def __init__(self, num_classes: int = N_CLASSES):
        super().__init__()
        self.densenet121 = torchvision.models.densenet121(weights=None)
        self.densenet121.classifier = build_classifier(num_classes)

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        return self.densenet121(x)


def build_classifier(num_classes: int = N_CLASSES) -> nn.Sequential:
    return nn.Sequential(
        nn.Linear(FEATURE_DIM, CLASSIFIER_HIDDEN_DIM),
        nn.ReLU(inplace=True),
        nn.Dropout(p=CLASSIFIER_DROPOUT),
        nn.Linear(CLASSIFIER_HIDDEN_DIM, num_classes),
        nn.Sigmoid(),
    )


def adapt_state_dict(state_dict: dict[str, torch.Tensor]) -> dict[str, torch.Tensor]:
    """Normalize checkpoint key names from CheXNet / DataParallel exports."""
    clean = {k.replace("module.", ""): v for k, v in state_dict.items()}
    adapted: dict[str, torch.Tensor] = {}
    for key, value in clean.items():
        normalized = (
            key.replace("norm.1", "norm1")
            .replace("norm.2", "norm2")
            .replace("conv.1", "conv1")
            .replace("conv.2", "conv2")
        )
        adapted[normalized] = value
    return adapted


def get_parameter_summary(model: nn.Module) -> dict[str, int]:
    total = sum(p.numel() for p in model.parameters())
    trainable = sum(p.numel() for p in model.parameters() if p.requires_grad)
    return {
        "total_parameters": total,
        "frozen_parameters": total - trainable,
        "trainable_parameters": trainable,
    }


def configure_transfer_learning(
    model: BraxTransferModel,
    *,
    unfreeze_last_block: bool = False,
) -> dict[str, Any]:
    """
    Freeze the full DenseNet backbone except optionally denseblock4 + classifier.
    """
    features = model.densenet121.features
    for param in features.parameters():
        param.requires_grad = False

    last_block_unfrozen = False
    if unfreeze_last_block:
        denseblock4 = getattr(features, LAST_DENSE_BLOCK, None)
        if denseblock4 is not None:
            for param in denseblock4.parameters():
                param.requires_grad = True
            last_block_unfrozen = True

    for param in model.densenet121.classifier.parameters():
        param.requires_grad = True

    summary = get_parameter_summary(model)
    summary["backbone_frozen"] = True
    summary["classifier_replaced"] = True
    summary["unfreeze_last_block"] = unfreeze_last_block
    summary["last_block_unfrozen"] = last_block_unfrozen
    return summary


def load_pretrained_backbone(
    model: BraxTransferModel,
    checkpoint_path: Path,
    logger: logging.Logger | None = None,
    *,
    require_checkpoint: bool = False,
) -> bool:
    """
    Load CheXNet weights from model.pth.tar.

    Backbone (features) weights are loaded; the old classifier head is skipped
    because the BRAX model uses a new classifier architecture.
    """
    if not checkpoint_path.is_file():
        msg = f"Checkpoint not found at {checkpoint_path}"
        if require_checkpoint:
            raise FileNotFoundError(msg)
        if logger:
            logger.warning("%s — backbone will use random initialization.", msg)
        return False

    checkpoint = torch.load(checkpoint_path, map_location="cpu")
    raw = checkpoint.get("state_dict", checkpoint) if isinstance(checkpoint, dict) else checkpoint
    state_dict = adapt_state_dict(raw)

    backbone_state = {
        key: value
        for key, value in state_dict.items()
        if key.startswith("densenet121.features.")
    }

    if not backbone_state:
        if logger:
            logger.warning("No backbone keys found in %s", checkpoint_path)
        return False

    missing, unexpected = model.load_state_dict(backbone_state, strict=False)
    if logger:
        logger.info("Loading pretrained checkpoint: %s", checkpoint_path)
        logger.info("Backbone keys loaded: %s", len(backbone_state))
        if missing:
            logger.debug("Missing keys after backbone load: %s", missing[:5])

    return True


def load_brax_checkpoint_state(
    model: BraxTransferModel,
    checkpoint_path: Path,
) -> dict[str, Any]:
    """Load a full BRAX training checkpoint (resume)."""
    if not checkpoint_path.is_file():
        raise FileNotFoundError(f"Checkpoint not found at {checkpoint_path}")

    checkpoint = torch.load(checkpoint_path, map_location="cpu")
    if not isinstance(checkpoint, dict) or "state_dict" not in checkpoint:
        raise ValueError(f"Checkpoint at {checkpoint_path} has no 'state_dict' key.")

    model.load_state_dict(adapt_state_dict(checkpoint["state_dict"]), strict=False)
    return checkpoint


def trainable_parameters(model: nn.Module) -> list[nn.Parameter]:
    return [p for p in model.parameters() if p.requires_grad]
