"""Cuts a sheet of several pictures (one image generated with a grid of panels) into one image
per panel, each still on its white background - ready for prepare_art.py like any single image.

    tools/images/.venv/Scripts/python.exe tools/images/split_sheet.py <sheet.png> <rows> <cols> <out1.png> [<out2.png> ...]
    tools/images/.venv/Scripts/python.exe tools/images/split_sheet.py --pairs <sheet.png> <rows> <out1.png> ...

--pairs: each row is one exercise pair (two figures side by side). The sheet is cut into its
two columns first and each column into rows on its own - a tall pose in one column may reach
lower than the next row starts in the other - and each row's two cells are put back side by
side, ready for prepare_art.py --pair.

Outputs are in reading order (left to right, top to bottom); fewer outputs than cells means
the last cells are ignored. The cuts go through the emptiest white gap near each expected
boundary (not at exactly 1/cols of the width): image generators never space panels perfectly,
and a straight cut would slice an arm that reaches into the gap. Deterministic.
"""
import sys

import numpy as np
from PIL import Image
from scipy import ndimage

NEAR_WHITE = 232
PAD = 24  # white margin kept around each cut panel


def cuts(content: np.ndarray, parts: int) -> list[int]:
    """Boundaries splitting a 1-D content profile into `parts`: for each expected boundary, the
    middle of the longest empty run within the middle 80% of the space between its neighbours."""
    n = len(content)
    empty = content <= max(1, int(content.max() * 0.002))
    bounds = [0]
    for k in range(1, parts):
        expected = k * n / parts
        window = n / parts * 0.4
        lo, hi = int(max(0, expected - window)), int(min(n, expected + window))
        best, best_len, run_start = int(expected), -1, None
        for i in range(lo, hi + 1):
            if i < hi and empty[i]:
                if run_start is None:
                    run_start = i
            elif run_start is not None:
                if i - run_start > best_len:
                    best_len, best = i - run_start, (run_start + i) // 2
                run_start = None
        bounds.append(best)
    bounds.append(n)
    return bounds


def ink_mask(image: Image.Image) -> np.ndarray:
    rgb = np.asarray(image.convert("RGB")).astype(np.int16)
    return ~((rgb[:, :, 0] >= NEAR_WHITE) & (rgb[:, :, 1] >= NEAR_WHITE) & (rgb[:, :, 2] >= NEAR_WHITE))


def clean(cell: Image.Image) -> Image.Image:
    """Whites out small pieces of a neighbouring picture: ink touching the cell's edge that is
    a small share of the cell's ink (a strand of hair, the top of the next icon)."""
    ink = ink_mask(cell)
    regions, count = ndimage.label(ndimage.binary_dilation(ink, iterations=2))
    if count < 2:
        return cell
    sizes = np.bincount(regions[ink], minlength=count + 1)
    edge = np.unique(np.concatenate([regions[0], regions[-1], regions[:, 0], regions[:, -1]]))
    pixels = np.asarray(cell.convert("RGB")).copy()
    for label in edge:
        if label != 0 and sizes[label] < 0.15 * sizes.sum():
            pixels[regions == label] = 255
    return Image.fromarray(pixels, "RGB")


def frame(cell: Image.Image) -> Image.Image:
    framed = Image.new("RGB", (cell.width + 2 * PAD, cell.height + 2 * PAD), (255, 255, 255))
    framed.paste(cell, (PAD, PAD))
    return framed


def split_pairs(sheet: Image.Image, rows: int) -> list[Image.Image]:
    rgb = sheet.convert("RGB")
    ink = ink_mask(rgb)
    col_bounds = cuts(ink.sum(axis=0), 2)
    columns = []  # per column: (top y on the sheet, cell) for each row
    for c in range(2):
        band = ink[:, col_bounds[c]:col_bounds[c + 1]]
        row_bounds = cuts(band.sum(axis=1), rows)
        columns.append([
            (row_bounds[r], clean(rgb.crop((col_bounds[c], row_bounds[r], col_bounds[c + 1], row_bounds[r + 1]))))
            for r in range(rows)
        ])
    pairs = []
    for r in range(rows):
        (ly, left), (ry, right) = columns[0][r], columns[1][r]
        gap = 3 * PAD
        top = min(ly, ry)
        height = max(ly + left.height, ry + right.height) - top
        joined = Image.new("RGB", (left.width + right.width + gap, height), (255, 255, 255))
        # At their heights on the sheet, as drawn: the bar / the floor stays where it was.
        joined.paste(left, (0, ly - top))
        joined.paste(right, (left.width + gap, ry - top))
        pairs.append(frame(joined))
    return pairs


def split(sheet: Image.Image, rows: int, cols: int) -> list[Image.Image]:
    rgb = np.asarray(sheet.convert("RGB")).astype(np.int16)
    ink = ~((rgb[:, :, 0] >= NEAR_WHITE) & (rgb[:, :, 1] >= NEAR_WHITE) & (rgb[:, :, 2] >= NEAR_WHITE))
    panels = []
    row_bounds = cuts(ink.sum(axis=1), rows)
    for r in range(rows):
        band = ink[row_bounds[r]:row_bounds[r + 1]]
        col_bounds = cuts(band.sum(axis=0), cols)
        for c in range(cols):
            box = (col_bounds[c], row_bounds[r], col_bounds[c + 1], row_bounds[r + 1])
            panels.append(frame(clean(sheet.convert("RGB").crop(box))))
    return panels


def main() -> int:
    args = sys.argv[1:]
    if args[:1] == ["--pairs"] and len(args) >= 4:
        sheet, rows, outputs = args[1], int(args[2]), args[3:]
        panels = split_pairs(Image.open(sheet), rows)
    elif len(args) >= 4:
        sheet, rows, cols, outputs = args[0], int(args[1]), int(args[2]), args[3:]
        panels = split(Image.open(sheet), rows, cols)
    else:
        print(__doc__, file=sys.stderr)
        return 2
    if len(outputs) > len(panels):
        print(f"{len(outputs)} outputs but the sheet has only {len(panels)} panels", file=sys.stderr)
        return 2
    for panel, output in zip(panels, outputs):
        rgb = np.asarray(panel).astype(np.int16)
        if ((rgb < NEAR_WHITE).any(axis=2)).sum() < 50:
            print(f"panel for {output} is empty - the sheet has fewer pictures than expected", file=sys.stderr)
            return 1
        panel.save(output)
    return 0


if __name__ == "__main__":
    sys.exit(main())
