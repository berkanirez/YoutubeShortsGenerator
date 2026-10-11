import type { ChunkingRules } from "./chunking";

// Areas of a 1080x1920 frame that YouTube's Shorts UI draws over: the
// title/channel/description block along the bottom, and the like/comment/
// share column down the right edge. Captions must stay out of both, or the
// viewer can't read them on a real phone.
export const SHORTS_SAFE_AREA = {
  top: 220,
  bottom: 480,
  left: 120,
  right: 160,
} as const;

export type CaptionStyle = {
  chunking: ChunkingRules;
  fontFamily: string;
  fontSize: number;
  fontWeight: number;
  textTransform: "none" | "uppercase";
  color: string;
  emphasisColor: string;
  outlineColor: string;
  outlineWidth: number;
  // Optional backing box behind the text; null = outline only.
  boxColor: string | null;
};

// A controlled set of presets, chosen by the plan's `captionStyle` field.
// Like animation ids, the plan names a style; it never supplies raw CSS.
export const CAPTION_STYLES = {
  bold: {
    // 15 chars, not more: uppercase Arial Black is ~51px per character at
    // 72px, so 15 characters is about as wide as the safe area allows on one
    // line (an 18-char limit wrapped "IF YOU'RE STUCK AT" onto two lines).
    // 60px since the storyboard videos (2026-10-11): the picture carries the point, the
    // caption supports it - at 72px it competed with the drawings. ~42px per uppercase
    // character at 60px, so 18 characters still fit one line.
    chunking: { maxWordsPerChunk: 4, maxCharsPerChunk: 18 },
    fontFamily: "Arial Black, Arial, sans-serif",
    fontSize: 60,
    fontWeight: 900,
    textTransform: "uppercase",
    color: "#FFFFFF",
    emphasisColor: "#FFD60A",
    outlineColor: "#000000",
    outlineWidth: 5,
    boxColor: null,
  },
  boxed: {
    // Mixed-case Arial at 64px is narrower than bold's uppercase Arial Black,
    // but the box's own padding eats width too: 24 chars wrapped onto a
    // second line, 20 fits on one.
    chunking: { maxWordsPerChunk: 5, maxCharsPerChunk: 20 },
    fontFamily: "Arial, sans-serif",
    fontSize: 64,
    fontWeight: 700,
    textTransform: "none",
    color: "#FFFFFF",
    emphasisColor: "#FFD60A",
    outlineColor: "#000000",
    outlineWidth: 0,
    boxColor: "rgba(28, 25, 23, 0.85)",
  },
} as const satisfies Record<string, CaptionStyle>;

export type CaptionStyleId = keyof typeof CAPTION_STYLES;

export const DEFAULT_CAPTION_STYLE: CaptionStyleId = "bold";

function isCaptionStyleId(value: string): value is CaptionStyleId {
  return Object.prototype.hasOwnProperty.call(CAPTION_STYLES, value);
}

// `captionStyle` is a free-form nullable string in the backend's VideoPlan, so
// an unknown or missing value falls back to the default instead of failing
// the whole render - captions are mandatory, the exact style isn't.
export function resolveCaptionStyle(id: string | null): CaptionStyle {
  return id && isCaptionStyleId(id) ? CAPTION_STYLES[id] : CAPTION_STYLES[DEFAULT_CAPTION_STYLE];
}
