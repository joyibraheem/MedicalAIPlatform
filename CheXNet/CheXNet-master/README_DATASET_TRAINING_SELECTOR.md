# CheXNet dataset training selector (BRAX vs VinDr-PCXR)

Use this guide to choose **one** dataset pipeline. BRAX and VinDr-PCXR are fully separate: different scripts, different data folders, different outputs. Running one does **not** affect the other.

All commands assume:

```powershell
cd CheXNet\CheXNet-master
python -m venv .venv
.\.venv\Scripts\activate
pip install -r requirements.txt
```

Shared fine-tuning checkpoint (optional, recommended):

```
CheXNet/CheXNet-master/model.pth.tar
```

If missing, both pipelines train from the ImageNet DenseNet121 backbone only.

---

## Which pipeline should I use?

| Choose | When |
|--------|------|
| **BRAX** | You downloaded the PhysioNet BRAX dataset with `master_spreadsheet.csv` and `Anonymized_DICOMs/` |
| **VinDr-PCXR** | You downloaded PhysioNet VinDr-PCXR v1.0.0 with `image_labels_train.csv` and `train/` DICOM files |

Do **not** mix files between `datasets/BRAX/` and `datasets/VINDR_PCXR/`.

---

## Option 1 — Train on BRAX

### Where to place data

```
CheXNet/CheXNet-master/datasets/BRAX/
  master_spreadsheet.csv          REQUIRED
  Anonymized_DICOMs/              REQUIRED (DICOM .dcm tree)
    id_<patient_id>/Study_.../Series_.../image-....dcm
  images/                         OPTIONAL (PNG fallback only)
```

Nested PhysioNet extract folders are auto-detected (e.g. `datasets/BRAX/brax-1.0.0/...`).

### Required files

| File / folder | Required |
|---------------|----------|
| `master_spreadsheet.csv` | Yes |
| `Anonymized_DICOMs/` | Yes (default DICOM path) |
| `annotations` / bbox CSV | No |

Key spreadsheet columns: **`DicomPath`** (primary), **`PngPath`** (fallback). See `README_BRAX_TRAINING.md` for label mapping.

### Commands

**Subset 100 (smoke test):**

```powershell
python prepare_brax_subset.py --size 100
python train_brax.py --size 100
```

**Subset 1000:**

```powershell
python prepare_brax_subset.py --size 1000
python train_brax.py --size 1000
```

**Subset 3000:**

```powershell
python prepare_brax_subset.py --size 3000
python train_brax.py --size 3000
```

### BRAX-only scripts

| File | Role |
|------|------|
| `brax_config.py` | Paths, defaults, label mapping |
| `brax_labels.py` | CSV column parsing, BRAX → CheXNet labels |
| `brax_path_resolver.py` | `DicomPath` / `PngPath` → file on disk |
| `brax_dicom_loader.py` | DICOM / PNG → PIL RGB |
| `brax_dataset.py` | PyTorch dataset for list files |
| `prepare_brax_subset.py` | Subset + train/val list generation |
| `brax_train_core.py` | Training loop |
| `train_brax.py` | Training CLI |
| `README_BRAX_TRAINING.md` | Detailed BRAX docs |

### BRAX outputs (never mixed with VinDr)

```
datasets/BRAX/subsets/subset_<SIZE>/
  train_list.txt, val_list.txt, subset.csv, meta.json

datasets/BRAX/runs/subset_<SIZE>/
  best_model.pth.tar, last_model.pth.tar, metrics.json, training.log
```

---

## Option 2 — Train on VinDr-PCXR

### Where to place data

```
CheXNet/CheXNet-master/datasets/VINDR_PCXR/
  image_labels_train.csv          REQUIRED
  train/                          REQUIRED (DICOM named by image_id)
    <image_id>.dicom
  annotations_train.csv           OPTIONAL (not used for subset prep)
  test/, image_labels_test.csv    NOT used for training subsets
```

Nested PhysioNet extract folders are auto-detected (e.g. `datasets/VINDR_PCXR/vindr-pcxr-1.0.0/...`).

### Required files

| File / folder | Required |
|---------------|----------|
| `image_labels_train.csv` | Yes |
| `train/` | Yes |
| `annotations_train.csv` | No |

Key CSV columns: **`image_id`**, **`labels`** (51-bit vector), **`rad_id`** (aggregated with OR). See `README_VINDR_PCXR_TRAINING.md` for label mapping.

### Commands

**Subset 100 (smoke test):**

```powershell
python prepare_vindr_subset.py --size 100
python train_vindr.py --size 100
```

**Subset 1000:**

```powershell
python prepare_vindr_subset.py --size 1000
python train_vindr.py --size 1000
```

**Subset 3000:**

```powershell
python prepare_vindr_subset.py --size 3000
python train_vindr.py --size 3000
```

### VinDr-only scripts

| File | Role |
|------|------|
| `vindr_pcxr_config.py` | Paths, defaults |
| `vindr_pcxr_labels.py` | CSV parsing, VinDr 51 → CheXNet 14 labels |
| `vindr_path_resolver.py` | `image_id` → DICOM path |
| `vindr_dicom_loader.py` | DICOM / PNG → PIL RGB |
| `vindr_dataset.py` | PyTorch dataset for list files |
| `prepare_vindr_subset.py` | Subset + train/val list generation |
| `vindr_train_core.py` | Training loop |
| `train_vindr.py` | Training CLI |
| `README_VINDR_PCXR_TRAINING.md` | Detailed VinDr docs |

### VinDr outputs (never mixed with BRAX)

```
datasets/VINDR_PCXR/subsets/subset_<SIZE>/
  train_list.txt, val_list.txt, subset.csv, meta.json

datasets/VINDR_PCXR/runs/subset_<SIZE>/
  best_model.pth.tar, last_model.pth.tar, metrics.json, training.log
```

---

## Shared CLI options (both pipelines)

Both `train_brax.py` and `train_vindr.py` accept:

| Flag | Purpose |
|------|---------|
| `--epochs` | Override default epochs (100→2, 1000→5, 3000→8) |
| `--batch-size` | Override batch size (100→8, 1000/3000→16) |
| `--learning-rate` | Adam LR (default `1e-4`) |
| `--num-workers` | DataLoader workers (default **0 on Windows**, 2 elsewhere) |
| `--finetune-from` | Custom checkpoint path instead of `./model.pth.tar` |

Dataset root overrides:

- BRAX: `--brax-root datasets/BRAX`
- VinDr: `--vindr-root datasets/VINDR_PCXR`

---

## After training: use the new weights for inference

Training saves checkpoints under each dataset's `runs/` folder. The FastAPI / app inference path reads:

```
CheXNet/CheXNet-master/model.pth.tar
```

To deploy a trained model, copy the best checkpoint manually:

```powershell
# After BRAX training (example: subset 1000)
copy datasets\BRAX\runs\subset_1000\best_model.pth.tar model.pth.tar

# After VinDr training (example: subset 1000)
copy datasets\VINDR_PCXR\runs\subset_1000\best_model.pth.tar model.pth.tar
```

Keep a backup of the original `model.pth.tar` before overwriting.

---

## What is shared (intentionally)

These files are used by **both** pipelines for the CheXNet architecture only. They are not dataset-specific:

| File | Shared use |
|------|------------|
| `model.py` | DenseNet121 + 14 CheXNet labels |
| `requirements.txt` | Python dependencies |
| `model.pth.tar` | Optional starting checkpoint |

Neither pipeline imports the other's helpers. Each pipeline only imports its own `brax_*` or `vindr_*` modules plus `model.py`.

---

## Quick verification

```powershell
python prepare_brax_subset.py --help
python train_brax.py --help
python prepare_vindr_subset.py --help
python train_vindr.py --help
```

Check `meta.json` after prepare — it includes `"pipeline": "BRAX"` or `"pipeline": "VINDR_PCXR"` so outputs are easy to identify.
