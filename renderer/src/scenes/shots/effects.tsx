import React from "react";
import { AbsoluteFill, Easing, interpolate } from "remotion";
import { imageInfo } from "../../assets/art";
import { placeArt, type Box } from "../layout";
import { fitBlockFontSize } from "../templates/fit";
import { WALL_FONT } from "../templates/shared";
import { stopwatch } from "./goal";

// Visual signs drawn in code (no art needed): they carry the meaning when the sound is off.
// Every one is a pure function of the frame, as Remotion requires.

const clamp = { extrapolateLeft: "clamp", extrapolateRight: "clamp" } as const;

export type Point = { x: number; y: number };

/** Where [fx, fy] (fractions of the figure's bounds) lands in the box, as AssetView places the art. */
export function focusPx(art: string | undefined, box: Box, fp: readonly number[] | null | undefined): Point | null {
  const info = art ? imageInfo(art) : null;
  if (!info || !fp || fp.length !== 2) {
    return null;
  }
  const p = placeArt(info, info.anchor, box);
  const s = p.width / info.width;
  const [x0, y0, x1, y1] = info.bounds;
  return { x: p.left + (x0 + fp[0] * (x1 - x0)) * s, y: p.top + (y0 + fp[1] * (y1 - y0)) * s };
}

/** Red rings pulsing out of a body part: "this hurts / this is wrong". */
export const PainPulse: React.FC<{ at: Point; frame: number }> = ({ at, frame }) => (
  <svg style={{ position: "absolute", left: at.x - 160, top: at.y - 160, overflow: "visible" }} width={320} height={320}>
    {[0, 12, 24].map((delay) => {
      const t = ((frame + 36 - delay) % 36) / 36;
      return <circle key={delay} cx={160} cy={160} r={30 + t * 120} fill="none" stroke="#EF4444" strokeWidth={14 * (1 - t)} opacity={1 - t} />;
    })}
    <circle cx={160} cy={160} r={26} fill="#EF4444" opacity={0.85} />
  </svg>
);

/** A green ring that pops onto the body part that matters. */
export const FocusRing: React.FC<{ at: Point; frame: number; color?: string }> = ({ at, frame, color = "#22C55E" }) => {
  const pop = interpolate(frame, [0, 8], [0, 1], { ...clamp, easing: Easing.out(Easing.back(2.2)) });
  const breathe = 1 + Math.sin(frame / 6) * 0.05;
  return (
    <div
      style={{
        position: "absolute",
        left: at.x - 95,
        top: at.y - 95,
        width: 190,
        height: 190,
        borderRadius: "50%",
        border: `12px solid ${color}`,
        boxShadow: `0 0 0 6px rgba(255,255,255,0.85)`,
        transform: `scale(${pop * breathe})`,
      }}
    />
  );
};

const ARROW_ANGLE: Record<string, number> = { up: -90, down: 90, in: 180, out: 0 };

/** A thick arrow next to the body part, nudging in the direction that fixes it. */
export const ArrowMark: React.FC<{ at: Point; dir: string | null | undefined; frame: number; color?: string }> = ({
  at,
  dir,
  frame,
  color = "#22C55E",
}) => {
  if (!dir || !(dir in ARROW_ANGLE)) {
    return null;
  }
  const angle = ARROW_ANGLE[dir];
  const rad = (angle * Math.PI) / 180;
  const nudge = 18 + Math.sin(frame / 4) * 14;
  // Start beside the ring, pointing the way the body part should move.
  const cx = at.x + Math.cos(rad) * (120 + nudge);
  const cy = at.y + Math.sin(rad) * (120 + nudge);
  const show = interpolate(frame, [4, 12], [0, 1], clamp);
  return (
    <svg
      width={200}
      height={140}
      viewBox="0 0 200 140"
      style={{ position: "absolute", left: cx - 100, top: cy - 70, transform: `rotate(${angle}deg)`, opacity: show, overflow: "visible" }}
    >
      <path d="M10 70 H130 M100 25 L160 70 L100 115" fill="none" stroke="#FFFFFF" strokeWidth={46} strokeLinecap="round" strokeLinejoin="round" />
      <path d="M10 70 H130 M100 25 L160 70 L100 115" fill="none" stroke={color} strokeWidth={28} strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
};

/** Short lines bursting around a point: shock, surprise, impact. */
export const ShockLines: React.FC<{ at: Point; frame: number; color?: string; radius?: number }> = ({
  at,
  frame,
  color = "#FACC15",
  radius = 230,
}) => {
  const burst = interpolate(frame, [0, 6, 14], [0.6, 1.08, 1], clamp);
  return (
    <svg style={{ position: "absolute", left: at.x - radius * 1.6, top: at.y - radius * 1.6, overflow: "visible" }} width={radius * 3.2} height={radius * 3.2}>
      {Array.from({ length: 10 }, (_, i) => {
        const a = (i / 10) * Math.PI * 2 + 0.3;
        const r0 = radius * burst;
        const r1 = radius * 1.45 * burst;
        const c = radius * 1.6;
        return (
          <line key={i} x1={c + Math.cos(a) * r0} y1={c + Math.sin(a) * r0} x2={c + Math.cos(a) * r1} y2={c + Math.sin(a) * r1}
            stroke={color} strokeWidth={14} strokeLinecap="round" />
        );
      })}
    </svg>
  );
};

/** Sweat drops falling beside a head. */
export const SweatDrops: React.FC<{ at: Point; frame: number }> = ({ at, frame }) => (
  <svg style={{ position: "absolute", left: at.x, top: at.y - 40, overflow: "visible" }} width={200} height={260}>
    {[0, 1, 2].map((i) => {
      const t = ((frame + i * 11) % 33) / 33;
      return (
        <path
          key={i}
          transform={`translate(${40 + i * 52} ${t * 150}) scale(1.4)`}
          d="M14 0 C14 0 0 20 0 30 A14 14 0 0 0 28 30 C28 20 14 0 14 0 Z"
          fill="#7DD3FC"
          stroke="#FFFFFF"
          strokeWidth={4}
          opacity={1 - t * 0.6}
        />
      );
    })}
  </svg>
);

/** A soft spotlight cone from the top of the frame. */
export const Spotlight: React.FC<{ frame: number }> = ({ frame }) => (
  <AbsoluteFill
    style={{
      opacity: interpolate(frame, [0, 10], [0, 1], clamp),
      background: "radial-gradient(ellipse 55% 80% at 50% 0%, rgba(255,250,220,0.55) 0%, rgba(255,250,220,0.18) 45%, transparent 75%)",
    }}
  />
);

/** Rays turning slowly behind the hero moment. */
export const Sunburst: React.FC<{ frame: number; color: string; center: Point }> = ({ frame, color, center }) => (
  <svg style={{ position: "absolute", left: center.x - 1300, top: center.y - 1300 }} width={2600} height={2600}>
    <g transform={`rotate(${frame * 0.4} 1300 1300)`} opacity={0.28}>
      {Array.from({ length: 16 }, (_, i) => {
        const a0 = (i / 16) * Math.PI * 2;
        const a1 = a0 + Math.PI / 16;
        return (
          <path key={i} d={`M1300 1300 L${1300 + Math.cos(a0) * 1400} ${1300 + Math.sin(a0) * 1400} L${1300 + Math.cos(a1) * 1400} ${1300 + Math.sin(a1) * 1400} Z`} fill={color} />
        );
      })}
    </g>
  </svg>
);

/** n of total bars filled: difficulty, progress. */
export const Meter: React.FC<{ value: number; total: number; frame: number; color: string; top: number }> = ({ value, total, frame, color, top }) => {
  const barW = Math.min(150, 760 / Math.max(1, total) - 16);
  const width = total * (barW + 16) - 16;
  return (
    <div style={{ position: "absolute", top, left: (1080 - width) / 2, display: "flex", gap: 16 }}>
      {Array.from({ length: total }, (_, i) => {
        const on = i < value ? interpolate(frame, [4 + i * 4, 10 + i * 4], [0, 1], clamp) : 0;
        return (
          <div key={i} style={{ width: barW, height: 40, borderRadius: 8, background: "rgba(255,255,255,0.14)", overflow: "hidden", transform: "skewX(-12deg)" }}>
            <div style={{ width: `${on * 100}%`, height: "100%", background: color }} />
          </div>
        );
      })}
    </div>
  );
};

/** One giant word behind everything, like lettering on the back wall ("STOP", "3", "LEVEL 2"). */
export const BigWord: React.FC<{ text: string; color: string; frame: number; top: number; size?: number; opacity?: number; maxLines?: number }> = ({
  text,
  color,
  frame,
  top,
  size = 420,
  opacity = 0.24,
  maxLines = 2,
}) => {
  const slam = interpolate(frame, [0, 7], [1.25, 1], { ...clamp, easing: Easing.out(Easing.cubic) });
  return (
    <div
      style={{
        position: "absolute",
        top,
        left: 0,
        right: 0,
        textAlign: "center",
        color,
        opacity,
        fontFamily: WALL_FONT,
        fontSize: fitBlockFontSize(text, 1040 * 1.2, size, 120, maxLines),
        lineHeight: 0.9,
        textTransform: "uppercase",
        transform: `scale(${slam}) rotate(-3deg)`,
      }}
    >
      {text}
    </div>
  );
};

/** The level's benchmark as a scoreboard: a stopwatch or a rep counter that turns green with a check at the goal. */
export const GoalCounter: React.FC<{ value: number; goal: number; unit: "seconds" | "reps"; frame: number; style?: React.CSSProperties }> = ({
  value,
  goal,
  unit,
  frame,
  style,
}) => {
  const done = value >= goal;
  const pop = interpolate(frame, [0, 8], [0, 1], { ...clamp, easing: Easing.out(Easing.back(1.8)) });
  return (
    <div
      style={{
        position: "absolute",
        padding: "14px 28px 10px",
        borderRadius: 18,
        background: done ? "#16A34A" : "rgba(15,15,18,0.82)",
        border: `6px solid ${done ? "#4ADE80" : "rgba(255,255,255,0.25)"}`,
        color: "#FFFFFF",
        fontFamily: WALL_FONT,
        textAlign: "center",
        transform: `scale(${pop}) rotate(-4deg)`,
        boxShadow: "8px 8px 0 rgba(0,0,0,0.4)",
        ...style,
      }}
    >
      <div style={{ fontSize: 120, lineHeight: 1, fontVariantNumeric: "tabular-nums" }}>
        {unit === "seconds" ? stopwatch(value) : value}
        {done ? " ✓" : ""}
      </div>
      <div style={{ fontSize: 44, letterSpacing: 4, opacity: 0.85 }}>{unit === "seconds" ? "SECONDS" : goal === 1 ? "REP" : `/ ${goal} REPS`}</div>
    </div>
  );
};

/** A quick white flash: the level-up moment. */
export const Flash: React.FC<{ frame: number; strength?: number }> = ({ frame, strength = 0.55 }) => {
  const o = interpolate(frame, [0, 7], [strength, 0], clamp);
  return o > 0 ? <AbsoluteFill style={{ background: "#FFFFFF", opacity: o }} /> : null;
};

/** A small slanted label: "✗ SAGGING HIPS", "✓ SQUEEZE GLUTES". */
export const Tag: React.FC<{ text: string; color: string; style?: React.CSSProperties }> = ({ text, color, style }) => (
  <div
    style={{
      position: "absolute",
      padding: "10px 26px",
      background: color,
      color: "#FFFFFF",
      fontFamily: WALL_FONT,
      fontSize: 62,
      letterSpacing: 2,
      textTransform: "uppercase",
      transform: "skewX(-10deg)",
      boxShadow: "6px 6px 0 rgba(0,0,0,0.35)",
      ...style,
    }}
  >
    {text}
  </div>
);
