namespace ShortsGenerator.Infrastructure.Art;

// Where the hand-made art comes in and which local tools turn it into catalog art.
// Relative paths are resolved against the Api project's content root (ContentRootPath).
public sealed class ArtImportOptions
{
    public const string SectionName = "ArtImport";

    // The subjects that can be asked for (assets/manifests/subjects.json).
    public string SubjectsPath { get; set; } = "../../assets/manifests/subjects.json";

    // Where the owner's generated images land (upload or by hand); originals stay here.
    public string IncomingDirectory { get; set; } = "../../assets/incoming";

    // Intermediates (4x upscale, prepared PNGs) - reproducible, gitignored.
    public string WorkDirectory { get; set; } = "../../tools/images/.cache/import";

    public string PythonPath { get; set; } = "../../tools/images/.venv/Scripts/python.exe";

    public string PrepareScriptPath { get; set; } = "../../tools/images/prepare_art.py";

    // Real-ESRGAN (docs/ASSETS.md). Optional: without it the art is imported at its own size.
    public string UpscalerPath { get; set; } = "../../tools/images/realesrgan/realesrgan-ncnn-vulkan.exe";

    public string UpscalerModel { get; set; } = "realesrgan-x4plus-anime";

    // Longer side of the catalog PNG (the video shows a mascot up to ~1450 px tall).
    public int Size { get; set; } = 2048;

    public int TimeoutSeconds { get; set; } = 300;

    public long MaxUploadBytes { get; set; } = 25 * 1024 * 1024;
}
