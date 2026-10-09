namespace ShortsGenerator.Application.Tests;

// Records what was queued; can be told to be full. Nothing is ever dequeued.
internal sealed class RecordingQueue : IVideoJobQueue
{
    public List<VideoJobWorkItem> Items { get; } = [];

    public bool IsFull { get; set; }

    // Called inside TryEnqueue, i.e. at the moment the worker could pick the item up.
    public Action<VideoJobWorkItem>? OnEnqueue { get; set; }

    public bool TryEnqueue(VideoJobWorkItem item)
    {
        if (IsFull || IsQueued(item.JobId))
        {
            return false;
        }

        OnEnqueue?.Invoke(item);
        Items.Add(item);
        return true;
    }

    public ValueTask<VideoJobWorkItem> DequeueAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public bool IsQueued(Guid jobId) => Items.Any(i => i.JobId == jobId);
}
