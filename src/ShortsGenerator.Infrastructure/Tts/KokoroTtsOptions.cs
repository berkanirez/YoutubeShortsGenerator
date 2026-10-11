using ShortsGenerator.Domain;

namespace ShortsGenerator.Infrastructure.Tts;

public sealed class KokoroTtsOptions
{
    public const string SectionName = "KokoroTts";

    // All paths are relative to the Api project's content root (see ContentRootPath) and
    // point at what tools/tts/setup.ps1 creates.
    public string PythonPath { get; set; } = "../../tools/tts/.venv/Scripts/python.exe";
    public string ScriptPath { get; set; } = "../../tools/tts/kokoro_tts.py";
    public string ModelPath { get; set; } = "../../tools/tts/models/kokoro-v1.0.onnx";
    public string VoicesPath { get; set; } = "../../tools/tts/models/voices-v1.0.bin";

    // af_heart is Kokoro's highest-rated English voice. Any Kokoro voice id works
    // (am_michael, bf_emma, ...); it's never a clone of a real person (CLAUDE.md section 9).
    public string Voice { get; set; } = "af_heart";
    public double Speed { get; set; } = 1.0;
    public string Language { get; set; } = "en-us";

    // Silence after every scene's sentence(s) - a natural breath between scenes, and the
    // moment the cut to the next scene happens in.
    public double PauseSeconds { get; set; } = 0.3;

    // Human-like delivery (SpeechDelivery): a beat after the hook, "Number two." on its own,
    // varied breaths between scenes, the key phrase emphasized. False = the old flat reading.
    public bool Expressive { get; set; } = true;

    // How the key phrase is emphasized: Pause (a beat before it), Stress (slower and louder,
    // spoken as its own part), or None.
    public EmphasisStyle Emphasis { get; set; } = EmphasisStyle.Pause;

    // Includes model load (~5-10s) plus CPU synthesis of a ~30-45s Short.
    public int TimeoutSeconds { get; set; } = 300;
}
