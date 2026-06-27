# BRAX — 10K Random Sample Downloader

Downloads a random sample of **10,000 images** from the
[BRAX Brazilian Chest X-Ray dataset (v1.1.0)](https://www.physionet.org/content/brax/1.1.0/)
hosted on PhysioNet, while preserving the original folder structure.

---

## Prerequisites

### 1. PhysioNet Credentialed Access
The BRAX dataset requires you to:
1. Create a free account at https://physionet.org
2. Complete the **required training** (CITI "Data or Specimens Only Research" course)
3. Sign the **Data Use Agreement** on the dataset page:
   https://www.physionet.org/content/brax/1.1.0/

### 2. Python 3.10+

### 3. Install dependencies
```bash
pip install requests tqdm
# pandas is optional but recommended for CSV inspection
pip install pandas
```

---

## Usage

### Basic (default: 10,000 images, seed=42)
```bash
python download_sample.py --username YOUR_USERNAME --password YOUR_PASSWORD
```

### Custom sample size
```bash
python download_sample.py -u YOUR_USERNAME -p YOUR_PASSWORD --n 5000
```

### Different random seed (for reproducibility)
```bash
python download_sample.py -u YOUR_USERNAME -p YOUR_PASSWORD --seed 123
```

### Resume an interrupted download
```bash
python download_sample.py -u YOUR_USERNAME -p YOUR_PASSWORD --resume
```

### Only generate the file list (no image download)
```bash
python download_sample.py -u YOUR_USERNAME -p YOUR_PASSWORD --list-only
```

### Increase download speed with more parallel workers
```bash
python download_sample.py -u YOUR_USERNAME -p YOUR_PASSWORD --workers 8
```

### Specify a custom output directory
```bash
python download_sample.py -u YOUR_USERNAME -p YOUR_PASSWORD --out D:\my_brax_sample
```

---

## All Options

| Flag | Default | Description |
|---|---|---|
| `--username` / `-u` | *(required)* | PhysioNet username |
| `--password` / `-p` | *(required)* | PhysioNet password |
| `--n` | `10000` | Number of images to download |
| `--seed` | `42` | Random seed (for reproducibility) |
| `--out` | `./brax_sample/` | Output directory |
| `--workers` | `4` | Parallel download threads |
| `--resume` | off | Resume a previously interrupted run |
| `--list-only` | off | Only save the manifest CSV, don't download |

---

## Output Structure

```
brax_sample/
│
├── master_spreadsheet.csv      ← Full dataset index (cached locally)
├── sample_manifest.csv         ← Your 10K sample index with all labels
├── failed_downloads.txt        ← Any files that failed (if any)
│
└── images/
    └── id_<PatientID>/
        └── Study_<StudyUID>/
            └── Series_<SeriesUID>/
                └── image-<SOPInstanceUID>.png
```

The folder structure mirrors the remote PhysioNet structure exactly,
so paths in `sample_manifest.csv` (`PngPath` column) map 1:1 to local files.

---

## About the Dataset

| Property | Value |
|---|---|
| Total images | 40,967 |
| Total studies | 24,959 |
| Total patients | 19,351 |
| Labels | 14 (Atelectasis, Cardiomegaly, Consolidation, Edema, …) |
| Image format | PNG (also DICOM available) |
| Full size | ~300 GB |

**Citation:**
> Reis, E. P. et al. (2022). *BRAX, a Brazilian labeled chest X-ray dataset* (version 1.1.0). PhysioNet. https://doi.org/10.13026/grwk-yh18
