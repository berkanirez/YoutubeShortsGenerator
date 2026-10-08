import React from "react";
import {
  AbsoluteFill,
  Easing,
  interpolate,
  Sequence,
  useCurrentFrame,
} from "remotion";
import { buildCaptionCues, type CaptionCue } from "./captionCues";
import { SHORTS_SAFE_AREA, type CaptionStyle } from "./captionStyles";

// A caption pops in over a few frames - fast enough not to delay reading,
// enough motion to signal "new line" when the text changes.
const CAPTION_ENTRANCE_FRAMES = 5;

// A solid outline keeps white text readable over any scene background or
// asset. The stroke is centered on the glyph edge, so it's drawn at double
// width *under* the fill (paint-order) - only the outer half shows and the
// letters keep their shape. (An 8-direction text-shadow outline was tried
// first and left visible notches at letter corners.)
function outlineStyle(width: number, color: string): React.CSSProperties {
  if (width <= 0) {
    return {};
  }
  return {
    WebkitTextStroke: `${width * 2}px ${color}`,
    paintOrder: "stroke fill",
  };
}

const CaptionChunk: React.FC<{ cue: CaptionCue; captionStyle: CaptionStyle }> = ({
  cue,
  captionStyle,
}) => {
  // Scene-local *and* cue-local: <Sequence> restarts the frame count at 0.
  const frame = useCurrentFrame();
  const entrance = interpolate(frame, [0, CAPTION_ENTRANCE_FRAMES], [0, 1], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
    easing: Easing.out(Easing.cubic),
  });
  // Emphasized words land slightly bigger, then settle back to normal size -
  // the "key phrase emphasis" CLAUDE.md section 12 asks for, without
  // karaoke-level timing. The lasting emphasis is the color; a lasting scale
  // was tried first, but transform doesn't grow the word's layout box, so a
  // permanently scaled word ate the space next to it ("core tight" read as
  // "coretight").
  const emphasisScale = interpolate(frame, [0, CAPTION_ENTRANCE_FRAMES * 2], [1.15, 1], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
    easing: Easing.out(Easing.cubic),
  });

  return (
    <div
      style={{
        display: "flex",
        flexWrap: "wrap",
        justifyContent: "center",
        columnGap: "0.28em",
        maxWidth: "100%",
        padding: captionStyle.boxColor ? "18px 34px" : 0,
        borderRadius: 24,
        backgroundColor: captionStyle.boxColor ?? undefined,
        fontFamily: captionStyle.fontFamily,
        fontSize: captionStyle.fontSize,
        fontWeight: captionStyle.fontWeight,
        textTransform: captionStyle.textTransform,
        lineHeight: 1.15,
        textAlign: "center",
        color: captionStyle.color,
        ...outlineStyle(captionStyle.outlineWidth, captionStyle.outlineColor),
        // Starts at 0.5, not 0: consecutive cues swap on the same frame, so a cue
        // starting fully transparent left one caption-less frame at every change -
        // a visible flicker (found on Day 18 from a still that landed on it).
        opacity: 0.5 + 0.5 * entrance,
        transform: `scale(${0.85 + 0.15 * entrance})`,
      }}
    >
      {cue.words.map((word, index) => (
        <span
          // Words can repeat within a chunk, so the index is part of the key.
          key={`${index}-${word.text}`}
          style={
            word.emphasized
              ? {
                  color: captionStyle.emphasisColor,
                  display: "inline-block",
                  // Room for the brief entrance overshoot, so it never
                  // touches the neighbouring words.
                  margin: "0 0.06em",
                  transform: `scale(${emphasisScale})`,
                }
              : undefined
          }
        >
          {word.text}
        </span>
      ))}
    </div>
  );
};

export type CaptionRendererProps = {
  narration: string | null;
  emphasis: string | null;
  durationInFrames: number;
  // How many of those frames are actual speech (from real narration timing), if known.
  spokenFrames?: number;
  captionStyle: CaptionStyle;
};

// Renders one scene's captions. Each cue is its own <Sequence>, so Remotion -
// not hand-written "which cue is active" checks - decides what's visible on
// every frame, the same way <Series> handles scenes.
export const CaptionRenderer: React.FC<CaptionRendererProps> = ({
  narration,
  emphasis,
  durationInFrames,
  spokenFrames,
  captionStyle,
}) => {
  const cues = buildCaptionCues(
    narration,
    emphasis,
    durationInFrames,
    captionStyle.chunking,
    spokenFrames,
  );

  return (
    <AbsoluteFill style={{ pointerEvents: "none" }}>
      {cues.map((cue) => (
        <Sequence
          key={cue.startFrame}
          from={cue.startFrame}
          durationInFrames={cue.endFrame - cue.startFrame}
          layout="none"
        >
          <div
            style={{
              position: "absolute",
              left: SHORTS_SAFE_AREA.left,
              right: SHORTS_SAFE_AREA.right,
              bottom: SHORTS_SAFE_AREA.bottom,
              display: "flex",
              justifyContent: "center",
            }}
          >
            <CaptionChunk cue={cue} captionStyle={captionStyle} />
          </div>
        </Sequence>
      ))}
    </AbsoluteFill>
  );
};
