namespace ShortsGenerator.Domain;

public sealed record ValidationResult(bool IsValid, IReadOnlyList<string> Reasons);
