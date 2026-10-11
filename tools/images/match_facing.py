"""Mirrors a picture when it faces the other way than a reference picture.

    tools/images/.venv/Scripts/python.exe tools/images/match_facing.py <picture.png> <reference.png>

The wrong-vs-right split shows a wrong-form drawing above the right-form one; if one push-up
faces left and the other right, the eye compares mirror images instead of the mistake. The
silhouettes decide: both figures are cropped to their bounds and scaled to the same small grid,
and the picture is mirrored when its mirror image overlaps the reference clearly better. Front
views overlap about equally both ways and are left alone. (Reading the head position from the
red hair failed: the red shorts sit in the middle of every figure.) Writes in place when flipped.
"""
import sys

import numpy as np
from PIL import Image, ImageOps

GRID = 96
MARGIN = 0.04  # the mirror must overlap this much better (IoU) to flip


def mask(image: Image.Image) -> np.ndarray:
    alpha = image.convert("RGBA").getchannel("A")
    box = alpha.getbbox()
    return np.asarray(alpha.crop(box).resize((GRID, GRID), Image.BILINEAR)) > 100


def iou(a: np.ndarray, b: np.ndarray) -> float:
    return float((a & b).sum()) / max(1, float((a | b).sum()))


def main() -> int:
    picture, reference = sys.argv[1], sys.argv[2]
    image = Image.open(picture)
    ref = mask(Image.open(reference))
    same = iou(mask(image), ref)
    mirrored = iou(mask(ImageOps.mirror(image)), ref)
    if mirrored > same + MARGIN:
        ImageOps.mirror(image).save(picture, optimize=True)
        print(f"{picture}: mirrored (overlap {same:.2f} -> {mirrored:.2f})")
    else:
        print(f"{picture}: kept (overlap {same:.2f}, mirrored {mirrored:.2f})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
