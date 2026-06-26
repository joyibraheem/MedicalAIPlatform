# CheXNet training on BRAX (college workflow)

> **Choosing a dataset?** See `README_DATASET_TRAINING_SELECTOR.md` for BRAX vs VinDr-PCXR side-by-side commands.

Train **CheXNet** (DenseNet121, 14-label chest X-ray model) on subsets of the **BRAX** dataset.

This flow is independent of the ASP.NET web app. It does not modify inference, MVC, or database code.

---

## 1. Prerequisites on the college machine

From the project folder:

```powershell
cd CheXNet\CheXNet-master
python -m venv .venv
.\.venv\Scripts\activate
pip install -r requirements.txt
```

Required packages include **`pydicom`** for reading `.dcm` files.

Optional but recommended:

- NVIDIA GPU + CUDA-enabled PyTorch
- Existing CheXNet weights: `model.pth.tar` in `CheXNet-master/` (fine-tuning starting point)

---

## 2. Where to put BRAX (DICOM layout)

Copy the PhysioNet BRAX download into:

```
CheXNet/CheXNet-master/datasets/BRAX/
```

Expected layout:

```
datasets/BRAX/
  master_spreadsheet.csv
  Anonymized_DICOMs/
    id_<patient_id>/
      Study_<study_uid>/
        Series_<series_uid>/
          image-<sop_uid>.dcm
```

Example:

```
Anonymized_DICOMs/
└── id_00082e3a-ec11c281-24a79518-35d3cc78-22432fb1
    ├── Study_09342613.22970294.40563343.35634289.53163857
    │   ├── Series_34523850.21768222.07508551.49190893.14603932
    │   │   └── image-48219538-15808688-10728535-52591088-74513595.dcm
    │   └── Series_46177599.95157937.50203011.63555832.78161828
    │       └── image-16153862-94805167-26028517-34518684-13054667.dcm
    └── Study_51027964.83117427.20948980.39828954.71003607
        ├── Series_57104384.74837822.26263330.97688944.88328246
        │   └── image-48651870-23127024-63651831-17193122-94277772.dcm
        └── Series_72993604.79060724.14705971.37953714.05369399
            └── image-08788867-77959894-95405066-47915205-10581326.dcm
```

Optional PNG folder (only used if `DicomPath` is missing and `--no-prefer-dicom`):

```
  images/
```

### Files that must exist before running

| File / folder | Required |
|---------------|----------|
| `datasets/BRAX/master_spreadsheet.csv` | Yes |
| `datasets/BRAX/Anonymized_DICOMs/` (`.dcm` tree) | Yes |
| `model.pth.tar` | No (ImageNet backbone used if missing) |
| Python venv + `requirements.txt` (includes `pydicom`) | Yes |

---

## 3. CSV → DICOM path mapping

`prepare_brax_subset.py` uses these spreadsheet columns:

| Priority | Column | Aliases |
|----------|--------|---------|
| 1 (default) | **`DicomPath`** | `DICOMPath`, `dicom_path` |
| 2 (fallback) | **`PngPath`** | `PNGPath`, `png_path`, `ImagePath`, `image_path` |

For each row, the path string from `DicomPath` is resolved in this order:

1. `{brax_root}/{value from DicomPath}`
2. `{brax_root}/Anonymized_DICOMs/{value}` if value does not already start with `Anonymized_DICOMs/`
3. `{brax_root}/Anonymized_DICOMs/{value}` if value starts with `id_...`
4. `{brax_root}/images/{value}` (PNG fallback only)

The resolved relative path is written into `train_list.txt` / `val_list.txt`. Training loads `.dcm` files with **pydicom** and converts them to RGB tensors.

---

## 4. Commands (exact college workflow)

All commands assume you are in:

```
CheXNet\CheXNet-master
```

with the virtual environment activated.

### Smoke test — 100 images

```powershell
python prepare_brax_subset.py --size 100
python train_brax.py --size 100
```

### Training — 1000 images

```powershell
python prepare_brax_subset.py --size 1000
python train_brax.py --size 1000
```

### Training — 3000 images

```powershell
python prepare_brax_subset.py --size 3000
python train_brax.py --size 3000
```

---

## 5. What each step produces

### After `prepare_brax_subset.py`

```
datasets/BRAX/subsets/subset_<SIZE>/
  train_list.txt      # relative .dcm path + 14 CheXNet labels
  val_list.txt
  subset.csv          # selected rows + resolved absolute paths
  meta.json
```

### After `train_brax.py`

```
datasets/BRAX/runs/subset_<SIZE>/
  best_model.pth.tar
  last_model.pth.tar
  metrics.json
  training.log
```

Default training settings:

| Subset | Epochs | Batch size |
|--------|--------|------------|
| 100 | 2 | 8 |
| 1000 | 5 | 16 |
| 3000 | 8 | 16 |

---

## 6. Label mapping (BRAX → CheXNet)

Only BRAX positives (`1`) are mapped. See previous mapping table in project docs.

---

## 7. Troubleshooting

**`No usable rows found`**

- Check `DicomPath` values in CSV match files under `Anonymized_DICOMs/`.
- Open `subset.csv` after a successful run and verify `absolute_image_path`.

**`pydicom` import error**

- Run `pip install pydicom` or reinstall `requirements.txt`.

**Slow training on CPU**

- Expected for 1000/3000 subsets.

**Windows DataLoader errors**

- Run with `--num-workers 0`:

```powershell
python train_brax.py --size 100 --num-workers 0
```

---

## 8. Script reference

| Script | Purpose |
|--------|---------|
| `prepare_brax_subset.py` | Sample BRAX rows, resolve DICOM paths |
| `train_brax.py` | Training CLI |
| `brax_train_core.py` | Training loop |
| `brax_path_resolver.py` | CSV → DICOM file resolution |
| `brax_dicom_loader.py` | `.dcm` → PIL RGB |
| `brax_dataset.py` | Dataset loader for list files |
| `brax_config.py` | Paths and label mapping |
| `brax_labels.py` | BRAX → CheXNet labels |

Existing `read_data.py`, `model.py`, `training_pipeline.py`, and `fastapi_app.py` are unchanged.
