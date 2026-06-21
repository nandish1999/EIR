#LANDSCAPE FEATURE EXTRACTION
import os
import re
import torch
import torchvision.transforms as transforms
from torchvision import models
from torchvision.datasets import ImageFolder
from torch.utils.data import DataLoader

import numpy as np
from tqdm import tqdm
import cv2

# ------------------------------------------------------------
# CONFIG
# ------------------------------------------------------------
BASE_DIR      = os.path.dirname(os.path.abspath(__file__))
DATASET_PATH  = os.path.join(BASE_DIR, "seg_train")
OUTPUT_FOLDER = os.path.join(BASE_DIR, "outputs")

os.makedirs(OUTPUT_FOLDER, exist_ok=True)

BATCH_SIZE = 32
DEVICE = "cuda" if torch.cuda.is_available() else "cpu"
print(f"Using device: {DEVICE}")

OUTPUT_FEATURES     = os.path.join(OUTPUT_FOLDER, "resnet_features.npy")
OUTPUT_LABELS       = os.path.join(OUTPUT_FOLDER, "resnet_labels.npy")
OUTPUT_CLASSES      = os.path.join(OUTPUT_FOLDER, "class_names.npy")
OUTPUT_IMAGE_IDS    = os.path.join(OUTPUT_FOLDER, "image_ids.npy")
OUTPUT_IMAGE_COLORS = os.path.join(OUTPUT_FOLDER, "image_colors.npy")

# ------------------------------------------------------------
# IMAGE TRANSFORMS
# ------------------------------------------------------------
transform = transforms.Compose([
    transforms.Resize((224, 224)),
    transforms.ToTensor(),
    transforms.Normalize(
        mean=[0.485, 0.456, 0.406],
        std=[0.229, 0.224, 0.225]
    )
])

# ------------------------------------------------------------
# DATASET
# ------------------------------------------------------------
dataset    = ImageFolder(DATASET_PATH, transform=transform)
dataloader = DataLoader(dataset, batch_size=BATCH_SIZE, shuffle=False)

class_names  = dataset.classes
image_paths  = [path for path, _ in dataset.samples]

print("Classes   :", class_names)
print("Total imgs:", len(image_paths))

image_ids = [os.path.basename(p) for p in image_paths]

# ------------------------------------------------------------
# LOAD RESNET50
# ------------------------------------------------------------
print("Loading pretrained ResNet50 model...")

resnet = models.resnet50(pretrained=True)
resnet = torch.nn.Sequential(*list(resnet.children())[:-1])
resnet = resnet.to(DEVICE)
resnet.eval()

# ------------------------------------------------------------
# COLOR EXTRACTION
# ------------------------------------------------------------
def compute_image_lab(image_path):
    img = cv2.imread(image_path)
    if img is None:
        print("Warning: cannot read", image_path)
        return np.array([50, 128, 128])

    h, w, _ = img.shape
    crop = img[h//4:3*h//4, w//4:3*w//4]
    crop = cv2.resize(crop, (64, 64))
    lab  = cv2.cvtColor(crop, cv2.COLOR_BGR2LAB)

    L = lab[:, :, 0].mean()
    A = lab[:, :, 1].mean()
    B = lab[:, :, 2].mean()
    return np.array([L, A, B])

# ------------------------------------------------------------
# FEATURE + COLOR EXTRACTION
# ------------------------------------------------------------

features     = []
labels       = []
image_colors = []
sample_index = 0

with torch.no_grad():
    for images, targets in tqdm(dataloader, desc="Extracting features"):

        images = images.to(DEVICE)
        output = resnet(images)
        output = output.view(output.size(0), -1)

        output = output / output.norm(dim=1, keepdim=True)

        features.append(output.cpu().numpy())
        labels.append(targets.numpy())

        batch_size = images.size(0)
        for i in range(batch_size):
            img_path  = image_paths[sample_index]
            color_lab = compute_image_lab(img_path)
            image_colors.append(color_lab)
            sample_index += 1

features     = np.vstack(features)
labels       = np.hstack(labels)
image_colors = np.array(image_colors)

print("\nFeature shape :", features.shape)
print("Image IDs     :", len(image_ids))
print("Image colors  :", image_colors.shape)

assert features.shape[0] == len(image_ids),       "Mismatch: features vs image_ids"
assert features.shape[0] == image_colors.shape[0], "Mismatch: features vs image_colors"
assert features.shape[0] == len(labels),          "Mismatch: features vs labels"

# ------------------------------------------------------------
# IMAGE ID VALIDATION
# ------------------------------------------------------------
print("\n" + "="*55)
print("IMAGE ID VALIDATION")
print("="*55)

validation_passed = True

null_ids  = [x for x in image_ids if x is None or str(x).strip() == ""]
print(f"[1] Null/empty image IDs  : {len(null_ids)}")
if null_ids:
    print(f"    Examples: {null_ids[:5]}")
    validation_passed = False

unique_ids = set(image_ids)
dup_count  = len(image_ids) - len(unique_ids)
print(f"[2] Duplicate image IDs   : {dup_count}")
if dup_count > 0:
    seen = {}
    for img_id in image_ids:
        seen[img_id] = seen.get(img_id, 0) + 1
    dups = {k: v for k, v in seen.items() if v > 1}
    for img_id, cnt in list(dups.items())[:5]:
        print(f"    '{img_id}' appears {cnt} times")
    validation_passed = False

all_disk_files = set()
for root, dirs, files in os.walk(DATASET_PATH):
    for f in files:
        ext = os.path.splitext(f)[1].lower()
        if ext in {".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".webp"}:
            all_disk_files.add(f)

missing_on_disk = [x for x in image_ids if str(x) not in all_disk_files]
print(f"[3] IDs missing on disk   : {len(missing_on_disk)}")
if missing_on_disk:
    print(f"    Examples: {missing_on_disk[:5]}")
    validation_passed = False

unassigned_files = all_disk_files - unique_ids
print(f"[4] Files on disk with no ID assigned : {len(unassigned_files)}")
if unassigned_files:
    print(f"    Examples: {sorted(unassigned_files)[:5]}")
    print(f"    (warning only - likely skipped during loading)")

bad_format = [
    x for x in image_ids
    if not re.match(r'.+\.(jpg|jpeg|png|bmp|tif|tiff|webp)$', str(x), re.IGNORECASE)
]
print(f"[5] IDs with bad format   : {len(bad_format)}")
if bad_format:
    print(f"    Examples: {bad_format[:5]}")
    validation_passed = False

print(f"[6] Total images in dataset : {len(dataset.samples)}")
print(f"    image_ids collected     : {len(image_ids)}")
print(f"    Count match             : {len(image_ids) == len(dataset.samples)}")
if len(image_ids) != len(dataset.samples):
    validation_passed = False

print("="*55)
if validation_passed:
    print("All checks passed - safe to save")
else:
    print("Validation FAILED - fix errors above before saving")
print("="*55)

if not validation_passed:
    raise RuntimeError(
        "Image ID validation failed. "
        "Fix the errors above before running dimensional reduction."
    )

# ------------------------------------------------------------
# SAVE
# ------------------------------------------------------------
np.save(OUTPUT_FEATURES,     features)
np.save(OUTPUT_LABELS,       labels)
np.save(OUTPUT_CLASSES,      np.array(class_names))
np.save(OUTPUT_IMAGE_IDS,    np.array(image_ids))
np.save(OUTPUT_IMAGE_COLORS, image_colors)

print("\nSaved:")
print("  resnet_features.npy")
print("  resnet_labels.npy")
print("  class_names.npy")
print("  image_ids.npy")
print("  image_colors.npy")