"""
CheXNet list-file dataset with BRAX DICOM support.

Each line in the list file is:
  <relative_image_path> <14 CheXNet label ints>

DICOM loading (via brax_dicom_loader):
  - pydicom read + RescaleSlope/Intercept
  - min-max normalization to uint8
  - grayscale → 3-channel RGB
  - torchvision transforms resize to 224×224 and convert to tensor
"""
from __future__ import annotations

import os

import torch
from torch.utils.data import Dataset

from brax_dicom_loader import load_brax_image


class BraxChexNetDataset(Dataset):
    """Same list format as read_data.ChestXrayDataSet; loads .dcm via pydicom."""

    def __init__(self, data_dir: str, image_list_file: str, transform=None):
        self.transform = transform
        self.image_names: list[str] = []
        self.labels: list[list[int]] = []

        with open(image_list_file, "r", encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if not line:
                    continue
                items = line.split()
                image_name = os.path.join(data_dir, items[0])
                label = [int(i) for i in items[1:]]
                self.image_names.append(image_name)
                self.labels.append(label)

    def __len__(self) -> int:
        return len(self.image_names)

    def __getitem__(self, index: int):
        image = load_brax_image(self.image_names[index])
        label = self.labels[index]
        if self.transform is not None:
            image = self.transform(image)
        return image, torch.FloatTensor(label)
