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
| file = `assets/<category folder>/<id>.svg` (the tool does this) | `assets/anatomy/quads-highlight.svg` |
| categories → folders: `mascot`→`mascot/`, `exercise`→`exercises/`, `equipment`/`prop`→`props/`, `anatomy`→`anatomy/`, `icon`→`icons/` | |
| exercise poses come in pairs `<exercise>-top` / `<exercise>-bottom` | `squat-top`, `squat-bottom` |
| anatomy schematics end in `-highlight` | `lats-highlight` |

## Drawing rules

- **SVG with a `viewBox`**, no fixed size, under 200 kB. 200x200 is the house size.
- **No scripts, event handlers (`onload=` ...), `javascript:`, `<foreignObject>`,
  `<iframe>`, or external URLs.** The renderer inlines the markup into its page, so an
  SVG is effectively code; renders must also work offline.
- **Pose pairs are registered**: the floor/bar/equipment sits at exactly the same
  coordinates in both poses and the feet/hands that don't move stay put. ExerciseDemo
  alternates the two drawings to animate a rep - anything that differs will jump.
  (Day 18 found the pull-up bar 10 units apart between the poses; fixed on Day 20.)
- **Original art only.** Simple shapes in the house palette: mascot teal `#2DD4BF`,
  accent orange `#FB923C`, neutral greys `#94A3B8` / `#64748B`, light `#E2E8F0`. The
  mascot "Spark" must never be yellow or resemble another channel's character.
- Anatomy art is schematic, not medical illustration.

## Metadata rules (what makes the LLM pick an asset well)

- **tags** are how assets get chosen: the LLM sees them, and `AssetMatcher` swaps a
  chosen asset for a better one when the chosen one's tags don't appear in the scene's
  text but another's do. Use the words a narration would say: `squat`, `quads`, `legs`,
  not `img_03`. Lowercase, single words or kebab-case.
- **compatibleSceneTypes**: the scene types the drawing makes sense in. `AssetMatcher`
  only considers compatible assets.
- The renderer requires `mascot-idle`, `mascot-thinking`, `mascot-happy` and
  `mascot-tired` (fallbacks when a scene has no asset); `validate` refuses a catalog
  without them.
