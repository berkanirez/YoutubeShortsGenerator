# Mascot image prompts

Generate each image with your image tool, save it under the given file name into
`assets/incoming/`, and tell Claude Code - it will clean the background, crop, align pose
pairs, register everything in the asset catalog and render a test video.

## How to use

1. Paste the **CHARACTER** block and the **RULES** block at the start of every prompt
   (same text every time - this is what keeps the character consistent).
2. Then add the line for the image you want.
3. Generate several, keep the one that matches the character best.

## CHARACTER (paste first, every time)

```
Character: an original athletic male fitness mascot in a clean anime / manga cel-shaded
style with bold black outlines. Spiky messy RED hair covering the forehead, simple white
eyes with no pupils, no mouth unless the expression needs one. Skin: SKIN_TONE.
Muscular but natural build, defined abs. Wears ONLY loose bright red athletic shorts,
a grey compression sleeve on his LEFT forearm and a grey compression sleeve on his
RIGHT knee. Barefoot.
```

Replace `SKIN_TONE` with one choice and keep it identical in every prompt:
* `warm natural tan skin` (recommended - keeps the mascot clearly original)
* `golden yellow skin` (closer to existing "yellow" faceless fitness channels - see the note in the chat)

## RULES (paste second, every time)

```
Rules: full character visible, centered, nothing cut off. Pure flat white background
(#FFFFFF), no gradient, no sun rays, no floor, no shadow on the ground, no props unless
asked. NO text, no letters, no numbers, no arrows, no icons, no check marks or crosses.
Square image 1024x1024. Same art style, line weight and colors as a single character
sheet.
```

## A. Expressions (needed first - 4 required)

| File | Add this line |
|---|---|
| `mascot-idle.png` | `Pose: standing relaxed, facing the viewer, arms at the sides, calm confident look.` |
| `mascot-thinking.png` | `Pose: standing, one hand on his chin, the other arm crossed, eyes looking up thoughtfully.` |
| `mascot-tired.png` | `Pose: sitting on the ground, knees up, both hands in his hair, sweating, exhausted.` |
| `mascot-happy.png` | `Pose: standing, big smile, one fist raised in celebration.` |

Optional extras:

| File | Add this line |
|---|---|
| `mascot-flex.png` | `Pose: standing, flexing his right biceps, left hand on his hip, proud.` |
| `mascot-stop.png` | `Pose: standing, right arm stretched toward the viewer, palm open in a "stop" gesture, serious.` |
| `mascot-thumbsup.png` | `Pose: upper body, giving a thumbs up toward the viewer, slight smile.` |
| `mascot-pointing.png` | `Pose: upper body, index finger raised as if saying "listen", serious look.` |
| `mascot-shocked.png` | `Pose: standing, leaning back, eyes wide, hands up in surprise.` |
| `mascot-sleeping.png` | `Pose: lying on his side on a simple pillow, asleep, peaceful.` |

## B. Exercise poses (pairs: the two images of a pair must use the same camera)

For a pair, generate **both positions in ONE image, side by side** so the scale and camera
match; Claude Code cuts it in two and aligns the halves:

```
Make a 2048x1024 image with the SAME character twice, same size and same camera angle:
LEFT half = <position 1>, RIGHT half = <position 2>.
```

| Files (one side-by-side image: `pullup-pair.png` etc.) | Left half | Right half | Camera |
|---|---|---|---|
| `pullup-pair.png` → `pullup-top`, `pullup-bottom` | chin above a horizontal pull-up bar, elbows bent | hanging from the same bar with straight arms | front view, bar at the same height in both |
| `pushup-pair.png` → `pushup-top`, `pushup-bottom` | push-up top position, arms straight, body in one straight line | bottom position, chest just above the floor | side view |
| `squat-pair.png` → `squat-top`, `squat-bottom` | standing tall, feet shoulder-width | deep squat, thighs parallel to the floor, back straight | side view |
| `dip-pair.png` → `dip-top`, `dip-bottom` | on parallel dip bars, arms straight | lowered, elbows at 90 degrees | side view |
| `deadlift-pair.png` → `deadlift-top`, `deadlift-bottom` | standing tall holding a barbell at the hips | hinged at the hips, flat back, barbell at mid-shin | side view |
| `curl-pair.png` → `curl-top`, `curl-bottom` | dumbbell curled up to the shoulder | arm straight down holding the dumbbell | front view |

Single poses:

| File | Add this line |
|---|---|
| `plank.png` | `Pose: forearm plank, body in one straight line, side view.` |
| `lunge.png` | `Pose: forward lunge, both knees at 90 degrees, upright torso, side view.` |
| `running.png` | `Pose: running mid-stride, side view, dynamic.` |

## C. Muscle highlights (anatomy scenes)

```
Pose: standing relaxed, facing the viewer (or back view where noted). The <MUSCLE> is
highlighted in solid bright red (#EF4444); everything else in his normal colors.
```

| File | `<MUSCLE>` | View |
|---|---|---|
| `chest-highlight.png` | chest muscles (pectorals) | front |
| `biceps-highlight.png` | biceps on both arms | front |
| `triceps-highlight.png` | triceps on both arms | back |
| `shoulders-highlight.png` | shoulder muscles (deltoids) | front |
| `core-highlight.png` | abdominal muscles | front |
| `lats-highlight.png` | lats (large back muscles under the arms) | back |
| `quads-highlight.png` | front thigh muscles | front |
| `hamstrings-highlight.png` | back thigh muscles | back |
| `glutes-highlight.png` | glutes | back |
| `calves-highlight.png` | calf muscles | back |

## D. Icons (no character)

```
Simple flat icon in the same cel-shaded style with bold black outlines, centered, pure white
background, no text. Square 1024x1024. Object: <OBJECT>.
```

| File | `<OBJECT>` |
|---|---|
| `dumbbell.png` | a single dumbbell |
| `pullup-bar.png` | a doorway pull-up bar |
| `sleep.png` | a crescent moon with small "z" shapes drawn as curls (no letters) |
| `meal.png` | a healthy plate: chicken, rice, vegetables |
| `calendar.png` | a wall calendar with check marks on some days |
| `stopwatch.png` | a stopwatch |
| `water.png` | a water bottle |

## Order

Start with **A (the 4 required expressions)**. Claude Code wires them in and renders a test
video so you can judge the look before making the rest.
