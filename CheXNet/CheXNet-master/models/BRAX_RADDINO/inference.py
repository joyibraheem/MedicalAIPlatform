import os
import torch
import torch.nn as nn
from torchvision import transforms
from PIL import Image
import pydicom
import numpy as np
from transformers import AutoModel

# ── 1. Configuration & Labels ──────────────────────────────────────
DEVICE = torch.device('cuda' if torch.cuda.is_available() else 'cpu')
BACKBONE_NAME = 'microsoft/rad-dino'

LABELS = [
    'No Finding', 'Enlarged Cardiomediastinum', 'Cardiomegaly',
    'Lung Lesion', 'Lung Opacity', 'Edema', 'Consolidation',
    'Pneumonia', 'Atelectasis', 'Pneumothorax', 'Pleural Effusion',
    'Pleural Other', 'Fracture', 'Support Devices'
]
NUM_LABELS = len(LABELS)

# ── 2. Model Architecture ──────────────────────────────────────────
class RADDINOClassifier(nn.Module):
    """
    Must match the exact architecture used during training.
    """
    def __init__(self, backbone_name, num_labels, dropout=0.3):
        super().__init__()
        # Load the base transformer
        self.backbone = AutoModel.from_pretrained(backbone_name)
        hidden_size = self.backbone.config.hidden_size
        
        # Custom classification head
        self.head = nn.Sequential(
            nn.LayerNorm(hidden_size),
            nn.Dropout(dropout),
            nn.Linear(hidden_size, 256),
            nn.GELU(),
            nn.Dropout(dropout / 2),
            nn.Linear(256, num_labels)
        )

    def forward(self, pixel_values):
        outputs = self.backbone(pixel_values=pixel_values)
        cls_token = outputs.last_hidden_state[:, 0, :]
        logits = self.head(cls_token)
        return logits

# ── 3. Image Preprocessing ─────────────────────────────────────────
val_transform = transforms.Compose([
    transforms.Resize((224, 224)),
    transforms.ToTensor(),
    transforms.Normalize(mean=[0.485, 0.456, 0.406],
                         std=[0.229, 0.224, 0.225]),
])

# ── 4. Global Model Loader ─────────────────────────────────────────
# We load the model globally so it doesn't reload into memory on every prediction request
_model = None

def load_model(weights_path):
    global _model
    if _model is None:
        print(f"Loading model into {DEVICE} memory...")
        _model = RADDINOClassifier(BACKBONE_NAME, NUM_LABELS)
        _model.load_state_dict(torch.load(weights_path, map_location=DEVICE))
        _model.to(DEVICE)
        _model.eval()
    return _model

# ── 5. Inference Function ──────────────────────────────────────────
def predict_dicom(dicom_path, weights_path='model_output/best_model.pth'):
    """
    Takes a DICOM file path, runs inference, and returns probabilities.
    """
    model = load_model(weights_path)
    
    # Extract pixel array from DICOM
    dcm = pydicom.dcmread(dicom_path)
    pixels = dcm.pixel_array.astype(np.float32)
    
    # Normalize pixel array to 0-255 uint8
    pixels -= pixels.min()
    if pixels.max() != 0:
        pixels /= pixels.max()
    pixels_uint8 = (pixels * 255).astype(np.uint8)
    
    # Convert to standard RGB Image
    image_pil = Image.fromarray(pixels_uint8).convert('RGB')
    
    # Apply standard PyTorch Vision transforms
    image_tensor = val_transform(image_pil).unsqueeze(0).to(DEVICE)
    
    # Run Inference
    with torch.no_grad():
        logits = model(image_tensor)
        probs = torch.sigmoid(logits).cpu().numpy()[0]
        
    # Zip labels with probabilities and sort by confidence
    results = {LABELS[i]: float(probs[i]) for i in range(NUM_LABELS)}
    
    # Sort dictionary by highest probability
    sorted_results = dict(sorted(results.items(), key=lambda item: item[1], reverse=True))
    
    return sorted_results

# ── Example Usage ──────────────────────────────────────────────────
if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser(description="Predict diseases from Chest X-Ray DICOM")
    parser.add_argument("dicom_path", help="Path to the .dcm file")
    parser.add_argument("--weights", default=r"E:\brax\model_output\best_model.pth", help="Path to best_model.pth")
    args = parser.parse_args()
    
    if os.path.exists(args.dicom_path):
        predictions = predict_dicom(args.dicom_path, weights_path=args.weights)
        print("\n=== AI DIAGNOSIS RESULTS ===")
        for condition, probability in predictions.items():
            print(f"{condition:<30}: {probability*100:5.1f}%")
    else:
        print(f"Error: Could not find DICOM file at {args.dicom_path}")
