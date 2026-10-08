using ShortsGenerator.Domain;

namespace ShortsGenerator.Application.Tests;

internal sealed class FakeLlmProvider : ILlmProvider
{
    private readonly Queue<object> _responses;

    public FakeLlmProvider(params object[] responses)
    {
        _responses = new Queue<object>(responses);
    }

    public List<string> ReceivedPrompts { get; } = [];
    public List<string> ReceivedSchemas { get; } = [];

    public Task<string> GenerateJsonAsync(string prompt, string jsonSchema, CancellationToken cancellationToken)
    {
        ReceivedSchemas.Add(jsonSchema);
        return GenerateAsync(prompt, cancellationToken);
    }

    public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken)
    {
        ReceivedPrompts.Add(prompt);

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("No more fake responses queued.");
        }

        return _responses.Dequeue() switch
        {
            string text => Task.FromResult(text),
            Exception ex => Task.FromException<string>(ex),
            var other => throw new InvalidOperationException($"Unsupported fake response type: {other.GetType()}."),
        };
    }

    public Task<LlmAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new LlmAvailability(true, true, "fake"));
}
