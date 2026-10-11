"""Removes background left inside a prepared picture: white gaps between the legs, between an
arm and the body, the hole in a kettlebell's handle - places prepare_art.py kept because they
were enclosed and smaller than its hole threshold.

    tools/images/.venv/Scripts/python.exe tools/images/clean_holes.py [--apply] <picture.png> ...

Without --apply it only reports. What counts as left-over background (measured on the art):
- the region is near-white and NEUTRAL (R = G = B, ~254): the generator's background. Drawn
  whites - a plate, paper, foam, a mug - are slightly warm (blue a few points lower), and stay;
- in a character picture it is also big enough (>= 0.075% of the figure's box), or at least
  0.02% and away from the red hair - so eye whites, which sit next to the hair, stay.
The picture keeps its size; the removed region's light fringe is faded like prepare_art.py does.
"""
import sys

import numpy as np
from PIL import Image, ImageFilter
from scipy import ndimage

NEAR_WHITE = 228
NEUTRAL = 2.5          # max |R-B| / |R-G| of a region's mean colour
BRIGHT = 252.0         # min mean channel value (icons: drawn whites are close to this)
BRIGHT_CHARACTER = 244.0  # characters: upscaling greys the gaps a little; no drawn neutral whites but eyes
BIG = 0.00075          # share of the figure's box: always background when neutral
SMALL = 0.0002         # share of the figure's box: background when also away from the hair
HAIR_DISTANCE = 0.09   # share of the figure's height


def holes(rgba: np.ndarray, character: bool) -> tuple[np.ndarray, list[str]]:
    rgb = rgba[:, :, :3].astype(np.int16)
    opaque = rgba[:, :, 3] > 200
    white = opaque & (rgb.min(axis=2) >= NEAR_WHITE)
    labels, count = ndimage.label(white)
    if count == 0:
        return np.zeros_like(white), []
    ys, xs = np.nonzero(opaque)
    box_area = max(1, (xs.max() - xs.min()) * (ys.max() - ys.min()))
    height = ys.max() - ys.min()
    hair = opaque & (rgb[:, :, 0] > 150) & (rgb[:, :, 1] < 70) & (rgb[:, :, 2] < 70)
    hair_distance = ndimage.distance_transform_edt(~hair) if character and hair.any() else None

    remove = np.zeros_like(white)
    notes = []
    sizes = np.bincount(labels.ravel())
    for label in range(1, count + 1):
        size = sizes[label]
        if size < 600:
            continue
        region = labels == label
        mean = rgb[region].mean(axis=0)
        neutral = abs(mean[0] - mean[2]) <= NEUTRAL and abs(mean[0] - mean[1]) <= NEUTRAL and mean.min() >= (BRIGHT_CHARACTER if character else BRIGHT)
        share = size / box_area
        far = True
        if hair_distance is not None:
            far = hair_distance[region].min() > HAIR_DISTANCE * height
        if not character:
            decision = neutral
        else:
            decision = neutral and (share >= BIG or (share >= SMALL and far))
        cy, cx = (int(v.mean()) for v in np.nonzero(region))
        notes.append(f"  {'REMOVE' if decision else 'keep  '} {size:7d}px {share * 100:6.3f}% neutral={neutral} far={far} at ({cx},{cy})")
        if decision:
            remove |= region
    return remove, notes


def clean(path: str, character: bool, apply: bool) -> bool:
    image = Image.open(path).convert("RGBA")
    rgba = np.asarray(image).copy()
    remove, notes = holes(rgba, character)
    if not notes:
        return False
    print(path)
    print("\n".join(notes))
    if not apply or not remove.any():
        return bool(remove.any())
    alpha = rgba[:, :, 3].copy()
    alpha[remove] = 0
    # Fade the light anti-aliasing pixels left around the hole.
    near = np.asarray(Image.fromarray(np.where(remove, 0, 255).astype(np.uint8), "L").filter(ImageFilter.MinFilter(5))) == 0
    brightness = rgba[:, :, :3].astype(np.int16).mean(axis=2)
    halo = near & ~remove & (alpha > 0) & (brightness > 200)
    alpha[halo] = np.minimum(alpha[halo], np.clip((255 - brightness[halo]) * 4, 0, 255)).astype(np.uint8)
    rgba[:, :, 3] = alpha
    Image.fromarray(rgba, "RGBA").save(path, optimize=True)
    return True


def main() -> int:
    args = sys.argv[1:]
    apply = "--apply" in args
    paths = [a for a in args if not a.startswith("--")]
    changed = 0
    for path in paths:
        normalized = path.replace("\\", "/")
        character = not any(f"/{folder}/" in normalized for folder in ("icons", "props"))
        changed += clean(path, character, apply)
    print(f"{changed} picture(s) {'cleaned' if apply else 'would change'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
