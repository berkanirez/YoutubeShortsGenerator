// On-screen text is a slogan painted on the gym wall, not a sentence: the captions already
// carry every word of the narration, so a longer text is simply not painted (null) and the
// visual gets the screen. Pure functions - deterministic per frame, as Remotion requires.
export const MAX_WALL_WORDS = 4;

export function wallText(text: string | null | undefined, maxWords = MAX_WALL_WORDS): string | null {
  const trimmed = text?.trim();
  if (!trimmed) {
    return null;
  }
  return trimmed.split(/\s+/).length <= maxWords ? trimmed : null;
}

// Which of `count` items is being talked about at `frame`: items take turns on the wall
// (one slogan at a time) instead of stacking into a paragraph.
export function currentItem(frame: number, starts: readonly number[]): number {
  let current = 0;
  starts.forEach((start, index) => {
    if (frame >= start) {
      current = index;
    }
  });
  return current;
}
