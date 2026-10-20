# Training data

```
ml/data/
  train/images/   isoline PNGs
  train/masks/    class-index masks, one per image
  val/images/
  val/masks/
```

A mask is a grayscale PNG of the same size as its image. Each pixel holds its class: 0 is background, 1 to 7 are the legend colors. `IsolineUNet` has 8 classes.

Training writes `models/isoline_unet.pt`. `python -m src.training.export_onnx --model models/isoline_unet.pt` writes `models/isoline_unet.onnx`. Neither file goes into git; `ml/models/MANIFEST.json` lists the files a service may load.
