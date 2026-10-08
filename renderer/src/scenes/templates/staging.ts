import { Easing, interpolate } from "remotion";

// Templates reveal their parts in step with what's being said (the fact after
// the myth, list items one by one). "Spoken" is the part of the scene with
// actual speech (Day 12); fractions of it land the reveal on the words.
export function spokenFrames(spokenDurationSeconds: number | null | undefined, fps: number, durationInFrames: number): number {
  const spoken = spokenDurationSeconds ? Math.round(spokenDurationSeconds * fps) : durationInFrames;
  return Math.max(1, Math.min(spoken, durationInFrames));
}

// 0 -> 1 over `frames` frames starting at `startFrame`, eased out.
export function revealAt(frame: number, startFrame: number, frames = 8): number {
  return interpolate(frame, [startFrame, startFrame + frames], [0, 1], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
    easing: Easing.out(Easing.cubic),
  });
}

// When item `index` of `count` appears: spread over the first 80% of the speech,
// so the last item is on screen while it's still being talked about.
export function itemStartFrame(index: number, count: number, spoken: number): number {
  if (count <= 1) {
    return 0;
  }
  return Math.round((index / count) * spoken * 0.8);
}

// "pullup-top" <-> "pullup-bottom": the two poses of one exercise, if this id is one.
export function poseCounterpart(assetId: string): string | null {
  if (assetId.endsWith("-top")) {
    return `${assetId.slice(0, -4)}-bottom`;
  }
  if (assetId.endsWith("-bottom")) {
    return `${assetId.slice(0, -7)}-top`;
  }
  return null;
}

// Which pose shows at `frame` for an alternating rep animation, and how far
// into the crossfade to the next pose we are. Starts at the bottom of the rep.
export function repPose(frame: number, fps: number, repSeconds = 1.4): { top: number; reps: number } {
  const half = Math.max(2, Math.round((repSeconds / 2) * fps));
  const phase = frame % (half * 2);
  // Short on purpose: the two pose drawings aren't registered to the same bar
  // position, so a long crossfade showed both ghosted on top of each other.
  // 2 frames reads as a crisp flipbook rep.
  const crossfade = 2;
  const top =
    phase < half
      ? interpolate(phase, [half - crossfade, half], [0, 1], { extrapolateLeft: "clamp", extrapolateRight: "clamp" })
      : interpolate(phase, [half * 2 - crossfade, half * 2], [1, 0], { extrapolateLeft: "clamp", extrapolateRight: "clamp" });
  return { top, reps: Math.floor(frame / (half * 2)) + (phase >= half ? 1 : 0) };
}

// The first number in a text, split out so a template can show it big.
export function splitLeadingNumber(text: string): { before: string; value: number; after: string } | null {
  const match = /(\d+)/.exec(text);
  if (!match || match.index === undefined) {
    return null;
  }
  return {
    before: text.slice(0, match.index).trim(),
    value: Number(match[1]),
    after: text.slice(match.index + match[1].length).trim(),
  };
}
