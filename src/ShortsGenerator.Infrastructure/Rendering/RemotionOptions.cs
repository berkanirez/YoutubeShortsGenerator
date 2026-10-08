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
}
