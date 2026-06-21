
import os
import torch
from PIL import Image
from fastapi_app import predict_ct, LUNGAI_CLASS_NAMES

# Mock image
img = Image.new('RGB', (224, 224), color = (73, 109, 137))

print("Testing predict_ct...")
try:
    result = predict_ct(img)
    print("Result:", result)
except Exception as e:
    print("Error during predict_ct:", e)
    import traceback
    traceback.print_exc()
