# encoding: utf-8

"""
The main CheXNet model implementation.
"""

import os
import numpy as np
import torch
import torch.nn as nn
import torch.backends.cudnn as cudnn
import torchvision
import torchvision.transforms as transforms
from torch.utils.data import DataLoader
from read_data import ChestXrayDataSet
try:
    from sklearn.metrics import roc_auc_score  # type: ignore
except Exception:  # pragma: no cover
    roc_auc_score = None  # optional dependency; only needed for evaluation

CKPT_PATH = 'model.pth.tar'
N_CLASSES = 14
CLASS_NAMES = [ 'Atelectasis', 'Cardiomegaly', 'Effusion', 'Infiltration', 'Mass', 'Nodule', 'Pneumonia',
                'Pneumothorax', 'Consolidation', 'Edema', 'Emphysema', 'Fibrosis', 'Pleural_Thickening', 'Hernia']
DATA_DIR = './ChestX-ray14/images'
TEST_IMAGE_LIST = './ChestX-ray14/labels/test_list.txt'
BATCH_SIZE = 64

# تحديد الجهاز (CPU أو GPU)
device = torch.device("cuda" if torch.cuda.is_available() else "cpu")


class DenseNet121(nn.Module):
    """Modified DenseNet121 with sigmoid classifier."""
    def __init__(self, out_size):
        super(DenseNet121, self).__init__()
        self.densenet121 = torchvision.models.densenet121(pretrained=True)
        num_ftrs = self.densenet121.classifier.in_features
        self.densenet121.classifier = nn.Sequential(
            nn.Linear(num_ftrs, out_size),
            nn.Sigmoid()
        )

    def forward(self, x):
        x = self.densenet121(x)
        return x


def compute_AUCs(gt, pred):
    """Computes Area Under the Curve (AUC) from prediction scores."""
    if roc_auc_score is None:
        raise ImportError(
            "scikit-learn is required for compute_AUCs(). Install it with: pip install scikit-learn"
        )
    AUROCs = []
    gt_np = gt.cpu().numpy()
    pred_np = pred.cpu().numpy()
    for i in range(N_CLASSES):
        AUROCs.append(roc_auc_score(gt_np[:, i], pred_np[:, i]))
    return AUROCs


def main():
    cudnn.benchmark = True

    # initialize and load the model
    model = DenseNet121(N_CLASSES).to(device)
    model = torch.nn.DataParallel(model).to(device)

    if os.path.isfile(CKPT_PATH):
        print("=> loading checkpoint")
        checkpoint = torch.load(CKPT_PATH, map_location=device)
        model.load_state_dict(checkpoint['state_dict'])
        print("=> loaded checkpoint")
    else:
        print("=> no checkpoint found")

    normalize = transforms.Normalize([0.485, 0.456, 0.406],
                                     [0.229, 0.224, 0.225])

    test_dataset = ChestXrayDataSet(
        data_dir=DATA_DIR,
        image_list_file=TEST_IMAGE_LIST,
        transform=transforms.Compose([
            transforms.Resize(256),
            transforms.TenCrop(224),
            transforms.Lambda(
                lambda crops: torch.stack([transforms.ToTensor()(crop) for crop in crops])
            ),
            transforms.Lambda(
                lambda crops: torch.stack([normalize(crop) for crop in crops])
            )
        ])
    )

    test_loader = DataLoader(
        dataset=test_dataset, batch_size=BATCH_SIZE,
        shuffle=False, num_workers=8, pin_memory=True
    )

    # initialize the ground truth and output tensor
    gt = torch.FloatTensor().to(device)
    pred = torch.FloatTensor().to(device)

    # switch to evaluate mode
    model.eval()

    with torch.no_grad():  # مهم عشان مانحتاجش لحساب التدرجات
        for i, (inp, target) in enumerate(test_loader):
            target = target.to(device)
            gt = torch.cat((gt, target), 0)
            bs, n_crops, c, h, w = inp.size()
            input_var = inp.view(-1, c, h, w).to(device)
            output = model(input_var)
            output_mean = output.view(bs, n_crops, -1).mean(1)
            pred = torch.cat((pred, output_mean), 0)

    AUROCs = compute_AUCs(gt, pred)
    AUROC_avg = np.array(AUROCs).mean()
    print(f'The average AUROC is {AUROC_avg:.3f}')
    for i in range(N_CLASSES):
        print(f'The AUROC of {CLASS_NAMES[i]} is {AUROCs[i]:.3f}')


if __name__ == '__main__':
    main()
