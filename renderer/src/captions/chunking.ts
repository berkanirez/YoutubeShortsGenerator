export type CaptionWord = {
  text: string;
  emphasized: boolean;
};

export type ChunkingRules = {
  maxWordsPerChunk: number;
  maxCharsPerChunk: number;
};

// A word ending in one of these closes a clause - the natural place for a
// caption to change, because that's where a speaker pauses.
export const CLAUSE_END = /[.,;:!?—–]$/;

export function splitWords(narration: string): string[] {
  return narration.trim().split(/\s+/).filter((word) => word.length > 0);
}

// Lowercases and strips leading/trailing punctuation, but keeps internal
// apostrophes and hyphens: "Lats," -> "lats", "you're" -> "you're",
// "pull-up" -> "pull-up".
export function normalizeWord(word: string): string {
  return word.toLowerCase().replace(/^[^a-z0-9]+|[^a-z0-9]+$/g, "");
}

// Marks the first occurrence of the scene's `emphasis` phrase in its words.
// Works on the whole scene's word list *before* chunking, so a phrase that
// ends up split across two captions is still highlighted in both. An
// emphasis phrase that doesn't actually appear in the narration is ignored
// rather than failing the render - it's a cosmetic hint, not structure.
export function markEmphasis(words: readonly string[], emphasis: string | null): CaptionWord[] {
  const result = words.map((text) => ({ text, emphasized: false }));
  if (!emphasis) {
    return result;
  }

  const target = splitWords(emphasis).map(normalizeWord).filter((word) => word.length > 0);
  if (target.length === 0) {
    return result;
  }

  const normalized = words.map(normalizeWord);
  for (let start = 0; start + target.length <= normalized.length; start++) {
    if (target.every((word, offset) => normalized[start + offset] === word)) {
      for (let offset = 0; offset < target.length; offset++) {
        result[start + offset] = { ...result[start + offset], emphasized: true };
      }
      break;
    }
  }

  return result;
}

export function chunkLength(chunk: readonly CaptionWord[]): number {
  return chunk.reduce((length, word, index) => length + word.text.length + (index > 0 ? 1 : 0), 0);
}

// Greedy chunking with three readability rules (CLAUDE.md section 12: short
// chunks, never paragraph subtitles):
//   1. never more than maxWordsPerChunk words or maxCharsPerChunk characters,
//      so a caption fits on one line at mobile font size;
//   2. always break after a clause-ending punctuation mark;
//   3. don't leave a single "orphan" word as the scene's last caption if it
//      fits on the previous line instead.
export function chunkWords(words: readonly CaptionWord[], rules: ChunkingRules): CaptionWord[][] {
  const chunks: CaptionWord[][] = [];
  let current: CaptionWord[] = [];
  let currentLength = 0;

  const flush = () => {
    if (current.length > 0) {
      chunks.push(current);
      current = [];
      currentLength = 0;
    }
  };

  for (const word of words) {
    const lengthIfAdded =
      current.length === 0 ? word.text.length : currentLength + 1 + word.text.length;
    if (
      current.length > 0 &&
      (current.length >= rules.maxWordsPerChunk || lengthIfAdded > rules.maxCharsPerChunk)
    ) {
      flush();
    }

    currentLength = current.length === 0 ? word.text.length : currentLength + 1 + word.text.length;
    current.push(word);

    if (CLAUSE_END.test(word.text)) {
      flush();
    }
  }
  flush();

  if (chunks.length >= 2) {
    const last = chunks[chunks.length - 1];
    const previous = chunks[chunks.length - 2];
    const previousEndsClause = CLAUSE_END.test(previous[previous.length - 1].text);
    const mergedLength = chunkLength(previous) + 1 + chunkLength(last);
    if (last.length === 1 && !previousEndsClause && mergedLength <= rules.maxCharsPerChunk) {
      chunks.splice(chunks.length - 2, 2, [...previous, ...last]);
    }
  }

  return chunks;
}
