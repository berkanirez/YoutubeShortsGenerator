namespace ShortsGenerator.Infrastructure.Llm;

public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    // 127.0.0.1, not "localhost" (Day 27, measured): Ollama listens on IPv4 only, and .NET on
    // Windows tries ::1 first; the refused IPv6 attempt cost ~2s on every new connection, i.e.
    // at the start of every video.
    public string BaseUrl { get; set; } = "http://127.0.0.1:11434";

    public string Model { get; set; } = "llama3.2:3b";

    // Ollama's own default generation length cap is too short for a multi-scene JSON plan
    // (Day 5) and truncates it mid-object. A narration-only prompt (Day 4) finishes well
    // under this long before hitting it, so one generous ceiling is safe for every call.
    public int MaxOutputTokens { get; set; } = 2048;

    // Day 26: transient failures only (connection refused - e.g. the Ollama app is still
    // starting - and 503 busy) are tried again. A missing model, an out-of-memory 500 or a
    // 2-minute timeout would fail the same way again, so they aren't.
    public int MaxAttempts { get; set; } = 2;

    public double RetryDelaySeconds { get; set; } = 3;

    // Day 27: how long Ollama keeps the model in memory after a call (Ollama's own default is
    // 5 minutes). Measured: loading llama3.2:3b costs ~6.6s at the start of a video made more
    // than 5 minutes after the previous one. 15 minutes covers a normal working session at
    // the price of ~2.5 GB of RAM staying in use meanwhile. "0" unloads right after each call.
    public string KeepAlive { get; set; } = "15m";
}
