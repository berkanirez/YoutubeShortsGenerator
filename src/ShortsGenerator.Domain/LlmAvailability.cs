namespace ShortsGenerator.Domain;

public sealed record LlmAvailability(bool IsReachable, bool IsModelAvailable, string Detail);
