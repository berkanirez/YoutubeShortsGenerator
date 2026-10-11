import { Easing, interpolate } from "remotion";

// The level's benchmark counted up on screen while it is talked about: a stopwatch for
// "held for 60 seconds", a rep counter for "10 strict pull-ups". Fast-forwarded - it reaches
// the goal at 80% of the speech, then holds there with a check.
export function goalCount(frame: number, spokenFrames: number, goal: number): number {
  const end = Math.max(2, Math.round(spokenFrames * 0.8));
  const t = interpolate(frame, [4, end], [0, 1], { extrapolateLeft: "clamp", extrapolateRight: "clamp", easing: Easing.inOut(Easing.quad) });
  return Math.round(t * goal);
}

/** 45 -> "0:45", 60 -> "1:00", 90 -> "1:30". */
export function stopwatch(seconds: number): string {
  const s = Math.max(0, Math.round(seconds));
  return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, "0")}`;
}
