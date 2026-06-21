# CheXNet Blazor UI (C#) + PyTorch inference

This workspace contains:

- `CheXNet-master/`: the original PyTorch CheXNet code + a local FastAPI wrapper.
- `CheXNet.BlazorApp/`: a .NET 8 Blazor Server UI that uploads a chest X‑ray and displays:
  - 14 pathology probabilities
  - a Grad‑CAM heatmap overlay
  - **AI Chatbot Assistant** (powered by Ollama + open-source LLM)

Model source: [arnoweng/CheXNet](https://github.com/arnoweng/CheXNet)

> **Important**: this is for educational/demo use only and is **not** clinical/diagnostic software.

## Run the Python API (FastAPI)

Open a terminal in `D:\AIProjects\CheXNet\CheXNet-master` and run:

```powershell
.\.venv\Scripts\uvicorn.exe fastapi_app:app --host 0.0.0.0 --port 8000
```

Health check:

```powershell
curl http://localhost:8000/health
```

## Run the Blazor app

Open another terminal in `D:\AIProjects\CheXNet` and run:

```powershell
dotnet run --project .\CheXNet.BlazorApp\CheXNet.BlazorApp.csproj
```

Then open the URL printed by `dotnet run` (typically `https://localhost:xxxx/`).

## Run with Visual Studio (recommended)

1. Open the solution: `CheXNet.sln`
2. Set **Startup Project** to `CheXNet.BlazorApp`
3. Press **F5**

The Blazor app will **auto-start the Python API** in the background and call it at `http://localhost:8000/`.

### If you see “file is locked” build errors in Visual Studio

This happens if you try to rebuild while the app is still running.

- Stop Debugging: **Shift+F5**
- Then: **Build → Rebuild Solution**

## AI Medical Assistant Chatbot (Free & Integrated)

The app includes a **medical AI assistant** (`/chat`) that integrates with CheXNet results to help doctors interpret findings.

### Features

✅ **Completely free** - Uses Ollama + open-source LLMs (runs locally, no API costs)  
✅ **Integrated with CheXNet** - Automatically receives analysis results when you click "Ask Assistant"  
✅ **Medical-focused** - Explains pathologies, interprets probabilities, describes heatmaps  
✅ **Educational tool** - Provides clinical context while maintaining appropriate disclaimers

### Setup Ollama

1. **Download and install Ollama**: https://ollama.ai/download
2. **Pull a medical-capable model** (recommended: `llama3.2` or `mistral`):
   ```powershell
   ollama pull llama3.2
   ```
   Or for a smaller/faster model:
   ```powershell
   ollama pull phi3
   ```
3. **Start Ollama** (runs automatically as a service on Windows, or manually):
   ```powershell
   ollama serve
   ```
   By default, Ollama runs on `http://localhost:11434`

### Using the Medical Assistant

**Workflow:**
1. Upload and analyze an X-ray on the **Analyze** page
2. Click **"Ask Assistant"** button (appears after analysis)
3. The chatbot automatically receives the CheXNet results
4. Ask questions like:
   - "Explain the top findings"
   - "What does Pneumonia mean clinically?"
   - "What does the heatmap show?"
   - "How should I interpret these probabilities?"

**Or use standalone:**
- Navigate to **Assistant** in the app menu
- Ask general questions about thoracic pathologies or CheXNet

### What the Assistant Can Do

- ✅ Explain all 14 pathologies detected by CheXNet
- ✅ Interpret probability scores
- ✅ Describe what Grad-CAM heatmaps highlight
- ✅ Provide clinical context and educational information
- ✅ Suggest relevant additional clinical information

### What the Assistant Cannot Do

- ❌ Provide medical diagnoses
- ❌ Give treatment recommendations
- ❌ Make clinical decisions
- ❌ Replace qualified medical professionals

**Important**: The assistant is an educational tool. Always emphasize that CheXNet results are AI-generated probabilities requiring clinical correlation.

### Configuration

Chat settings in `CheXNet.BlazorApp/appsettings.json`:

```json
{
  "Chat": {
    "BaseUrl": "http://localhost:11434",
    "OllamaModel": "llama3.2"
  }
}
```

Change `OllamaModel` to any model you've pulled (e.g., `mistral`, `phi3`, `qwen2.5`, `llama3.1`).

## Configuration

The Blazor app calls the API base URL from:

- `CheXNet.BlazorApp/appsettings.json` → `CheXNetApi:BaseUrl`

Default:

```json
{
  "CheXNetApi": { "BaseUrl": "http://localhost:8000/" }
}
```

## API response shape

`POST /predict` (multipart form field: `file`) returns JSON:

- `probabilities`: dictionary of `class_name -> probability`
- `topk`: ranked list of the top-K
- `heatmap.image_base64`: PNG image (base64) containing an overlay heatmap

