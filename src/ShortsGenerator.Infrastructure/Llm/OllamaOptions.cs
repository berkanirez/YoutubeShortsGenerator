namespace ShortsGenerator.Infrastructure.Llm;

public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; set; } = "http://localhost:11434";

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
}
