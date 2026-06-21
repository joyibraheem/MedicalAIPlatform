import torch
import torch.nn as nn
from torchvision import transforms
from PIL import Image

# import model architecture from repo
from model import DenseNet121

# --------- Load model ----------
device = torch.device("cuda" if torch.cuda.is_available() else "cpu")

# Original CheXNet is 14-class; we'll load it as-is and then extract the Pneumonia probability (class index 6)
N_CLASSES = 14
PNEUMONIA_CLASS_INDEX = 6

model = DenseNet121(out_size=N_CLASSES).to(device)

checkpoint = torch.load("model.pth.tar", map_location=device)
state_dict = checkpoint["state_dict"] if "state_dict" in checkpoint else checkpoint

# remove 'module.' prefix if present (from DataParallel)
clean_state_dict = {k.replace("module.", ""): v for k, v in state_dict.items()}

# adapt older DenseNet naming (norm.1 -> norm1, conv.1 -> conv1, etc.) to current torchvision conventions
adapted_state_dict = {}
for k, v in clean_state_dict.items():
    new_key = (
        k.replace("norm.1", "norm1")
         .replace("norm.2", "norm2")
         .replace("conv.1", "conv1")
         .replace("conv.2", "conv2")
    )
    adapted_state_dict[new_key] = v

# load with strict=False to tolerate any non-critical leftover mismatches
model.load_state_dict(adapted_state_dict, strict=False)
model.eval()

# --------- Preprocessing ----------
transform = transforms.Compose([
    transforms.Resize((224, 224)),
    transforms.ToTensor(),
    transforms.Normalize(
        mean=[0.485, 0.456, 0.406],
        std=[0.229, 0.224, 0.225]
    ),
])

def predict(image_path):
    img = Image.open(image_path).convert("RGB")
    img = transform(img).unsqueeze(0).to(device)

    with torch.no_grad():
        outputs = model(img)  # shape [1, 14], already passed through sigmoid in the model
        pneumonia_prob = float(outputs[0, PNEUMONIA_CLASS_INDEX].item())

    return pneumonia_prob

# --------- Run test ----------
if __name__ == "__main__":
    prob = predict("sample_xray.jpg")
    print(f"Pneumonia probability: {prob:.4f}")
