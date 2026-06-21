"""
Test the /predict/ct endpoint.

Prereqs:
  1. pip install -r requirements.txt   (includes python-multipart, requests)
  2. Start API: python -m uvicorn fastapi_app:app --host 127.0.0.1 --port 8000

Usage: python test_ct_endpoint.py [port]
"""
import sys
import os

try:
    import requests
except ImportError:
    print("Install: pip install requests")
    sys.exit(1)

port = int(sys.argv[1]) if len(sys.argv) > 1 else 8000
base = f"http://127.0.0.1:{port}"
image_path = os.path.join(os.path.dirname(__file__), "sample_xray.jpg")
if not os.path.isfile(image_path):
    print(f"Test image not found: {image_path}")
    sys.exit(1)

print(f"POST {base}/predict/ct with {image_path}")
try:
    with open(image_path, "rb") as f:
        r = requests.post(f"{base}/predict/ct", files={"file": ("ct.jpg", f, "image/jpeg")}, timeout=30)
except requests.exceptions.ConnectionError:
    print("Connection refused. Start the API first:")
    print("  python -m uvicorn fastapi_app:app --host 127.0.0.1 --port", port)
    sys.exit(1)

print(f"Status: {r.status_code}")
body = r.text
print(body[:600] if len(body) > 600 else body)
if r.status_code == 200:
    try:
        j = r.json()
        if j.get("error"):
            print("\n[CT error]", j.get("error"))
        else:
            print("\nPredicted class:", j.get("predicted_class", ""))
            print("Probabilities:", j.get("probabilities", {}))
    except Exception as e:
        print("Parse error:", e)
else:
    sys.exit(1)
