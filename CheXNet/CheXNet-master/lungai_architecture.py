"""
LungAI CT scan model - ResNet50-based lung cancer classification.
Classes: Adenocarcinoma, Large Cell Carcinoma, Normal, Squamous Cell Carcinoma.
"""
import torch
import torch.nn as nn
from torchvision.models import resnet50

try:
    from torchvision.models import ResNet50_Weights
    _HAS_WEIGHTS_ENUM = True
except ImportError:
    _HAS_WEIGHTS_ENUM = False


def _resnet50_with_weights(use_pretrained):
    if use_pretrained and _HAS_WEIGHTS_ENUM:
        return resnet50(weights=ResNet50_Weights.IMAGENET1K_V1)
    # Older torchvision or no pretrained: backbone will be overwritten by state_dict
    return resnet50(weights=None)


class ResNetLungCancer(nn.Module):
    def __init__(self, num_classes=4, use_pretrained=True):
        super(ResNetLungCancer, self).__init__()
        self.resnet = _resnet50_with_weights(use_pretrained)
        num_ftrs = self.resnet.fc.in_features
        self.resnet.fc = nn.Identity()
        self.fc = nn.Sequential(
            nn.Linear(num_ftrs, 256),
            nn.ReLU(),
            nn.Dropout(0.5),
            nn.Linear(256, num_classes),
        )

    def forward(self, x):
        x = self.resnet(x)
        return self.fc(x)


LUNGAI_CLASS_NAMES = [
    "Adenocarcinoma",
    "Large Cell Carcinoma",
    "Normal",
    "Squamous Cell Carcinoma",
]
