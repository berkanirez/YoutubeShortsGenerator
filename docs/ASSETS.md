# Assets: rules and workflow

Everything the videos show comes from `assets/`, described by one catalog:
`assets/manifests/assets.json`. The backend reads it at startup, the LLM can only
pick ids from it (they're an enum in its JSON Schema), and the renderer inlines the
SVG files. Adding or replacing art never requires a code change.

## Commands

Run from the repository root:

```
dotnet run --project tools/ShortsGenerator.AssetTool -- validate
dotnet run --project tools/ShortsGenerator.AssetTool -- add --id quads-highlight --file path/to/drawing.svg \
    --category anatomy --tags quads,legs,squat --scene-types AnatomyHighlight [--orientation Square]
dotnet run --project tools/ShortsGenerator.AssetTool -- replace --id dumbbell --file path/to/better.svg
dotnet run --project tools/ShortsGenerator.AssetTool -- index      # -> output/asset-index.html (contact sheet)
```

`add` and `replace` check the SVG first, then the whole catalog; if anything fails,
nothing is changed. Restart the API afterwards (the catalog is loaded at startup) and
re-run `npm run sync-assets` in `renderer/` if you use Remotion Studio.

## Naming rules

| Rule | Example |
|---|---|
| ids: lowercase letters, digits, `-` | `quads-highlight`, `mascot-tired` |
| file = `assets/<category folder>/<id>.svg` or `.png` (the tool does this) | `assets/anatomy/quads-highlight.svg`, `assets/mascot/mascot-idle.png` |
| categories → folders: `mascot`→`mascot/`, `exercise`→`exercises/`, `equipment`/`prop`→`props/`, `anatomy`→`anatomy/`, `icon`→`icons/` | |
| exercise poses come in pairs `<exercise>-top` / `<exercise>-bottom` | `squat-top`, `squat-bottom` |
| anatomy schematics end in `-highlight` | `lats-highlight` |

## PNG art (the channel mascot)

The mascot and any generated/painted art are PNGs (prompts: `docs/MASCOT_PROMPTS.md`).
Drop the raw image into `assets/incoming/` (originals stay there), then:

```
tools/images/.venv/Scripts/python.exe tools/images/prepare_art.py assets/incoming/mascot-idle.png output/art-preview/mascot-idle.png
dotnet run --project tools/ShortsGenerator.AssetTool -- replace --id mascot-idle --file output/art-preview/mascot-idle.png
```

`prepare_art.py` makes the white background (and the white sticker outline, and enclosed
white gaps between limbs) transparent, keeps eye whites, crops tight to the figure (3% margin,
not square - the video fits it bottom-aligned in a tall box, so a standing mascot can fill the
screen height) and scales the longer side to `--size` (catalog art: `--size 1280`). One-time setup: `py -3.11 -m venv tools/images/.venv` then
`tools/images/.venv/Scripts/python.exe -m pip install -r tools/images/requirements.txt`.
**HD first.** Generated images are ~1250 px, and the video shows a mascot up to ~1450 px
tall - upscaled by the browser they look soft. Upscale the original 4x with Real-ESRGAN's
anime model (free, BSD-3, runs on the GPU via Vulkan, ~7 s per image on the RTX 3050), then
prepare at 2048:

```
tools/images/realesrgan/realesrgan-ncnn-vulkan.exe -i assets/incoming/x.png -o tools/images/.cache/hd/x.png -n realesrgan-x4plus-anime
tools/images/.venv/Scripts/python.exe tools/images/prepare_art.py --size 2048 tools/images/.cache/hd/x.png output/art-preview/x.png
```

The binary is not in the repo: download `realesrgan-ncnn-vulkan-20220424-windows.zip` from
the Real-ESRGAN GitHub release v0.2.5.0 and unzip it into `tools/images/realesrgan/`.

Two more modes:

- `--pair <pair.png> <x-top.png> <x-bottom.png>` - a wide image with both poses side by
  side (left = top, right = bottom). It is cut at the empty column nearest the middle, the
  halves are shifted sideways so what touches the floor (feet, hands, bar stands) lines up,
  and both are cropped with the same box and the same scale - so the rep animation doesn't
  make the figure jump.
- `--keep-holes` - icons with white parts (calendar cells, plates): only the outer
  background is removed, enclosed white stays.

PNG rules: a real PNG, 256-4096 px per side, under 4 MB, transparent background.
The renderer shows PNGs with Remotion's `<Img>` (a frame waits until the image is loaded).

## Drawing rules (SVG)

- **SVG with a `viewBox`**, no fixed size, under 200 kB. 200x200 is the house size.
- **No scripts, event handlers (`onload=` ...), `javascript:`, `<foreignObject>`,
  `<iframe>`, or external URLs.** The renderer inlines the markup into its page, so an
  SVG is effectively code; renders must also work offline.
- **Pose pairs are registered**: the floor/bar/equipment sits at exactly the same
  coordinates in both poses and the feet/hands that don't move stay put. ExerciseDemo
  alternates the two drawings to animate a rep - anything that differs will jump.
  (Day 18 found the pull-up bar 10 units apart between the poses; fixed on Day 20.)
- **Original art only.** Simple shapes in the house palette: mascot teal `#2DD4BF`,
  accent orange `#FB923C`, neutral greys `#94A3B8` / `#64748B`, light `#E2E8F0`. Since
  the mascot became the channel owner's own character (PNG, red hair, red shorts, grey
  sleeves), the remaining SVG drawings are placeholders until the PNG set replaces them.
- Anatomy art is schematic, not medical illustration.

## Metadata rules (what makes the LLM pick an asset well)

- **tags** are how assets get chosen: the LLM sees them, and `AssetMatcher` swaps a
  chosen asset for a better one when the chosen one's tags don't appear in the scene's
  text but another's do. Use the words a narration would say: `squat`, `quads`, `legs`,
  not `img_03`. Lowercase, single words or kebab-case.
- **The first tag is the subject** (`pullup`, `squat`, `lats`): an ExerciseDemo in a
  push-up video shows the asset whose first tag the topic names, unless the scene names
  another exercise (Day 29). `pullup` also matches "pull-ups" in a narration.
- **Don't tag poses or categories** (`top`, `bottom`, `standing`, `muscle`, ...): they
  are ignored for matching, because "...at the top" or "chest muscles" made a squat
  drawing fit a pull-up scene and biceps fit a chest scene (Day 29).
- **compatibleSceneTypes**: the scene types the drawing makes sense in. `AssetMatcher`
  only considers compatible assets.
- The renderer requires `mascot-idle`, `mascot-thinking`, `mascot-happy` and
  `mascot-tired` (fallbacks when a scene has no asset); `validate` refuses a catalog
  without them.
