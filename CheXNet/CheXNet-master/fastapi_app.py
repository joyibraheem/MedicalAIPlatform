import io
import base64
import os
import time
from typing import Dict, List, Optional, Tuple, Any

import torch
import torch.nn.functional as F
from fastapi import FastAPI, File, UploadFile, HTTPException, Form
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse
from pydantic import BaseModel
import numpy as np
from PIL import Image
from torchvision import transforms

from training_pipeline import run_fine_tune

from model import DenseNet121
from lungai_architecture import ResNetLungCancer, LUNGAI_CLASS_NAMES


# --------- Config ----------
CLASS_NAMES: List[str] = [
    "Atelectasis",
    "Cardiomegaly",
    "Effusion",
    "Infiltration",
    "Mass",
    "Nodule",
    "Pneumonia",
    "Pneumothorax",
    "Consolidation",
    "Edema",
    "Emphysema",
    "Fibrosis",
    "Pleural_Thickening",
    "Hernia",
]

N_CLASSES = len(CLASS_NAMES)
MODEL_CHECKPOINT_PATH = "model.pth.tar"

# LungAI CT model path (optional): AIProjects/LungAI-main/LungAI-main/Model/
_BASE_DIR = os.path.dirname(os.path.abspath(__file__))
# Try multiple possible locations for the model
_default_paths = [
    "D:\\AIProjects\\LungAI-main\\LungAI-main\\Model\\lung_cancer_detection_model.pth",  # Check absolute path first
    os.path.join(_BASE_DIR, "..", "..", "LungAI-main", "LungAI-main", "Model", "lung_cancer_detection_model.pth"),
    os.path.join(_BASE_DIR, "..", "..", "..", "LungAI-main", "LungAI-main", "Model", "lung_cancer_detection_model.pth"),
    os.path.join(_BASE_DIR, "..", "LungAI-main", "LungAI-main", "Model", "lung_cancer_detection_model.pth"),
]
_default_path = None
for path in _default_paths:
    normalized = os.path.normpath(os.path.abspath(path))
    if os.path.isfile(normalized):
        _default_path = normalized
        print(f"[LungAI] Found model at: {_default_path}")
        break
if _default_path is None:
    _default_path = _default_paths[0]  # Use first as fallback
    print(f"[LungAI] Model not found in any location, using default: {_default_path}")

LUNGAI_MODEL_PATH = os.environ.get("LUNGAI_MODEL_PATH", _default_path)
print(f"[LungAI] Using model path: {LUNGAI_MODEL_PATH}")


# --------- Device ----------
device = torch.device("cuda" if torch.cuda.is_available() else "cpu")


# --------- Model Loading ----------
def load_model() -> torch.nn.Module:
    model = DenseNet121(out_size=N_CLASSES).to(device)

    checkpoint = torch.load(MODEL_CHECKPOINT_PATH, map_location=device)
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
    return model


model = load_model()

# --------- LungAI (CT) Model ----------
lungai_model: Optional[ResNetLungCancer] = None
lungai_transform = transforms.Compose([
    transforms.Resize(256),
    transforms.CenterCrop(224),
    transforms.ToTensor(),
    transforms.Normalize(mean=[0.485, 0.456, 0.406], std=[0.229, 0.224, 0.225]),
])


def load_lungai_model() -> Optional[ResNetLungCancer]:
    global lungai_model
    if lungai_model is not None:
        return lungai_model
    path = os.path.normpath(os.path.abspath(LUNGAI_MODEL_PATH))
    # Try to find the model in multiple locations if the default path doesn't exist
    if not os.path.isfile(path):
        # Try additional paths
        additional_paths = [
            "D:\\AIProjects\\LungAI-main\\LungAI-main\\Model\\lung_cancer_detection_model.pth",
            os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(_BASE_DIR))), "LungAI-main", "LungAI-main", "Model", "lung_cancer_detection_model.pth"),
        ]
        for alt_path in additional_paths:
            normalized_alt = os.path.normpath(os.path.abspath(alt_path))
            if os.path.isfile(normalized_alt):
                print(f"[LungAI] Found model at alternative path: {normalized_alt}")
                path = normalized_alt
                break
        else:
            # Still not found after checking all paths
            print(f"[LungAI] Model not found at any path. Checked: {path}")
            return None
    print(f"[LungAI] Loading model from: {path}")
    try:
        try:
            checkpoint = torch.load(path, map_location=device, weights_only=False)
        except TypeError:
            checkpoint = torch.load(path, map_location=device)
        state_dict = checkpoint
        if isinstance(checkpoint, dict):
            state_dict = checkpoint.get("state_dict") or checkpoint.get("model_state_dict") or checkpoint
        if not isinstance(state_dict, dict):
            return None
        # strip DataParallel 'module.' prefix
        clean = {k.replace("module.", ""): v for k, v in state_dict.items()}
        lungai_model = ResNetLungCancer(num_classes=4).to(device)
        lungai_model.load_state_dict(clean, strict=False)
        lungai_model.eval()
        return lungai_model
    except Exception:
        return None


def predict_ct(image: Image.Image) -> Dict[str, Any]:
    m = load_lungai_model()
    if m is None:
        return {
            "scan_type": "CT",
            "error": "LungAI CT model not found or failed to load. Set LUNGAI_MODEL_PATH or place model at LungAI-main/LungAI-main/Model/lung_cancer_detection_model.pth",
            "predicted_class": "",
            "class_names": LUNGAI_CLASS_NAMES,
            "probabilities": {},
        }
    try:
        img = image.convert("RGB")
        x = lungai_transform(img).unsqueeze(0).to(device)
        with torch.no_grad():
            logits = m(x)
            probs = F.softmax(logits, dim=1)[0].cpu().numpy()
        pred_idx = int(logits.argmax(dim=1).item())
        return {
            "scan_type": "CT",
            "predicted_class": LUNGAI_CLASS_NAMES[pred_idx],
            "class_names": LUNGAI_CLASS_NAMES,
            "probabilities": {LUNGAI_CLASS_NAMES[i]: float(probs[i]) for i in range(4)},
        }
    except Exception as e:
        return {
            "scan_type": "CT",
            "error": f"CT inference failed: {e!s}",
            "predicted_class": "",
            "class_names": LUNGAI_CLASS_NAMES,
            "probabilities": {},
        }


# --------- Preprocessing ----------
transform = transforms.Compose(
    [
        transforms.Resize((224, 224)),
        transforms.ToTensor(),
        transforms.Normalize(
            mean=[0.485, 0.456, 0.406],
            std=[0.229, 0.224, 0.225],
        ),
    ]
)


def _preprocess(image: Image.Image) -> Tuple[Image.Image, torch.Tensor]:
    """
    Returns the resized RGB image (224x224) and the normalized tensor [1,3,224,224].
    """
    img = image.convert("RGB").resize((224, 224), resample=Image.BILINEAR)
    tensor = transform(img).unsqueeze(0).to(device)
    return img, tensor


def _forward_logits_and_probs(x: torch.Tensor) -> Tuple[torch.Tensor, torch.Tensor, torch.Tensor]:
    """
    Forward pass that returns:
    - features: [1, C, H, W] feature maps used for Grad-CAM
    - logits:   [1, 14] pre-sigmoid logits
    - probs:    [1, 14] sigmoid probabilities

    We compute logits manually because the repo model includes Sigmoid inside the classifier.
    """
    # DenseNet backbone
    features = model.densenet121.features(x)  # [N, 1024, 7, 7] for DenseNet121
    if torch.is_grad_enabled():
        features.retain_grad()

    out = F.relu(features, inplace=False)
    out = F.adaptive_avg_pool2d(out, (1, 1)).view(out.size(0), -1)

    # classifier is Sequential([Linear, Sigmoid]) in this repo
    linear = model.densenet121.classifier[0]
    logits = linear(out)
    probs = torch.sigmoid(logits)
    return features, logits, probs


def _gradcam_overlay_png_base64(
    resized_rgb_image: Image.Image,
    x: torch.Tensor,
    class_index: int,
    alpha_max: int = 180,
) -> str:
    """
    Computes a Grad-CAM overlay image for a given class index and returns base64 PNG (no data: prefix).
    """
    if class_index < 0 or class_index >= N_CLASSES:
        raise ValueError("class_index out of range")

    model.zero_grad(set_to_none=True)
    features, logits, _probs = _forward_logits_and_probs(x)

    score = logits[0, class_index]
    score.backward()

    grads = features.grad  # [1, C, H, W]
    if grads is None:
        raise RuntimeError("Grad-CAM failed: gradients were not populated.")

    weights = grads.mean(dim=(2, 3), keepdim=True)  # [1, C, 1, 1]
    cam = (weights * features).sum(dim=1)  # [1, H, W]
    cam = F.relu(cam)
    cam = cam - cam.min()
    cam = cam / (cam.max() + 1e-8)
    cam_np = cam.squeeze(0).detach().cpu().numpy()  # [H,W], 0..1

    # Upscale cam to 224x224 and create a red alpha overlay.
    cam_u8 = (cam_np * 255.0).astype(np.uint8)
    cam_img = Image.fromarray(cam_u8, mode="L").resize(resized_rgb_image.size, resample=Image.BILINEAR)
    cam_arr = np.array(cam_img, dtype=np.uint8)

    base_rgba = resized_rgb_image.convert("RGBA")
    overlay = np.zeros((base_rgba.height, base_rgba.width, 4), dtype=np.uint8)
    overlay[..., 0] = 255  # red
    overlay[..., 3] = (cam_arr.astype(np.float32) / 255.0 * float(alpha_max)).astype(np.uint8)
    overlay_img = Image.fromarray(overlay, mode="RGBA")

    blended = Image.alpha_composite(base_rgba, overlay_img)

    buff = io.BytesIO()
    blended.save(buff, format="PNG")
    return base64.b64encode(buff.getvalue()).decode("ascii")


# --------- FastAPI App ----------
app = FastAPI(
    title="CheXNet Inference API",
    description=(
        "Educational FastAPI wrapper around CheXNet, exposing 14 thoracic disease "
        "probabilities and a Grad-CAM heatmap for an input chest X-ray image. "
        "Not for clinical or diagnostic use."
    ),
    version="0.1.0",
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=False,
    allow_methods=["*"],
    allow_headers=["*"],
)


@app.get("/health")
def health() -> Dict[str, str]:
    return {
        "status": "ok",
        "device": str(device),
    }


@app.post("/predict")
async def predict(
    file: UploadFile = File(...),
    heatmap_class: Optional[str] = None,
    top_k: int = 5,
) -> JSONResponse:
    if file.content_type not in ("image/jpeg", "image/png", "image/jpg"):
        raise HTTPException(
            status_code=400,
            detail="Unsupported file type. Please upload a JPEG or PNG chest X-ray image.",
        )

    try:
        contents = await file.read()
        image = Image.open(io.BytesIO(contents))
    except Exception as exc:  # pragma: no cover - defensive
        raise HTTPException(
            status_code=400, detail="Could not read image file."
        ) from exc

    t0 = time.perf_counter()
    resized_img, x = _preprocess(image)

    with torch.no_grad():
        _features, _logits, probs = _forward_logits_and_probs(x)
        probs_1d = probs[0].detach().cpu().numpy().astype(np.float32)

    probabilities: Dict[str, float] = {CLASS_NAMES[i]: float(probs_1d[i]) for i in range(N_CLASSES)}

    # top-k
    k = int(max(1, min(N_CLASSES, top_k)))
    top_indices = probs_1d.argsort()[::-1][:k]
    topk = [{"class_name": CLASS_NAMES[int(i)], "probability": float(probs_1d[int(i)])} for i in top_indices]

    # choose heatmap class
    if heatmap_class is not None:
        if heatmap_class not in CLASS_NAMES:
            raise HTTPException(
                status_code=400,
                detail=f"Unknown heatmap_class '{heatmap_class}'. Must be one of: {', '.join(CLASS_NAMES)}",
            )
        heatmap_index = CLASS_NAMES.index(heatmap_class)
    else:
        heatmap_index = int(top_indices[0])

    # Grad-CAM needs gradients enabled.
    with torch.enable_grad():
        heatmap_png_b64 = _gradcam_overlay_png_base64(resized_img, x, heatmap_index)

    elapsed_ms = int((time.perf_counter() - t0) * 1000)

    # include pneumonia probability for convenience
    pneumonia_prob = probabilities.get("Pneumonia", 0.0)

    return JSONResponse(
        {
            "class_names": CLASS_NAMES,
            "probabilities": probabilities,
            "topk": topk,
            "heatmap": {
                "class_name": CLASS_NAMES[heatmap_index],
                "mime": "image/png",
                "image_base64": heatmap_png_b64,
            },
            "pneumonia_probability": pneumonia_prob,
            "scan_type": "X-ray",
            "device": str(device),
            "inference_ms": elapsed_ms,
        }
    )


# --------- Text (stub) and Chat ----------
class TextRequest(BaseModel):
    text: str


class ChatRequest(BaseModel):
    question: str
    context: Optional[Dict[str, Any]] = None


def predict_text_stub(text: str) -> Dict[str, Any]:
    """Stub for text/NER analysis. Replace with BIOBERT call if service available."""
    text = (text or "").strip()
    if not text:
        return {"scan_type": "text", "entities": [], "summary": "No text provided.", "error": None}
    # Simple keyword-based summary for demo
    words = text.lower().split()
    summary = f"Text analyzed ({len(words)} words). For full NER/MeSH use BioBERT service."
    return {
        "scan_type": "text",
        "entities": [],
        "summary": summary,
        "input_preview": text[:200] + ("..." if len(text) > 200 else ""),
    }


@app.post("/predict/ct")
async def predict_ct_endpoint(file: UploadFile = File(...)) -> JSONResponse:
    # Always return 200 with JSON body (success or "error" key) so client never gets 500
    err_body = {
        "scan_type": "CT",
        "error": "",
        "predicted_class": "",
        "class_names": LUNGAI_CLASS_NAMES,
        "probabilities": {},
    }
    allowed = ("image/jpeg", "image/png", "image/jpg", "image/webp", "application/octet-stream")
    if file.content_type and file.content_type not in allowed and not (file.content_type or "").startswith("image/"):
        err_body["error"] = "Unsupported file type. Use JPEG or PNG."
        return JSONResponse(err_body)
    try:
        contents = await file.read()
        image = Image.open(io.BytesIO(contents))
    except Exception as e:
        err_body["error"] = f"Could not read image: {e!s}"
        return JSONResponse(err_body)
    try:
        result = predict_ct(image)
        return JSONResponse(result)
    except Exception as e:
        err_body["error"] = f"CT analysis failed: {e!s}"
        return JSONResponse(err_body)


@app.post("/predict/text")
async def predict_text_endpoint(req: TextRequest) -> JSONResponse:
    result = predict_text_stub(req.text)
    return JSONResponse(result)


@app.post("/predict/combined")
async def predict_combined(
    xray: Optional[UploadFile] = File(None),
    text: Optional[str] = Form(None),
    ct: Optional[UploadFile] = File(None),
) -> JSONResponse:
    xray_result: Optional[Dict[str, Any]] = None
    text_result: Optional[Dict[str, Any]] = None
    ct_result: Optional[Dict[str, Any]] = None

    if xray and xray.filename:
        try:
            contents = await xray.read()
            image = Image.open(io.BytesIO(contents))
            resized_img, x_t = _preprocess(image)
            with torch.no_grad():
                _f, _l, probs = _forward_logits_and_probs(x_t)
            probs_1d = probs[0].detach().cpu().numpy().astype(np.float32)
            pneumonia_prob = float(probs_1d[CLASS_NAMES.index("Pneumonia")])
            xray_result = {
                "scan_type": "X-ray",
                "pneumonia_probability": pneumonia_prob,
                "probabilities": {CLASS_NAMES[i]: float(probs_1d[i]) for i in range(N_CLASSES)},
            }
        except Exception as e:
            xray_result = {"scan_type": "X-ray", "error": str(e)}

    if text:
        text_result = predict_text_stub(text)

    if ct and ct.filename:
        try:
            contents = await ct.read()
            image = Image.open(io.BytesIO(contents))
            ct_result = predict_ct(image)
        except Exception as e:
            ct_result = {"scan_type": "CT", "error": str(e)}

    return JSONResponse({
        "xray": xray_result,
        "text": text_result,
        "ct": ct_result,
        "summary": "Combined results for X-ray, text, and CT inputs.",
    })


@app.post("/chat")
async def chat_endpoint(req: ChatRequest) -> JSONResponse:
    """Answer questions about the three models' results using provided context."""
    question = (req.question or "").strip().lower()
    ctx = req.context or {}
    answer = "I can answer questions about the X-ray (CheXNet), text, and CT (LungAI) results. "
    if not question:
        return JSONResponse({"answer": answer + "Please ask a specific question."})
    # Simple template-based answers for demo
    if "xray" in question or "pneumonia" in question or "chexnet" in question:
        x = ctx.get("xray") or {}
        prob = x.get("pneumonia_probability")
        if prob is not None:
            answer += f" X-ray (CheXNet) pneumonia probability: {prob:.2%}. "
        else:
            answer += " No X-ray result in context. "
    if "ct" in question or "lung" in question or "cancer" in question or "lungai" in question:
        ct = ctx.get("ct") or {}
        pred = ct.get("predicted_class")
        if pred:
            answer += f" CT (LungAI) predicted class: {pred}. "
        else:
            answer += " No CT result in context. "
    if "text" in question or "ner" in question or "entity" in question:
        tx = ctx.get("text") or {}
        answer += f" Text analysis: {tx.get('summary', 'No text result.')} "
    if answer == "I can answer questions about the X-ray (CheXNet), text, and CT (LungAI) results. ":
        answer += "Ask about 'xray', 'pneumonia', 'CT', 'lung cancer', or 'text' for details."
    return JSONResponse({"answer": answer.strip()})


class TrainingStartRequest(BaseModel):
    model: str
    datasetPath: str


@app.post("/api/training/start")
async def training_start(req: TrainingStartRequest) -> JSONResponse:
    """
    Fine-tune a model from an exported HITL dataset (JSON).
    Called by ASP.NET when modified-sample threshold is reached.
    """
    try:
        result = run_fine_tune(req.model, req.datasetPath)
        return JSONResponse(
            {
                "model": result.model,
                "version": result.version,
                "file_path": result.file_path,
                "accuracy": result.accuracy,
                "f1_score": result.f1_score,
                "loss": result.loss,
                "dataset_size": result.dataset_size,
                "training_log_path": result.training_log_path,
            }
        )
    except Exception as e:
        return JSONResponse({"error": str(e)}, status_code=400)


# --------- BRAX RAD-DINO (Research / Fine-Tuned) ----------
import sys

_RADDINO_DIR = os.path.join(_BASE_DIR, "models", "BRAX_RADDINO")
if _RADDINO_DIR not in sys.path:
    sys.path.insert(0, _RADDINO_DIR)

try:
    from raddino_api import predict_dicom_bytes, predict_image_bytes

    _RADDINO_AVAILABLE = True
except Exception as _raddino_import_err:  # pragma: no cover - optional model package
    _RADDINO_AVAILABLE = False
    print(f"[BRAX_RADDINO] Import failed: {_raddino_import_err!s}")


@app.post("/predict/raddino")
async def predict_raddino(file: UploadFile = File(...)) -> JSONResponse:
    """BRAX fine-tuned RAD-DINO chest X-ray inference (separate from production CheXNet /predict)."""
    if not _RADDINO_AVAILABLE:
        raise HTTPException(
            status_code=503,
            detail="BRAX RAD-DINO model is not available. Check models/BRAX_RADDINO/ installation.",
        )

    allowed = (
        "image/jpeg",
        "image/png",
        "image/jpg",
        "application/dicom",
        "application/octet-stream",
    )
    content_type = (file.content_type or "").lower()
    if content_type and content_type not in allowed and not content_type.startswith("image/"):
        raise HTTPException(
            status_code=400,
            detail="Unsupported file type. Upload JPEG, PNG, or DICOM chest X-ray.",
        )

    try:
        contents = await file.read()
    except Exception as exc:  # pragma: no cover
        raise HTTPException(status_code=400, detail="Could not read uploaded file.") from exc

    if not contents:
        raise HTTPException(status_code=400, detail="Empty upload.")

    name = (file.filename or "").lower()
    is_dicom = content_type == "application/dicom" or name.endswith((".dcm", ".dicm"))

    try:
        if is_dicom:
            result = predict_dicom_bytes(contents)
        else:
            result = predict_image_bytes(contents)
    except Exception as exc:
        raise HTTPException(status_code=400, detail=f"RAD-DINO inference failed: {exc!s}") from exc

    return JSONResponse(result)


if __name__ == "__main__":
    import uvicorn

    uvicorn.run("fastapi_app:app", host="0.0.0.0", port=8000, reload=True)


