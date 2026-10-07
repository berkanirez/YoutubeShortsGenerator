namespace ShortsGenerator.Domain;

public interface ILlmProvider
{
    Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken);

    Task<LlmAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken);
}
