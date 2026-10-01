# OpenRebar ML — Training Data Layout
#
# Place training data in:
#   data/train/images/  — input isoline PNGs from LIRA-SAPR
#   data/train/masks/   — corresponding class-index masks (0-7)
#   data/val/images/    — validation images
#   data/val/masks/     — validation masks
#
# Mask format: grayscale PNG where pixel value = class index
#   0 = background (no reinforcement)
#   1-7 = reinforcement zones by legend color
#
# Trained model checkpoint: models/isoline_unet.pt
# ONNX export: models/isoline_unet.onnx
