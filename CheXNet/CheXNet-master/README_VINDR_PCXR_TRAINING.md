# CheXNet training on VinDr-PCXR (college workflow)

> **Choosing a dataset?** See `README_DATASET_TRAINING_SELECTOR.md` for BRAX vs VinDr-PCXR side-by-side commands.

Train **CheXNet** (DenseNet121, 14-label chest X-ray model) on subsets of **VinDr-PCXR** (PhysioNet v1.0.0).

This flow is isolated under `CheXNet/CheXNet-master/` and does not modify the ASP.NET MVC app.

> **Note:** Legacy BRAX scripts (`prepare_brax_subset.py`, etc.) remain in this folder for reference but are **not** the active training path.

---

## 1. Prerequisites

```powershell
cd CheXNet\CheXNet-master
python -m venv .venv
.\.venv\Scripts\activate
pip install -r requirements.txt
```

Requires **`pydicom`** (listed in `requirements.txt`) because VinDr-PCXR images are DICOM.

Optional: copy `model.pth.tar` into `CheXNet-master/` for CheXNet fine-tuning (recommended).

---

## 2. Where to put VinDr-PCXR

```
CheXNet/CheXNet-master/datasets/VINDR_PCXR/
  image_labels_train.csv
  annotations_train.csv
  train/
    <anonymous_image_id>.dicom
  test/
  image_labels_test.csv
  annotations_test.csv
```

Images are DICOM files named by hashed SOP Instance UID (`image_id` in CSV). The pipeline tries:

- `train/{image_id}.dicom`
- `train/{image_id}.dcm`
- nested paths under `train/` (rglob fallback)

---

## 3. CSV columns used

### `image_labels_train.csv` (required for subset prep)

| Column | Aliases | Purpose |
|--------|---------|---------|
| `image_id` | Image ID | Links row to DICOM filename |
| `rad_id` | rad_ID | Radiologist id (multiple rows per image aggregated) |
| `labels` | Labels | 51-element binary vector `{0,1,0,...}` |

Label aggregation: if any radiologist marks a pathology as `1`, that index is treated as positive for the image.

### `annotations_train.csv` (optional)

Not required for CheXNet multi-label subset training (bbox labels). Subset prep uses **image-level** labels only.

---

## 4. VinDr -> CheXNet label mapping

VinDr-PCXR uses **51 labels** (36 findings + 15 diagnoses). CheXNet uses **14** ChestX-ray14 labels.

Only VinDr indices mapped in `vindr_pcxr_labels.VINDR_INDEX_TO_CHEXNET` are transferred. Examples:

| VinDr label (index) | CheXNet |
|---------------------|---------|
| Cardiomegaly (5) | Cardiomegaly |
| Consolidation (10) | Consolidation |
| Pleural effusion (16) | Effusion |
| Atelectasis (18) | Atelectasis |
| Infiltration / opacities (1,2,6,8,24) | Infiltration |
| Pneumothorax (26) | Pneumothorax |
| Edema (27) | Edema |
| Pleural thickening (28) | Pleural_Thickening |
| Emphysema (32) | Emphysema |
| Pulmonary fibrosis (23) | Fibrosis |
| Mass-like findings (9,12,13,30,50) | Mass / Nodule |
| Pneumonia diagnoses (38,42,43) | Pneumonia |

**Unmapped CheXNet labels** (stay 0): e.g. **Hernia** — no VinDr equivalent.

Pediatric-specific VinDr labels (Bronchitis, CPAM, etc.) are not mapped unless listed above.

---

## 5. Commands

From `CheXNet\CheXNet-master` with venv active:

### Smoke test (100)

```powershell
python prepare_vindr_subset.py --size 100
python train_vindr.py --size 100
```

### 1000

```powershell
python prepare_vindr_subset.py --size 1000
python train_vindr.py --size 1000
```

### 3000

```powershell
python prepare_vindr_subset.py --size 3000
python train_vindr.py --size 3000
```

---

## 6. Outputs

### After `prepare_vindr_subset.py`

```
datasets/VINDR_PCXR/subsets/subset_<SIZE>/
  train_list.txt
  val_list.txt
  subset.csv
  meta.json
```

### After `train_vindr.py`

```
datasets/VINDR_PCXR/runs/subset_<SIZE>/
  best_model.pth.tar
  last_model.pth.tar
  metrics.json
  training.log
```

Default epochs: 100->2, 1000->5, 3000->8.

---

## 7. Troubleshooting

| Issue | Fix |
|-------|-----|
| `No usable rows found` | Verify `train/` DICOM files match `image_id` in CSV |
| `pydicom` missing | `pip install pydicom` |
| Slow on CPU | Expected; use GPU if available |
| Windows DataLoader error | `python train_vindr.py --size 100 --num-workers 0` |
| Missing `model.pth.tar` | Trains from ImageNet backbone only (see log warning) |

---

## 8. Script reference

| Script | Purpose |
|--------|---------|
| `prepare_vindr_subset.py` | Sample images, write train/val lists |
| `train_vindr.py` | Training CLI |
| `vindr_train_core.py` | Training loop |
| `vindr_pcxr_config.py` | Paths and defaults |
| `vindr_pcxr_labels.py` | CSV parsing and label mapping |
| `vindr_path_resolver.py` | `image_id` -> DICOM path |
| `vindr_dicom_loader.py` | DICOM -> PIL RGB |
| `vindr_dataset.py` | PyTorch dataset |

Unchanged: `model.py`, `read_data.py`, `training_pipeline.py`, `fastapi_app.py`, entire ASP.NET app.
