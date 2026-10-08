namespace ShortsGenerator.Domain;

public interface ILlmProvider
{
    Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken);

    /// <summary>
    /// Generates JSON constrained to <paramref name="jsonSchema"/> (a JSON Schema document) while
    /// it is being generated - not merely asked for in the prompt. The result must still be
    /// validated: a schema can't express every business rule.
    /// </summary>
    Task<string> GenerateJsonAsync(string prompt, string jsonSchema, CancellationToken cancellationToken);

    Task<LlmAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken);
}
