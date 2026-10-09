namespace ShortsGenerator.Infrastructure.Rendering;

public sealed class RemotionOptions
{
    public const string SectionName = "Remotion";

    // "node" = found on PATH. Node is started directly - never npx/npm, which are .cmd
    // scripts on Windows and would put cmd.exe (and its own argument parsing) in between.
    public string NodePath { get; set; } = "node";

    // Relative to the Api project's content root (see ContentRootPath).
    public string RendererDirectory { get; set; } = "../../renderer";

    public string EntryPoint { get; set; } = "src/index.ts";
    public string CompositionId { get; set; } = "ShortComposition";

    // A 30-45s Short renders in roughly 1-2 minutes on a laptop CPU (bundling included);
    // the generous limit only exists to stop a hung Chrome from blocking forever.
    public int TimeoutSeconds { get; set; } = 900;

    // Day 27: how many frames Chrome renders in parallel. Null = Remotion's default (half the
    // logical cores; 6 on the dev laptop). Measured on a 30s Short: 2 -> 33.9s, 4 -> 26.1s,
    // 6 -> 22.7s, 8 -> 22.1s, 12 -> 21.6s. Lower it for a quieter machine, not for speed.
    public int? Concurrency { get; set; }

    // Day 27: re-rendering a video whose plan, narration, renderer code and assets are all
    // unchanged reuses the existing final.mp4 (a render is ~24s of full CPU).
    public bool SkipUnchangedRenders { get; set; } = true;
}
