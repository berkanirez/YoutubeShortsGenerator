using ShortsGenerator.Domain;

namespace ShortsGenerator.Application;

public sealed record VideoJobWorkItem(Guid JobId, VideoJobWorkKind Kind);

// In-process queue between the HTTP endpoints (producers) and the background worker
// (single consumer). CLAUDE.md section 15: Channel<T> + BackgroundService, no Redis/broker.
public interface IVideoJobQueue
{
    /// <summary>
    /// False when the job is already waiting in the queue, or the queue is full - never blocks
    /// an HTTP request.
    /// </summary>
    bool TryEnqueue(VideoJobWorkItem item);

    ValueTask<VideoJobWorkItem> DequeueAsync(CancellationToken cancellationToken);

    bool IsQueued(Guid jobId);
}

// What the worker does with each dequeued item; an interface so the worker (and the API
// tests) can run without Ollama, Kokoro or Remotion.
public interface IVideoJobProcessor
{
    Task ProcessAsync(VideoJobWorkItem item, CancellationToken cancellationToken);
}
