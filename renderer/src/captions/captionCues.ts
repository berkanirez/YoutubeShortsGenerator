import {
  CLAUSE_END,
  chunkWords,
  markEmphasis,
  splitWords,
  type CaptionWord,
  type ChunkingRules,
} from "./chunking";

// One caption on screen: which words, and the scene-local frame range
// [startFrame, endFrame) it is visible for.
export type CaptionCue = {
  startFrame: number;
  endFrame: number;
  words: CaptionWord[];
};

// A clause-ending chunk gets a little extra time, because speakers pause at
// punctuation - roughly the time of a few extra letters.
const PAUSE_WEIGHT = 4;

function chunkWeight(chunk: readonly CaptionWord[]): number {
  const letters = chunk.reduce(
    (total, word) => total + word.text.replace(/[^A-Za-z0-9]/g, "").length,
    0,
  );
  const lastWord = chunk[chunk.length - 1].text;
  return Math.max(1, letters) + (CLAUSE_END.test(lastWord) ? PAUSE_WEIGHT : 0);
}

// Phrase-level timing without real audio (CLAUDE.md section 12: reliable
// phrase-level timing first). Until TTS exists (Day 11) and audio timing
// drives scene durations (Day 12), the best available estimate is: a scene's
// narration fills the scene, and each chunk takes time proportional to how
// long it is to say - approximated by its letter count.
//
// Boundaries are rounded from the *cumulative* weight, not per chunk: rounding
// each chunk's own length would let rounding errors pile up, so the last
// caption could end a few frames before or after the scene does. This way the
// first cue always starts at frame 0, the last always ends exactly at
// durationInFrames, and cues never overlap or leave gaps.
//
// spokenFrames (Day 12): when real narration exists, the backend knows how long the
// scene's speech actually lasts. Cues are spread over that span only - not over the
// pause before the next scene - and the last cue then stays up until the scene ends,
// so the screen never goes caption-less mid-scene.
export function buildCaptionCues(
  narration: string | null,
  emphasis: string | null,
  durationInFrames: number,
  rules: ChunkingRules,
  spokenFrames?: number,
): CaptionCue[] {
  if (!narration || durationInFrames <= 0) {
    return [];
  }

  const words = markEmphasis(splitWords(narration), emphasis);
  const chunks = chunkWords(words, rules);
  if (chunks.length === 0) {
    return [];
  }

  const weights = chunks.map(chunkWeight);
  const totalWeight = weights.reduce((total, weight) => total + weight, 0);
  const speechFrames = Math.min(
    durationInFrames,
    spokenFrames !== undefined && spokenFrames > 0 ? spokenFrames : durationInFrames,
  );

  const cues: CaptionCue[] = [];
  let cumulativeWeight = 0;
  let startFrame = 0;
  chunks.forEach((chunk, index) => {
    cumulativeWeight += weights[index];
    const endFrame =
      index === chunks.length - 1
        ? durationInFrames
        : Math.round((speechFrames * cumulativeWeight) / totalWeight);

    // Only possible when a scene has more chunks than frames; skipping a
    // zero-length cue is better than handing <Sequence> an invalid duration.
    if (endFrame > startFrame) {
      cues.push({ startFrame, endFrame, words: chunk });
      startFrame = endFrame;
    }
  });

  return cues;
}
