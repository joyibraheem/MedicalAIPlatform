"""
Reusable BRAX Dataset and DataLoader factory for CheXNet fine-tuning.

Loads DICOM via pydicom, converts grayscale → RGB, resizes to 224×224,
normalizes with ImageNet stats, and returns PyTorch tensors.
"""
from __future__ import annotations

import os
from pathlib import Path
from typing import Tuple

import torch
from torch.utils.data import DataLoader
from torchvision import transforms

from brax_dataset import BraxChexNetDataset


IMAGENET_MEAN = [0.485, 0.456, 0.406]
IMAGENET_STD = [0.229, 0.224, 0.225]


def build_train_transform() -> transforms.Compose:
    return transforms.Compose(
        [
            transforms.Resize(256),
            transforms.RandomResizedCrop(224, scale=(0.9, 1.0)),
            transforms.RandomHorizontalFlip(),
            transforms.ToTensor(),
            transforms.Normalize(IMAGENET_MEAN, IMAGENET_STD),
        ]
    )


def build_val_transform() -> transforms.Compose:
    return transforms.Compose(
        [
            transforms.Resize(256),
            transforms.CenterCrop(224),
            transforms.ToTensor(),
            transforms.Normalize(IMAGENET_MEAN, IMAGENET_STD),
        ]
    )


def create_brax_datasets(
    data_dir: str | Path,
    train_list: str | Path,
    val_list: str | Path,
) -> Tuple[BraxChexNetDataset, BraxChexNetDataset]:
    """Build train/validation BraxChexNetDataset instances."""
    data_dir = str(data_dir)
    train_ds = BraxChexNetDataset(data_dir, str(train_list), transform=build_train_transform())
    val_ds = BraxChexNetDataset(data_dir, str(val_list), transform=build_val_transform())
    return train_ds, val_ds


def create_brax_dataloaders(
    data_dir: str | Path,
    train_list: str | Path,
    val_list: str | Path,
    *,
    batch_size: int,
    num_workers: int = 0,
) -> Tuple[DataLoader, DataLoader, BraxChexNetDataset, BraxChexNetDataset]:
    """Build train/validation DataLoaders for prepared BRAX list files."""
    train_ds, val_ds = create_brax_datasets(data_dir, train_list, val_list)

    train_loader = DataLoader(
        train_ds,
        batch_size=min(batch_size, max(1, len(train_ds))),
        shuffle=True,
        num_workers=num_workers,
        pin_memory=torch.cuda.is_available(),
    )
    val_loader = DataLoader(
        val_ds,
        batch_size=min(batch_size, max(1, len(val_ds))) if len(val_ds) else 1,
        shuffle=False,
        num_workers=num_workers,
        pin_memory=torch.cuda.is_available(),
    )
    return train_loader, val_loader, train_ds, val_ds


def default_num_workers() -> int:
    return 0 if os.name == "nt" else 2
