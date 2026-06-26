# BRAX Fine-Tuning Workflow (CheXNet)

Offline research workflow for fine-tuning the existing **CheXNet** model (`model.py` / DenseNet121) on the **BRAX** dataset.

This pipeline is isolated under `CheXNet/CheXNet-master/`. It does **not** modify:

- ASP.NET MVC (Controllers, Views, Services)
- FastAPI inference endpoints
- Authentication
- `training_pipeline.py` (HITL)

Production inference remains untouched.

---

## 1. Prerequisites

From the project root:

```powershell
cd CheXNet\CheXNet-master
python -m venv .venv
.\.venv\Scripts\activate
pip install -r requirements.txt
```

Required packages: `torch`, `torchvision`, `numpy`, `pillow`, `pydicom`, `scikit-learn`.

Place your CheXNet weights at:

```
CheXNet/CheXNet-master/model.pth.tar
```

Fine-tuning always loads this checkpoint when present. If it is missing, training falls back to the ImageNet backbone only (use `--require-checkpoint` to abort instead).

---

## 2. Dataset layout

Expected location:

```
CheXNet/CheXNet-master/datasets/BRAX/
  master_spreadsheet.csv
  Anonymized_DICOMs/
    id_<patient_id>/
      Study_<study_uid>/
        Series_<series_uid>/
          image-<sop_uid>.dcm
```

### ZIP delivery

If the dataset arrives as a ZIP inside `datasets/BRAX/`:

- `validate_brax_dataset.py` and `train_brax.py` extract it automatically
- Folder structure is preserved
- Existing files are **never overwritten** (skipped safely)

---

## 3. Step 1 — Validate dataset (run first)

```powershell
python validate_brax_dataset.py
```

This verifies:

| Check | Description |
|-------|-------------|
| CSV exists | `master_spreadsheet.csv` |
| DICOM folder | `Anonymized_DICOMs/` |
| Row mapping | Every CSV row → existing DICOM |
| Duplicates | Duplicated resolved paths |
| Statistics | Row counts + CheXNet label distribution |
| Packages | torch, pydicom, sklearn, etc. |
| Checkpoint | `model.pth.tar` |
| Disk space | ≥ 2 GB free recommended |
| Device | CUDA or CPU |

Optional JSON report:

```powershell
python validate_brax_dataset.py --save-report datasets/BRAX/validation_report.json
```

---

## 4. Step 2 — Prepare class-diverse subset

Subsets are written to `datasets/BRAX/subsets/subset_<SIZE>/`. The **original** spreadsheet and DICOM tree are never modified.

Supported sizes: **100, 300, 600, 1000, 3000**

Sampling uses round-robin selection across CheXNet label buckets to preserve class diversity.

```powershell
python prepare_brax_subset.py --size 100
python prepare_brax_subset.py --size 300
python prepare_brax_subset.py --size 600
python prepare_brax_subset.py --size 1000
python prepare_brax_subset.py --size 3000
```

Each subset folder contains:

```
datasets/BRAX/subsets/subset_<SIZE>/
  train_list.txt      # relative DICOM path + 14 CheXNet labels
  val_list.txt
  subset.csv          # selected rows + resolved paths
  meta.json           # label coverage, seed, counts
```

---

## 5. Step 3 — Fine-tune CheXNet

`train_brax.py` runs pre-training validation automatically, then fine-tunes from `model.pth.tar`.

### Exact commands (two steps each)

**100 images**

```powershell
python prepare_brax_subset.py --size 100
python train_brax.py --size 100
```

**300 images**

```powershell
python prepare_brax_subset.py --size 300
python train_brax.py --size 300
```

**600 images**

```powershell
python prepare_brax_subset.py --size 600
python train_brax.py --size 600
```

**1000 images**

```powershell
python prepare_brax_subset.py --size 1000
python train_brax.py --size 1000
```

**3000 images**

```powershell
python prepare_brax_subset.py --size 3000
python train_brax.py --size 3000
```

### Windows tip

If DataLoader workers fail on Windows:

```powershell
python train_brax.py --size 100 --num-workers 0
```

### Require checkpoint

```powershell
python train_brax.py --size 100 --require-checkpoint
```

### Transfer learning (default)

BRAX training uses `brax_model.py` (not `model.py`):

- Loads **backbone weights** from `model.pth.tar`
- **Replaces** the classifier with `Linear(1024→512) → ReLU → Dropout(0.3) → Linear(512→14) → Sigmoid`
- **Freezes** the full DenseNet backbone; only the classifier trains by default

Optional gradual unfreezing:

```powershell
python train_brax.py --size 300 --unfreeze-last-block --num-workers 0
```

This unfreezes `denseblock4` plus the classifier. Production inference still uses unchanged `model.py`.

### Resume training

Continue from a previous BRAX run checkpoint instead of `model.pth.tar`.
Each resume still writes to a **new** timestamped folder under `model_versions/`.

```powershell
python train_brax.py --size 100 --resume model_versions/BRAX_20260626_174039_size100_ep2_lr1e-04/best_model.pth.tar
```

Resume with a different subset size (uses the prepared subset for `--size`):

```powershell
python prepare_brax_subset.py --size 300
python train_brax.py --size 300 --resume model_versions/BRAX_20260626_174039_size100_ep2_lr1e-04/best_model.pth.tar
```

Run additional epochs in the resumed session:

```powershell
python train_brax.py --size 100 --epochs 3 --resume model_versions/BRAX_xxx/best_model.pth.tar --num-workers 0
```

On resume, the CLI prints:

- Starting checkpoint path
- Starting epoch (previous checkpoint epoch + 1)
- Current learning rate
- Total trainable parameters

If the checkpoint contains `optimizer_state_dict`, optimizer momentum/state is restored.
Older checkpoints without optimizer state still load model weights and continue with a fresh optimizer.

---

## 6. Training outputs (timestamped, never overwritten)

Each run creates a unique folder under:

```
CheXNet/CheXNet-master/model_versions/
  BRAX_YYYYMMDD_HHMMSS_size<SIZE>_ep<EPOCHS>_lr<LR>/
    best_model.pth.tar
    last_model.pth.tar
    loss.csv
    metrics.json
    training_summary.txt
    training.log
```

After training completes, the console prints:

- Accuracy
- Precision (micro)
- Recall (micro)
- F1 (micro)
- ROC-AUC (macro)
- Training time
- Best epoch
- Output folder

---

## 7. Default hyperparameters

| Subset | Epochs | Batch size |
|--------|--------|------------|
| 100 | 2 | 8 |
| 300 | 3 | 12 |
| 600 | 4 | 16 |
| 1000 | 5 | 16 |
| 3000 | 8 | 16 |

Override with `--epochs`, `--batch-size`, `--learning-rate`.

---

## 8. Image pipeline

Handled by `brax_dicom_loader.py`, `brax_dataset.py`, and `brax_dataloader.py`:

1. Read DICOM with **pydicom**
2. Apply RescaleSlope / RescaleIntercept
3. Min-max normalize to `[0, 255]`
4. Grayscale → 3-channel RGB
5. Resize to **224×224** (via 256 + crop)
6. ImageNet normalization → **PyTorch tensor**

Model architecture: **`model.py`** → `DenseNet121` (unchanged).

---

## 9. Label mapping (BRAX → CheXNet)

Only BRAX positives (`1`) map to CheXNet labels. See `brax_config.py` → `BRAX_TO_CHEXNET`.

Examples:

| BRAX label | CheXNet label(s) |
|------------|------------------|
| Pleural Effusion | Effusion |
| Lung Opacity | Infiltration |
| Lung Lesion | Mass, Nodule |
| Pleural Other | Pleural_Thickening |

---

## 10. Script reference

| Script | Purpose |
|--------|---------|
| `validate_brax_dataset.py` | Dataset + environment validation |
| `prepare_brax_subset.py` | Class-diverse subset generation |
| `brax_model.py` | BRAX transfer-learning model (frozen backbone + new head) |
| `train_brax.py` | Fine-tuning CLI |
| `brax_data_validation.py` | Validation logic + ZIP extraction |
| `brax_subset_sampler.py` | Class-diverse sampling |
| `brax_dataloader.py` | Dataset + DataLoader factory |
| `brax_dataset.py` | `BraxChexNetDataset` |
| `brax_dicom_loader.py` | DICOM → PIL RGB |
| `brax_path_resolver.py` | CSV → file path resolution |
| `brax_labels.py` | BRAX → CheXNet label vectors |
| `brax_train_core.py` | Training loop, metrics, checkpoints |
| `brax_config.py` | Paths, sizes, label mapping |
| `model.py` | CheXNet architecture (reused, not duplicated) |

---

## 11. Troubleshooting

**`READY FOR TRAINING: NO`**

- Confirm `master_spreadsheet.csv` and `Anonymized_DICOMs/` exist
- Run validation and inspect missing-image count
- Check `DicomPath` values match on-disk paths

**`No usable rows found`**

- DICOM tree incomplete or paths in CSV do not match folder layout

**Low disk space warning**

- Free space before large subsets (3000 images + checkpoints)

**Slow on CPU**

- Expected for 1000/3000 subsets; use GPU when available

---

## 12. Quick checklist

1. Copy BRAX → `datasets/BRAX/` (or drop ZIP there)
2. Copy `model.pth.tar` → `CheXNet-master/`
3. `python validate_brax_dataset.py`
4. `python prepare_brax_subset.py --size <N>`
5. `python train_brax.py --size <N>`
6. Inspect `model_versions/BRAX_*` for checkpoints and metrics
