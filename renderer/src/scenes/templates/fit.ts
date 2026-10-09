// Day 29: "OVERTRAINING" and "INSUFFICIENT" ran out of their 430px Comparison cards at the
// fixed 62px headline size and into the neighbouring card. Words never break mid-word, so the
// longest word decides: the font shrinks until it fits, down to a still-readable minimum.
// A pure function of the text (deterministic per frame, as Remotion requires).

// Measured on the uppercase headline font: the clipped word showed ~0.54em per character;
// 0.6 leaves a margin for wide letters (M, W).
const EM_PER_CHAR = 0.6;

// How many lines `text` wraps to at `size` in `widthPx` (greedy word wrap, the same estimate).
// Line wrapping needs more margin than a single word: whole words move to the next line, so a
// line is rarely full. 0.6 predicted 3 lines for headlines that rendered in 4 (final Day 29
// videos: a 4-line Recovery title pushed its chips onto the captions).
const LINE_EM_PER_CHAR = 0.7;

export function estimateLines(text: string, widthPx: number, size: number): number {
  const perLine = Math.max(1, Math.floor(widthPx / (size * LINE_EM_PER_CHAR)));
  let lines = 1;
  let used = 0;
  for (const word of text.split(/\s+/).filter(Boolean)) {
    const needed = used === 0 ? word.length : used + 1 + word.length;
    if (needed <= perLine || used === 0) {
      used = needed;
    } else {
      lines++;
      used = word.length;
    }
  }
  return lines;
}

// Day 29, second round: the model ignores "max 5 words" now and then - a 10-word Hook at
// 124px ran over the mascot and the captions, a 12-word Progress label off its card. The
// block shrinks until it fits in `maxLines` lines (and its longest word fits), never below
// `minSize`; short texts keep their size. Cheaper and more reliable than asking the model again.
export function fitBlockFontSize(
  text: string, widthPx: number, baseSize: number, minSize: number, maxLines: number,
): number {
  let size = fitFontSize(text, widthPx, baseSize, minSize);
  while (size > minSize && estimateLines(text, widthPx, size) > maxLines) {
    size -= 2;
  }
  return Math.max(minSize, size);
}

export function fitFontSize(text: string, availableWidthPx: number, baseSize: number, minSize: number): number {
  const longestWord = Math.max(0, ...text.split(/\s+/).map((word) => word.length));
  if (longestWord === 0) {
    return baseSize;
  }

  const fitting = Math.floor(availableWidthPx / (longestWord * EM_PER_CHAR));
  return Math.max(minSize, Math.min(baseSize, fitting));
}
