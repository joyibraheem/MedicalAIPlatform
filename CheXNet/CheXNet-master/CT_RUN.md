# CT Scan API – Build and Run

## Blazor app (MedAI Pro)

Build from solution folder or Blazor app folder:

```bash
cd d:\AIProjects\CheXNet\CheXNet.BlazorApp
dotnet build
```

Run:

```bash
dotnet run
```

## Python API (for X-Ray + CT)

1. Install dependencies (from `CheXNet-master`):

   ```bash
   cd d:\AIProjects\CheXNet\CheXNet-master
   pip install -r requirements.txt
   pip install requests
   ```

2. Start the API (default port 8000):

   ```bash
   python -m uvicorn fastapi_app:app --host 127.0.0.1 --port 8000
   ```

3. Test the CT endpoint:

   ```bash
   python test_ct_endpoint.py 8000
   ```

You should see either a predicted class and probabilities, or an error message in the response (e.g. model file path).

## LungAI model path

CT uses: `d:\AIProjects\LungAI-main\LungAI-main\Model\lung_cancer_detection_model.pth`

To use another path, set:

```bash
set LUNGAI_MODEL_PATH=d:\path\to\lung_cancer_detection_model.pth
```
