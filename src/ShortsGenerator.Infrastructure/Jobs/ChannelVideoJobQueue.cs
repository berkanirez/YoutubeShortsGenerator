using System.Collections.Concurrent;
using System.Threading.Channels;
using ShortsGenerator.Application;

namespace ShortsGenerator.Infrastructure.Jobs;

public sealed class ChannelVideoJobQueue : IVideoJobQueue
{
    private readonly Channel<VideoJobWorkItem> _channel;

    // Job ids currently waiting (not yet picked up). Prevents the same job being queued
    // twice by a double-click - two workers' worth of runs on one job would race each other
    // on the same files and status.
    private readonly ConcurrentDictionary<Guid, byte> _waiting = new();

    public ChannelVideoJobQueue(int capacity = 100)
    {
        // Bounded: a runaway client can't grow memory without limit. SingleReader matches
        // the one-worker design and lets the channel use a cheaper implementation.
        _channel = Channel.CreateBounded<VideoJobWorkItem>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    public bool TryEnqueue(VideoJobWorkItem item)
    {
        if (!_waiting.TryAdd(item.JobId, 0))
        {
            return false;
        }

        // TryWrite never waits: with FullMode.Wait it simply returns false when full,
        // so an HTTP request is never blocked behind the queue.
        if (_channel.Writer.TryWrite(item))
        {
            return true;
        }

        _waiting.TryRemove(item.JobId, out _);
        return false;
    }

    public async ValueTask<VideoJobWorkItem> DequeueAsync(CancellationToken cancellationToken)
    {
        var item = await _channel.Reader.ReadAsync(cancellationToken);
        _waiting.TryRemove(item.JobId, out _);
        return item;
    }

    public bool IsQueued(Guid jobId) => _waiting.ContainsKey(jobId);
}
