"""Turns a generated character/icon image on a white background into a catalog-ready PNG.

    tools/images/.venv/Scripts/python.exe tools/images/prepare_art.py <input> <output.png> [--size 1024]
    tools/images/.venv/Scripts/python.exe tools/images/prepare_art.py --keep-holes <icon.png> <output.png>
    tools/images/.venv/Scripts/python.exe tools/images/prepare_art.py --pair <pair.png> <left.png> <right.png>
    tools/images/.venv/Scripts/python.exe tools/images/prepare_art.py --frames <in1.png> ... <inN.png> <out1.png> ... <outN.png>

Steps (deterministic, local, no network):
1. Background: every near-white region that touches the image border becomes transparent -
   the background plus the white "sticker" outline image generators draw around a figure.
2. Holes: enclosed near-white regions bigger than a small blob (the gap between a bent arm
   and the body, between the legs) are background too; small ones (eye whites, highlights)
   stay.
3. Fringe: light anti-aliasing pixels left on the edge are faded out.
4. Crop tight to the figure (small margin) and scale its longer side to --size.
The video draws its own background, so the art must not carry one.

--pair: one wide image with the same figure twice (e.g. pull-up top | bottom). It is cut at
the empty column nearest the middle, and both halves are cropped with the SAME box and
scaled by the SAME factor - so when a scene swaps top/bottom, the bar and the feet stay put
instead of the figure jumping as each half is re-centered on its own.
"""
import argparse
import sys

import numpy as np
from PIL import Image, ImageFilter
from scipy import ndimage

NEAR_WHITE = 232          # all of R, G, B at or above this counts as "white"
HOLE_MIN_FRACTION = 0.0012  # enclosed white regions larger than this share of the image are background
MARGIN = 0.03
MIN_SIDE = 300          # the catalog requires >= 256 px per side


def cutout(source: Image.Image, keep_holes: bool = False) -> Image.Image:
    """The source with its white background made transparent, uncropped."""
    rgba = source.convert("RGBA")
    pixels = np.asarray(rgba).astype(np.int16)
    white = (pixels[:, :, 0] >= NEAR_WHITE) & (pixels[:, :, 1] >= NEAR_WHITE) & (pixels[:, :, 2] >= NEAR_WHITE)

    # Every connected near-white region at once (one pass); a region is background when it
    # touches the border or is a large enclosed hole.
    regions, count = ndimage.label(white)
    height, width = white.shape
    border = np.unique(np.concatenate([regions[0], regions[-1], regions[:, 0], regions[:, -1]]))
    sizes = np.bincount(regions.ravel(), minlength=count + 1)
    is_background = sizes >= int(width * height * HOLE_MIN_FRACTION)
    if keep_holes:
        is_background[:] = False
    is_background[border] = True
    is_background[0] = False          # label 0 = not white at all
    background = is_background[regions]
    alpha = np.where(background, 0, 255).astype(np.uint8)

    # Fade light pixels that sit right next to the removed background (anti-aliasing halo).
    near_edge = np.asarray(Image.fromarray(alpha, "L").filter(ImageFilter.MinFilter(5))) == 0
    brightness = pixels[:, :, :3].mean(axis=2)
    halo = near_edge & (alpha == 255) & (brightness > 200)
    alpha[halo] = np.clip((255 - brightness[halo]) * 4, 0, 255).astype(np.uint8)

    result = np.asarray(rgba).copy()
    result[:, :, 3] = alpha
    return Image.fromarray(result, "RGBA")


def fit(figure: Image.Image, box: tuple[int, int, int, int], size: int) -> Image.Image:
    """Crops box out of figure with a small margin and scales its longer side to size.

    Tight, not square: the video fits art into a tall box, and a square canvas left a
    standing mascot only ~45% of the frame width - it could never grow past the width.
    Very flat art (a pull-up bar) is padded to MIN_SIDE so the catalog accepts it.
    """
    figure = figure.crop(box)
    pad = int(max(figure.width, figure.height) * MARGIN)
    canvas = Image.new("RGBA", (figure.width + 2 * pad, figure.height + 2 * pad), (0, 0, 0, 0))
    canvas.paste(figure, (pad, pad), figure)
    scale = size / max(canvas.width, canvas.height)
    canvas = canvas.resize((max(1, round(canvas.width * scale)), max(1, round(canvas.height * scale))), Image.LANCZOS)
    if min(canvas.size) < MIN_SIDE:
        padded = Image.new("RGBA", (max(canvas.width, MIN_SIDE), max(canvas.height, MIN_SIDE)), (0, 0, 0, 0))
        padded.paste(canvas, ((padded.width - canvas.width) // 2, (padded.height - canvas.height) // 2), canvas)
        canvas = padded
    return canvas


def bbox(figure: Image.Image) -> tuple[int, int, int, int]:
    box = figure.getbbox()
    if box is None:
        raise ValueError("nothing left after removing the background")
    return box


def prepare(source: Image.Image, size: int, keep_holes: bool = False) -> Image.Image:
    figure = cutout(source, keep_holes)
    return fit(figure, bbox(figure), size)


def align_feet(halves: list[Image.Image]) -> list[Image.Image]:
    """Shifts the halves sideways so what touches the floor (feet, hands, bar stands) lines up.

    Generators keep the height consistent but drift sideways; without this the squat's feet
    jump when the scene swaps standing for squatting.
    """
    anchors = []
    for half in halves:
        alpha = np.asarray(half)[:, :, 3]
        left, top, right, bottom = bbox(half)
        band = alpha[bottom - max(1, (bottom - top) // 16):bottom]
        columns = np.flatnonzero(band.max(axis=0) > 0)
        anchors.append(int(columns.mean()))
    common = max(anchors)
    offsets = [common - anchor for anchor in anchors]
    width = max(half.width + offset for half, offset in zip(halves, offsets))
    shifted = []
    for half, offset in zip(halves, offsets):
        canvas = Image.new("RGBA", (width, half.height), (0, 0, 0, 0))
        canvas.paste(half, (offset, 0))
        shifted.append(canvas)
    return shifted


def prepare_pair(source: Image.Image, size: int) -> tuple[Image.Image, Image.Image]:
    figure = cutout(source)
    empty = np.flatnonzero(np.asarray(figure)[:, :, 3].max(axis=0) == 0)
    middle = figure.width / 2
    central = empty[np.abs(empty - middle) < figure.width * 0.2]
    if central.size == 0:
        raise ValueError("no empty column near the middle - the two figures touch or overlap")
    split = int(central[np.argmin(np.abs(central - middle))])
    halves = align_feet([figure.crop((0, 0, split, figure.height)), figure.crop((split, 0, figure.width, figure.height))])
    boxes = [bbox(half) for half in halves]
    shared = (min(b[0] for b in boxes), min(b[1] for b in boxes), max(b[2] for b in boxes), max(b[3] for b in boxes))
    left, right = (fit(half, shared, size) for half in halves)
    return left, right


def prepare_frames(sources: list[Image.Image], size: int, hanging: bool = False) -> list[Image.Image]:
    """Animation frames cut apart from a sprite sheet: each figure is cropped, then all are
    placed on one canvas - standing on one floor (or hanging from one bar) and lined up on what
    touches it - and scaled alike, so the flipbook moves the body, not the whole picture.
    (Cells from different rows of a sheet don't share coordinates, so this can't reuse the
    pair's shared crop box.)"""
    crops = []
    for source in sources:
        figure = cutout(source)
        crops.append(figure.crop(bbox(figure)))

    def anchor(image: Image.Image) -> tuple[int, int]:
        """(x, y) of what touches the floor or bar. Hanging: the bar is the widest row in the
        upper half - not the top edge, which is the head when the chin is over the bar."""
        alpha = np.asarray(image)[:, :, 3] > 0
        if hanging:
            widths = alpha[: max(1, image.height // 2)].sum(axis=1)
            y = int(np.argmax(widths))
            columns = np.flatnonzero(alpha[y])
        else:
            y = image.height
            columns = np.flatnonzero(alpha[-max(1, image.height // 16):].any(axis=0))
        return (int(columns.mean()) if columns.size else image.width // 2), y

    anchors = [anchor(c) for c in crops]
    left = max(x for x, _ in anchors)
    right = max(c.width - x for c, (x, _) in zip(crops, anchors))
    above = max(y for _, y in anchors)
    height = max(above - y + c.height for c, (_, y) in zip(crops, anchors))
    frames = []
    for crop, (x, y) in zip(crops, anchors):
        canvas = Image.new("RGBA", (left + right, height), (0, 0, 0, 0))
        canvas.paste(crop, (left - x, above - y), crop)
        frames.append(fit(canvas, (0, 0, canvas.width, canvas.height), size))
    return frames


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--pair", action="store_true", help="split a side-by-side pair into two aligned images")
    parser.add_argument("--frames", action="store_true", help="align N cut-apart animation frames: N inputs then N outputs")
    parser.add_argument("--hanging", action="store_true", help="with --frames: the figure hangs from a bar (align the top)")
    parser.add_argument("--keep-holes", action="store_true",
                        help="only remove the outer background (icons with white parts: calendar cells, plates)")
    parser.add_argument("input")
    parser.add_argument("outputs", nargs="+", metavar="output")
    parser.add_argument("--size", type=int, default=1024)
    args = parser.parse_args()
    if args.frames:
        files = [args.input, *args.outputs]
        if len(files) % 2 or len(files) < 4:
            print("--frames needs N inputs followed by N outputs", file=sys.stderr)
            return 2
        half = len(files) // 2
        for image, output in zip(prepare_frames([Image.open(f) for f in files[:half]], args.size, args.hanging), files[half:]):
            image.save(output, optimize=True)
        return 0
    if len(args.outputs) != (2 if args.pair else 1) or not all(o.lower().endswith(".png") for o in args.outputs):
        print("expected one output .png (two with --pair)", file=sys.stderr)
        return 2
    source = Image.open(args.input)
    images = prepare_pair(source, args.size) if args.pair else [prepare(source, args.size, args.keep_holes)]
    for image, output in zip(images, args.outputs):
        image.save(output, optimize=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
