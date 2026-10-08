// A stable number per plan (FNV-1a over its title + narration): the same plan
// always renders the same way (deterministic renders), different plans spread
// across the variants (CLAUDE.md section 4: varied visual combinations - the
// first second of every video must not look identical).
export function planSeed(title: string, narrationText: string): number {
  let hash = 0x811c9dc5;
  for (const char of `${title}\n${narrationText}`) {
    hash ^= char.codePointAt(0) ?? 0;
    hash = Math.imul(hash, 0x01000193) >>> 0;
  }
  return hash;
}

export function pickVariant(seed: number, count: number): number {
  return seed % count;
}
