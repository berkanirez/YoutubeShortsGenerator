import React from "react";
import { Easing, interpolate } from "remotion";
import { imageInfo } from "../../assets/art";
import type { ShotType } from "../../schemas/videoPlan";
import { FLOOR_Y, VISUAL_TOP_ALONE, VISUAL_TOP_WITH_WALL, WALL_TOP, type Box } from "../layout";
import { AssetView, GREEN, RED, Sign, WALL_FONT, WallText, type TemplateProps } from "../templates/shared";
import { repPose, spokenFrames } from "../templates/staging";
import { pickVariant } from "../templates/variant";
import { wallText } from "../templates/wall";
import { Camera } from "./camera";
import { ArrowMark, BigWord, Flash, FocusRing, focusPx, GoalCounter, Meter, PainPulse, ShockLines, Spotlight, Sunburst, SweatDrops, Tag } from "./effects";
import { goalCount } from "./goal";
import { cycleFrames, onceFrame, repFrame } from "./cycle";

// One component per storyboard shot (ShortsGenerator.Domain.ShotType). Each one says its
// point with a picture and a sign, so it works with the sound off: the cross and the check,
// the pain point, the arrow, the magnifier, the meter. The camera frames the shot (camera.tsx).

const clamp = { extrapolateLeft: "clamp", extrapolateRight: "clamp" } as const;
const FULL: Box = { width: 1080, height: FLOOR_Y - VISUAL_TOP_ALONE };
// Below a giant word: the word stays readable above the head.
const HERO_TOP = 600;
const LEVEL_TOP = 600;
const HERO: Box = { width: 1080, height: FLOOR_Y - HERO_TOP };
const UNDER_WALL: Box = { width: 1080, height: FLOOR_Y - VISUAL_TOP_WITH_WALL };

const Area: React.FC<{ top: number; box: Box; children: React.ReactNode; style?: React.CSSProperties }> = ({ top, box, children, style }) => (
  <div style={{ position: "absolute", left: 0, top, width: box.width, height: box.height, ...style }}>{children}</div>
);

const Wall: React.FC<{ text: string | null | undefined; color: string; frame: number; maxWords?: number; tilt?: number }> = ({
  text,
  color,
  frame,
  maxWords = 4,
  tilt = -4,
}) => {
  const t = wallText(text, maxWords);
  return t ? (
    <div style={{ position: "absolute", top: WALL_TOP, left: 0, right: 0, display: "flex", justifyContent: "center" }}>
      <WallText text={t} color={color} frame={frame} tilt={tilt} />
    </div>
  ) : null;
};

// The exercise doing reps: the two poses of a pair alternate (registered on their union
// bounds so the floor/bar stays put); a single picture just stands.
const Reps: React.FC<{ main: string; partner?: string; art: Record<string, string>; box: Box; frame: number; fps: number }> = ({
  main,
  partner,
  art,
  box,
  frame,
  fps,
}) => {
  // A four-frame animation when the catalog has one: the rep as a flipbook.
  const frames = cycleFrames(art, main);
  if (frames) {
    const all = frames.map((id) => imageInfo(art[id] ?? "")?.bounds).filter((b) => b !== undefined);
    const bounds = all.length === 4
      ? ([Math.min(...all.map((b) => b[0])), Math.min(...all.map((b) => b[1])), Math.max(...all.map((b) => b[2])), Math.max(...all.map((b) => b[3]))] as const)
      : undefined;
    return <AssetView svg={art[frames[repFrame(frame, fps)]]} box={box} bounds={bounds} />;
  }
  const partnerArt = partner ? art[partner] : undefined;
  if (!partnerArt) {
    return <AssetView svg={art[main]} box={box} />;
  }
  const a = imageInfo(art[main] ?? "")?.bounds;
  const b = imageInfo(partnerArt)?.bounds;
  const union = a && b ? ([Math.min(a[0], b[0]), Math.min(a[1], b[1]), Math.max(a[2], b[2]), Math.max(a[3], b[3])] as const) : undefined;
  const { top } = repPose(frame, fps, 1.6);
  const mainIsTop = main.endsWith("-top");
  return (
    <>
      <AssetView svg={art[main]} box={box} bounds={union} style={{ position: "absolute", opacity: mainIsTop ? top : 1 - top }} />
      <AssetView svg={partnerArt} box={box} bounds={union} style={{ position: "absolute", opacity: mainIsTop ? 1 - top : top }} />
    </>
  );
};

// Where a picture stands. Tall art (a standing or hanging figure) uses the floor; wide art
// (a push-up, a plank) is short, so it stands higher - above the captions - and gets the full
// width instead of sitting tiny at the bottom behind the caption text.
const CAPTION_LINE = 1330;
function standBox(art: string | undefined, top: number): Box {
  const b = art ? imageInfo(art)?.bounds : undefined;
  const wide = b ? (b[2] - b[0]) / Math.max(1, b[3] - b[1]) > 1.3 : false;
  return { width: 1080, height: (wide ? CAPTION_LINE : FLOOR_Y) - top };
}

const shotFrame = (p: TemplateProps) => ({ frame: p.frame, durationInFrames: p.durationInFrames, enabled: p.camera !== false });

// --- Hooks -----------------------------------------------------------------------------

const HookStop: React.FC<TemplateProps> = (p) => (
  <Camera move="whipIn" {...shotFrame(p)}>
    <BigWord text="STOP" color={RED} frame={p.frame} top={170} size={480} opacity={0.55} maxLines={1} />
    <Area top={HERO_TOP} box={HERO}>
      <AssetView svg={p.assetSvgById[p.assetId]} box={HERO} />
    </Area>
    <Sign kind="cross" frame={p.frame - 8} color={RED} size={340} style={{ right: 30, top: 640 }} />
  </Camera>
);

const HookNumber: React.FC<TemplateProps> = (p) => (
  <Camera move="whipIn" {...shotFrame(p)}>
    <BigWord text={String(p.scene.count ?? 3)} color={p.palette.accent} frame={p.frame} top={180} size={1100} opacity={0.3} maxLines={1} />
    <Wall text="MISTAKES" color={p.palette.accent} frame={p.frame - 4} />
    <Area top={VISUAL_TOP_WITH_WALL} box={UNDER_WALL}>
      <AssetView svg={p.assetSvgById[p.assetId]} box={UNDER_WALL} />
    </Area>
  </Camera>
);

const HookShock: React.FC<TemplateProps> = (p) => {
  const head = focusPx(p.assetSvgById[p.assetId], FULL, [0.5, 0.1]);
  return (
    <Camera move="whipIn" {...shotFrame(p)}>
      <BigWord text="?" color={p.palette.accent} frame={p.frame} top={160} size={1000} opacity={0.26} maxLines={1} />
      <Area top={VISUAL_TOP_ALONE} box={FULL}>
        <AssetView svg={p.assetSvgById[p.assetId]} box={FULL} />
        {head ? <ShockLines at={head} frame={p.frame - 3} /> : null}
      </Area>
    </Camera>
  );
};

// Two pictures stacked, each standing on its own floor line, clear of the captions.
const HALF: Box = { width: 1080, height: 540 };
const HALF_TOP = [200, 770];

const HookQuestion: React.FC<TemplateProps> = (p) => {
  const [wrong, right] = p.scene.altAssetIds ?? [];
  // Which one is on top changes per video, so "top = wrong" can't be learned.
  const order = pickVariant(p.variantSeed, 2) === 0 ? [wrong, right] : [right, wrong];
  return (
    <Camera move="whipIn" {...shotFrame(p)}>
      {order.map((id, i) => (
        <Area key={id ?? i} top={HALF_TOP[i]} box={HALF} style={{ borderBottom: "6px solid rgba(255,255,255,0.18)" }}>
          <AssetView svg={id ? p.assetSvgById[id] : undefined} box={HALF} />
          <Tag text={i === 0 ? "A" : "B"} color={p.palette.accent} style={{ left: 40, top: 20 }} />
        </Area>
      ))}
      <Sign kind="question" frame={p.frame - 10} color={p.palette.accent} size={420} style={{ right: 10, top: 560 }} />
    </Camera>
  );
};

// --- Middle shots ----------------------------------------------------------------------

const KineticWord: React.FC<TemplateProps> = (p) => (
  <Camera move="pushIn" {...shotFrame(p)}>
    <BigWord text={p.scene.onScreenText ?? ""} color={p.palette.accent} frame={p.frame} top={200} size={240} opacity={0.6} />
    <Area top={HERO_TOP} box={HERO}>
      <AssetView svg={p.assetSvgById[p.assetId]} box={HERO} />
    </Area>
  </Camera>
);

const SplitFix: React.FC<TemplateProps> = (p) => {
  const right = p.scene.assetIds[0];
  const wrong = p.scene.altAssetIds?.[0];
  const spoken = spokenFrames(p.scene.spokenDurationSeconds, p.fps, p.durationInFrames);
  const turn = Math.round(spoken * 0.45);
  const reveal = interpolate(p.frame, [turn, turn + 8], [0, 1], { ...clamp, easing: Easing.out(Easing.cubic) });
  const wrongPoint = focusPx(wrong ? p.assetSvgById[wrong] : undefined, HALF, p.scene.focusPoint);
  const rightPoint = focusPx(right ? p.assetSvgById[right] : undefined, HALF, p.scene.focusPoint);
  return (
    <Camera move="none" {...shotFrame(p)}>
      <div style={{ position: "absolute", left: 40, top: 40, fontFamily: WALL_FONT, fontSize: 150, color: p.palette.accent, opacity: 0.9, lineHeight: 1 }}>
        #{p.scene.count ?? ""}
      </div>
      <Area top={HALF_TOP[0]} box={HALF} style={{ borderBottom: "6px solid rgba(255,255,255,0.18)" }}>
        {/* The wrong way, drained of colour: wrong is grey, right is in colour. */}
        <div style={{ position: "absolute", inset: 0, filter: "grayscale(1) brightness(0.8)" }}>
          <AssetView svg={wrong ? p.assetSvgById[wrong] : undefined} box={HALF} />
        </div>
        {wrongPoint ? <PainPulse at={wrongPoint} frame={p.frame} /> : null}
        <Sign kind="cross" frame={p.frame - 4} color={RED} size={240} style={{ right: 30, top: 40 }} />
        {p.scene.onScreenText ? <Tag text={`✗ ${p.scene.onScreenText}`} color={RED} style={{ left: 240, top: 30 }} /> : null}
      </Area>
      <Area top={HALF_TOP[1]} box={HALF} style={{ opacity: reveal, transform: `translateY(${(1 - reveal) * 60}px)` }}>
        <AssetView svg={right ? p.assetSvgById[right] : undefined} box={HALF} />
        {rightPoint && reveal > 0 ? <FocusRing at={rightPoint} frame={p.frame - turn} /> : null}
        <Sign kind="check" frame={p.frame - turn - 3} color={GREEN} size={240} style={{ right: 30, top: 40 }} />
        {p.scene.secondaryText ? <Tag text={`✓ ${p.scene.secondaryText}`} color={GREEN} style={{ left: 40, top: 30 }} /> : null}
      </Area>
    </Camera>
  );
};

const MistakeReaction: React.FC<TemplateProps> = (p) => {
  const head = focusPx(p.assetSvgById[p.assetId], UNDER_WALL, [0.62, 0.06]);
  return (
    <Camera move="punch" {...shotFrame(p)}>
      <BigWord text={`#${p.scene.count ?? ""}`} color={RED} frame={p.frame} top={380} size={900} opacity={0.22} maxLines={1} />
      <Wall text={p.scene.onScreenText} color={RED} frame={p.frame} />
      <Area top={VISUAL_TOP_WITH_WALL} box={UNDER_WALL}>
        <AssetView svg={p.assetSvgById[p.assetId]} box={UNDER_WALL} />
        {head ? <SweatDrops at={head} frame={p.frame} /> : null}
      </Area>
      <Sign kind="cross" frame={p.frame - 8} color={RED} size={320} style={{ left: 20, top: 640 }} />
    </Camera>
  );
};

const FixDemo: React.FC<TemplateProps> = (p) => {
  const main = p.scene.assetIds[0];
  const box = standBox(main ? p.assetSvgById[main] : undefined, VISUAL_TOP_WITH_WALL);
  const point = focusPx(main ? p.assetSvgById[main] : undefined, box, p.scene.focusPoint);
  return (
    <Camera move="pushIn" {...shotFrame(p)}>
      <Wall text={p.scene.onScreenText} color={GREEN} frame={p.frame} />
      <Area top={VISUAL_TOP_WITH_WALL} box={box}>
        {main ? <Reps main={main} partner={p.scene.altAssetIds?.[0]} art={p.assetSvgById} box={box} frame={p.frame} fps={p.fps} /> : null}
        {point ? <FocusRing at={point} frame={p.frame - 6} /> : null}
        {point ? <ArrowMark at={point} dir={p.scene.arrow} frame={p.frame - 6} /> : null}
      </Area>
      <Sign kind="check" frame={p.frame - 10} color={GREEN} size={260} style={{ right: 30, top: 470 }} />
    </Camera>
  );
};

const ZoomDetail: React.FC<TemplateProps> = (p) => {
  const main = p.scene.assetIds[0];
  const art = main ? p.assetSvgById[main] : undefined;
  const box = standBox(art, VISUAL_TOP_WITH_WALL);
  const point = focusPx(art, box, p.scene.focusPoint);
  const r = 230;
  const zoom = 2.4;
  const circle = { x: 1080 - r - 50, y: 70 + r };
  const open = interpolate(p.frame, [6, 16], [0, 1], { ...clamp, easing: Easing.out(Easing.back(1.6)) });
  return (
    <Camera move="none" {...shotFrame(p)}>
      <Wall text={p.scene.onScreenText} color={GREEN} frame={p.frame} />
      <Area top={VISUAL_TOP_WITH_WALL} box={box}>
        <AssetView svg={art} box={box} />
        {point ? (
          <>
            <svg style={{ position: "absolute", left: 0, top: 0, overflow: "visible" }} width={1080} height={box.height}>
              <line x1={point.x} y1={point.y} x2={circle.x} y2={circle.y} stroke="#FFFFFF" strokeWidth={8} strokeDasharray="18 12" opacity={open} />
            </svg>
            <FocusRing at={point} frame={p.frame} color="#FFFFFF" />
            <div
              style={{
                position: "absolute",
                left: circle.x - r,
                top: circle.y - r,
                width: 2 * r,
                height: 2 * r,
                borderRadius: "50%",
                overflow: "hidden",
                border: `12px solid ${GREEN}`,
                background: "#2F3033",
                transform: `scale(${open})`,
                boxShadow: "0 0 0 8px #FFFFFF",
              }}
            >
              <div style={{ position: "absolute", left: r - point.x * zoom, top: r - point.y * zoom, width: 1080, height: box.height, transform: `scale(${zoom})`, transformOrigin: "0 0" }}>
                <AssetView svg={art} box={box} />
              </div>
            </div>
            <Sign kind="check" frame={p.frame - 14} color={GREEN} size={170} style={{ left: circle.x + r * 0.35, top: circle.y + r * 0.45 }} />
          </>
        ) : null}
      </Area>
    </Camera>
  );
};

// The ladder at a glance: up to 3 side by side, 4 as a 2x2 grid. Every cell has its own number
// and frame, so a plank, a hanging pose and a standing pose all read cleanly (one shared row put
// a plank tiny next to a figure hanging from a bar).
const LINEUP_TOP = 470;
const LINEUP_BOTTOM = 1390;

const Lineup: React.FC<TemplateProps> = (p) => {
  const ids = p.scene.assetIds;
  const n = Math.max(1, ids.length);
  const cols = n === 4 ? 2 : n;
  const rows = Math.ceil(n / cols);
  const cellW = 1080 / cols;
  const cellH = (LINEUP_BOTTOM - LINEUP_TOP) / rows;
  const box: Box = { width: cellW - 40, height: cellH - 90 };
  const isHook = p.scene.type === "Hook";
  return (
    <Camera move="pushIn" {...shotFrame(p)}>
      <Wall text={p.scene.onScreenText} color={p.palette.accent} frame={p.frame} maxWords={4} />
      {ids.map((id, i) => {
        const pop = interpolate(p.frame, [4 + i * 5, 12 + i * 5], [0, 1], { ...clamp, easing: Easing.out(Easing.back(1.8)) });
        const left = (i % cols) * cellW;
        const top = LINEUP_TOP + Math.floor(i / cols) * cellH;
        return (
          <div key={id} style={{ position: "absolute", left, top, width: cellW, height: cellH, opacity: pop, transform: `scale(${0.7 + 0.3 * pop})` }}>
            <div style={{ position: "absolute", left: 70, top: 0, fontFamily: WALL_FONT, fontSize: 96, lineHeight: 1, color: p.palette.accent, textShadow: "5px 5px 0 rgba(0,0,0,0.45)" }}>
              {i + 1}
            </div>
            {/* The hook hides the top level: a black silhouette with a "?" - the viewer stays to see it. */}
            <div style={{ position: "absolute", left: 20, top: 80, filter: isHook && i === ids.length - 1 ? "brightness(0) opacity(0.85)" : undefined }}>
              <AssetView svg={p.assetSvgById[id]} box={box} />
            </div>
            {isHook && i === ids.length - 1 ? (
              <div style={{ position: "absolute", left: 0, right: 0, top: 80 + box.height * 0.3, textAlign: "center", fontFamily: WALL_FONT, fontSize: 200, color: p.palette.accent, textShadow: "6px 6px 0 rgba(0,0,0,0.5)" }}>
                ?
              </div>
            ) : null}
          </div>
        );
      })}

    </Camera>
  );
};

const LevelShow: React.FC<TemplateProps> = (p) => {
  const main = p.scene.assetIds[0];
  const level = p.scene.count ?? 1;
  const total = p.scene.total ?? level;
  const levelBox = standBox(main ? p.assetSvgById[main] : undefined, LEVEL_TOP);
  const goal = p.scene.goalValue;
  const unit = p.scene.goalUnit;
  const spoken = spokenFrames(p.scene.spokenDurationSeconds, p.fps, p.durationInFrames);
  return (
    <>
    <Camera move="pushIn" {...shotFrame(p)}>
      <Wall text={`LEVEL ${level}`} color={p.palette.accent} frame={p.frame} />
      <Meter value={level} total={total} frame={p.frame} color={p.palette.accent} top={400} />
      {p.scene.secondaryText ? (
        <div style={{ position: "absolute", top: 460, left: 0, right: 0, textAlign: "center", fontFamily: WALL_FONT, fontSize: 96, color: "#FFFFFF", textTransform: "uppercase", textShadow: "6px 6px 0 rgba(0,0,0,0.45)" }}>
          {p.scene.secondaryText}
        </div>
      ) : null}
      <Area top={LEVEL_TOP} box={levelBox}>
        {main ? <Reps main={main} partner={p.scene.altAssetIds?.[0]} art={p.assetSvgById} box={levelBox} frame={p.frame} fps={p.fps} /> : null}
      </Area>
      {goal && unit ? (
        <GoalCounter value={goalCount(p.frame, spoken, goal)} goal={goal} unit={unit} frame={p.frame - 6} style={{ right: 40, top: 640 }} />
      ) : null}
    </Camera>
    {/* The level-up moment: a flash on the cut (the sound is the scene's levelUp sfx). */}
    <Flash frame={p.frame} />
    </>
  );
};

const Payoff: React.FC<TemplateProps> = (p) => (
  <Camera move="pushIn" {...shotFrame(p)}>
    <Sunburst frame={p.frame} color={p.palette.accent} center={{ x: 540, y: 950 }} />
    <Spotlight frame={p.frame} />
    <Wall text={p.scene.onScreenText} color="#FACC15" frame={p.frame} maxWords={3} />
    <Area top={VISUAL_TOP_WITH_WALL} box={UNDER_WALL}>
      <AssetView svg={p.assetSvgById[p.assetId]} box={UNDER_WALL} />
    </Area>
  </Camera>
);

const CtaLoop: React.FC<TemplateProps> = (p) => {
  // The pointing pose, animated when the gesture's frames exist: the arm comes up to point.
  const gesture = p.assetId === "mascot-pointing" ? cycleFrames(p.assetSvgById, "mascot-point") : null;
  const art = gesture ? p.assetSvgById[gesture[onceFrame(p.frame)]] : p.assetSvgById[p.assetId];
  return (
  <>
    <Camera move="closeUp" {...shotFrame(p)}>
      <Area top={VISUAL_TOP_ALONE} box={FULL}>
        <AssetView svg={art} box={FULL} />
      </Area>
    </Camera>
    <Wall text={p.scene.onScreenText} color={p.palette.accent} frame={p.frame} />
  </>
  );
};


// Exhaustive by type: a shot added to the schema without a component here doesn't compile.
export const SHOT_BY_TYPE: Record<ShotType, React.FC<TemplateProps>> = {
  hookStop: HookStop,
  hookNumber: HookNumber,
  hookShock: HookShock,
  hookQuestion: HookQuestion,
  kineticWord: KineticWord,
  splitFix: SplitFix,
  mistakeReaction: MistakeReaction,
  fixDemo: FixDemo,
  zoomDetail: ZoomDetail,
  lineup: Lineup,
  levelShow: LevelShow,
  payoff: Payoff,
  ctaLoop: CtaLoop,
};

