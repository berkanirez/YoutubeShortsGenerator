using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ShortsGenerator.Application;
using ShortsGenerator.Domain;
using ShortsGenerator.Infrastructure.Jobs;

namespace ShortsGenerator.Infrastructure.Tests;

public class VideoJobQueueAndWorkerTests
{
    [Fact]
    public async Task Queue_IsFifo_AndTracksWhatIsWaiting()
    {
        var queue = new ChannelVideoJobQueue();
        var first = Item();
        var second = Item();

        Assert.True(queue.TryEnqueue(first));
        Assert.True(queue.TryEnqueue(second));
        Assert.True(queue.IsQueued(first.JobId));

        Assert.Equal(first, await queue.DequeueAsync(CancellationToken.None));
        Assert.False(queue.IsQueued(first.JobId));
        Assert.Equal(second, await queue.DequeueAsync(CancellationToken.None));
    }

    [Fact]
    public void Queue_RejectsTheSameJobTwiceWhileItIsWaiting()
    {
        var queue = new ChannelVideoJobQueue();
        var item = Item();

        Assert.True(queue.TryEnqueue(item));
        Assert.False(queue.TryEnqueue(item with { Kind = VideoJobWorkKind.Render }));
    }

    [Fact]
    public async Task Queue_SameJobCanBeQueuedAgainOnceItWasPickedUp()
    {
        var queue = new ChannelVideoJobQueue();
        var item = Item();
        queue.TryEnqueue(item);
        await queue.DequeueAsync(CancellationToken.None);

        Assert.True(queue.TryEnqueue(item));
    }

    [Fact]
    public void Queue_WhenFull_ReturnsFalseImmediately_AndForgetsTheRejectedJob()
    {
        var queue = new ChannelVideoJobQueue(capacity: 1);
        queue.TryEnqueue(Item());
        var rejected = Item();

        Assert.False(queue.TryEnqueue(rejected));
        Assert.False(queue.IsQueued(rejected.JobId));
    }

    [Fact]
    public async Task Worker_ProcessesJobsOneAtATime_InOrder()
    {
        var queue = new ChannelVideoJobQueue();
        var processor = new RecordingProcessor(delay: TimeSpan.FromMilliseconds(50));
        using var worker = CreateWorker(queue, processor);
        var items = Enumerable.Range(0, 4).Select(_ => Item()).ToList();

        await worker.StartAsync(CancellationToken.None);
        items.ForEach(item => queue.TryEnqueue(item));
        await processor.WaitForAsync(4);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(items.Select(i => i.JobId), processor.Processed);
        Assert.Equal(1, processor.MaxConcurrency);
    }

    [Fact]
    public async Task Worker_KeepsRunningAfterAJobThrows()
    {
        var queue = new ChannelVideoJobQueue();
        var poisoned = Item();
        var processor = new RecordingProcessor(throwFor: poisoned.JobId);
        using var worker = CreateWorker(queue, processor);

        await worker.StartAsync(CancellationToken.None);
        queue.TryEnqueue(poisoned);
        var healthy = Item();
        queue.TryEnqueue(healthy);
        await processor.WaitForAsync(2);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal([poisoned.JobId, healthy.JobId], processor.Processed);
    }

    [Fact]
    public async Task Worker_UsesAFreshDiScopePerJob()
    {
        var queue = new ChannelVideoJobQueue();
        var processor = new RecordingProcessor();
        var scopes = new List<IServiceProvider>();
        var services = new ServiceCollection();
        services.AddScoped<IVideoJobProcessor>(provider =>
        {
            scopes.Add(provider);
            return processor;
        });
        using var provider = services.BuildServiceProvider();
        using var worker = new VideoJobWorker(queue, provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<VideoJobWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        queue.TryEnqueue(Item());
        queue.TryEnqueue(Item());
        await processor.WaitForAsync(2);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(2, scopes.Count);
        Assert.NotSame(scopes[0], scopes[1]);
    }

    private static VideoJobWorkItem Item() => new(Guid.NewGuid(), VideoJobWorkKind.Generate);

    private static VideoJobWorker CreateWorker(IVideoJobQueue queue, IVideoJobProcessor processor)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => processor);
        var provider = services.BuildServiceProvider();
        return new VideoJobWorker(queue, provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<VideoJobWorker>.Instance);
    }

    private sealed class RecordingProcessor : IVideoJobProcessor
    {
        private readonly TimeSpan _delay;
        private readonly Guid? _throwFor;
        private readonly object _lock = new();
        private int _running;

        public RecordingProcessor(TimeSpan? delay = null, Guid? throwFor = null)
        {
            _delay = delay ?? TimeSpan.Zero;
            _throwFor = throwFor;
        }

        public List<Guid> Processed { get; } = [];
        public int MaxConcurrency { get; private set; }

        public async Task ProcessAsync(VideoJobWorkItem item, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                _running++;
                MaxConcurrency = Math.Max(MaxConcurrency, _running);
            }

            try
            {
                await Task.Delay(_delay, cancellationToken);
                lock (_lock)
                {
                    Processed.Add(item.JobId);
                }

                if (item.JobId == _throwFor)
                {
                    throw new InvalidOperationException("poisoned job");
                }
            }
            finally
            {
                lock (_lock)
                {
                    _running--;
                }
            }
        }

        public async Task WaitForAsync(int count)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                lock (_lock)
                {
                    if (Processed.Count >= count)
                    {
                        return;
                    }
                }

                await Task.Delay(10);
            }

            throw new TimeoutException($"Only {Processed.Count} of {count} jobs were processed.");
        }
    }
}
