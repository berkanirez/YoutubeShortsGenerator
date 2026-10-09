using ShortsGenerator.Domain;

namespace ShortsGenerator.Domain.Tests;

public class VideoJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_WithValidTopic_StartsInCreatedStatus()
    {
        var job = VideoJob.Create("Why Your Pull-Ups Aren't Increasing", null, Now);

        Assert.Equal(VideoJobStatus.Created, job.Status);
        Assert.Equal("Why Your Pull-Ups Aren't Increasing", job.Topic);
        Assert.Equal(Now, job.CreatedAt);
        Assert.Equal(Now, job.UpdatedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithoutTopic_Throws(string? topic)
    {
        Assert.Throws<ArgumentException>(() => VideoJob.Create(topic!, null, Now));
    }

    [Fact]
    public void Create_TopicLongerThanTheLimit_Throws_ButTheLimitItselfIsFine()
    {
        Assert.Throws<ArgumentException>(() => VideoJob.Create(new string('a', VideoJob.MaxTopicLength + 1), null, Now));
        Assert.Equal(VideoJob.MaxTopicLength, VideoJob.Create(new string('a', VideoJob.MaxTopicLength), null, Now).Topic.Length);
    }

    public enum Transition { Planning, Narrating, Rendering, Completed, Failed }

    // The whole state machine in one table: every (from, transition) pair, not just the ones
    // someone thought of. A new allowed edge has to be added here on purpose.
    private static readonly HashSet<(VideoJobStatus From, Transition To)> Allowed =
    [
        (VideoJobStatus.Created, Transition.Planning),
        (VideoJobStatus.Planning, Transition.Narrating),
        (VideoJobStatus.Narrating, Transition.Rendering),
        (VideoJobStatus.Rendering, Transition.Completed),
        (VideoJobStatus.Completed, Transition.Rendering),   // re-render (Day 13)
        (VideoJobStatus.Completed, Transition.Narrating),   // narration edit (Day 23)
        (VideoJobStatus.Created, Transition.Failed),
        (VideoJobStatus.Planning, Transition.Failed),
        (VideoJobStatus.Narrating, Transition.Failed),
        (VideoJobStatus.Rendering, Transition.Failed),
    ];

    public static TheoryData<VideoJobStatus, Transition> EveryPair()
    {
        var data = new TheoryData<VideoJobStatus, Transition>();
        foreach (var from in Enum.GetValues<VideoJobStatus>())
        {
            foreach (var to in Enum.GetValues<Transition>())
            {
                data.Add(from, to);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryPair))]
    public void StateMachine_AllowsExactlyTheDocumentedTransitions(VideoJobStatus from, Transition to)
    {
        var job = JobIn(from);
        var statusBefore = job.Status;

        var act = () => Apply(job, to);

        if (Allowed.Contains((from, to)))
        {
            act();
            Assert.Equal(Enum.Parse<VideoJobStatus>(to.ToString()), job.Status);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(act);
            Assert.Equal(statusBefore, job.Status);   // a refused transition changes nothing
        }
    }

    private static VideoJob JobIn(VideoJobStatus status)
    {
        var job = VideoJob.Create("Topic", null, Now);
        var path = status switch
        {
            VideoJobStatus.Created => Array.Empty<Transition>(),
            VideoJobStatus.Planning => [Transition.Planning],
            VideoJobStatus.Narrating => [Transition.Planning, Transition.Narrating],
            VideoJobStatus.Rendering => [Transition.Planning, Transition.Narrating, Transition.Rendering],
            VideoJobStatus.Completed => [Transition.Planning, Transition.Narrating, Transition.Rendering, Transition.Completed],
            VideoJobStatus.Failed => [Transition.Failed],
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Add the new status to this test."),
        };
        foreach (var step in path)
        {
            Apply(job, step);
        }

        Assert.Equal(status, job.Status);
        return job;
    }

    private static void Apply(VideoJob job, Transition to)
    {
        switch (to)
        {
            case Transition.Planning: job.MarkPlanning(Now); break;
            case Transition.Narrating: job.MarkNarrating(Now); break;
            case Transition.Rendering: job.MarkRendering(Now); break;
            case Transition.Completed: job.MarkCompleted("final.mp4", Now); break;
            case Transition.Failed: job.MarkFailed("boom", Now); break;
        }
    }

    [Fact]
    public void RecoverAfterInterruption_ARenderStartsOverFromNarrating_AndIsCounted()
    {
        var job = JobIn(VideoJobStatus.Rendering);
        job.MarkQueued(VideoJobWorkKind.Render, Now);

        Assert.True(job.RecoverAfterInterruption(Now));

        Assert.Equal(VideoJobStatus.Narrating, job.Status);
        Assert.Equal(1, job.InterruptionCount);
        Assert.Equal(VideoJobWorkKind.Render, job.PendingWork);
    }

    [Fact]
    public void RecoverAfterInterruption_GivesUpAfterTheLimit_AndFinishingWorkResetsTheCount()
    {
        var job = JobIn(VideoJobStatus.Planning);
        job.MarkQueued(VideoJobWorkKind.Generate, Now);
        Assert.True(job.RecoverAfterInterruption(Now));
        job.MarkWorkFinished(Now);               // it got through once: the count starts over
        job.MarkQueued(VideoJobWorkKind.Generate, Now);

        for (var i = 0; i < VideoJob.MaxInterruptions; i++)
        {
            Assert.True(job.RecoverAfterInterruption(Now));
        }

        Assert.False(job.RecoverAfterInterruption(Now));
        Assert.Equal(VideoJobStatus.Failed, job.Status);
        Assert.Null(job.PendingWork);
    }

    [Fact]
    public void RecoverAfterInterruption_ACompletedJobThatKeepsCrashing_StaysCompleted()
    {
        var job = JobIn(VideoJobStatus.Completed);
        job.MarkQueued(VideoJobWorkKind.Render, Now);
        for (var i = 0; i < VideoJob.MaxInterruptions; i++)
        {
            job.RecoverAfterInterruption(Now);
        }

        Assert.False(job.RecoverAfterInterruption(Now));
        Assert.Equal(VideoJobStatus.Completed, job.Status);   // its earlier video is still fine
        Assert.Null(job.PendingWork);
    }

    [Fact]
    public void RecoverAfterInterruption_WithoutPendingWork_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => JobIn(VideoJobStatus.Planning).RecoverAfterInterruption(Now));
    }

    [Theory]
    [InlineData(VideoJobStatus.Created)]
    [InlineData(VideoJobStatus.Planning)]
    [InlineData(VideoJobStatus.Narrating)]
    public void RetryFrom_PutsAFailedJobBack_AndClearsTheError(VideoJobStatus resumeAt)
    {
        var job = JobIn(VideoJobStatus.Failed);

        job.RetryFrom(resumeAt, Now);

        Assert.Equal(resumeAt, job.Status);
        Assert.Null(job.ErrorMessage);
    }

    [Fact]
    public void RetryFrom_OnlyFromFailed_AndNeverStraightToRenderingOrCompleted()
    {
        Assert.Throws<InvalidOperationException>(() => JobIn(VideoJobStatus.Completed).RetryFrom(VideoJobStatus.Narrating, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => JobIn(VideoJobStatus.Failed).RetryFrom(VideoJobStatus.Completed, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => JobIn(VideoJobStatus.Failed).RetryFrom(VideoJobStatus.Rendering, Now));
    }

    [Fact]
    public void HappyPath_MovesThroughAllStatusesInOrder()
    {
        var job = VideoJob.Create("Topic", null, Now);

        job.MarkPlanning(Now);
        Assert.Equal(VideoJobStatus.Planning, job.Status);

        job.AttachGeneratedScript("generated script", Now);
        Assert.Equal(VideoJobStatus.Planning, job.Status);
        Assert.Equal("generated script", job.GeneratedScript);

        job.MarkNarrating(Now);
        Assert.Equal(VideoJobStatus.Narrating, job.Status);

        job.MarkRendering(Now);
        Assert.Equal(VideoJobStatus.Rendering, job.Status);

        job.MarkCompleted("output/job/final.mp4", Now);
        Assert.Equal(VideoJobStatus.Completed, job.Status);
        Assert.Equal("output/job/final.mp4", job.FinalVideoPath);
    }

    [Fact]
    public void MarkRendering_FromCompleted_AllowsReRenderAndKeepsPreviousVideoUntilDone()
    {
        var job = VideoJob.Create("Topic", null, Now);
        job.MarkPlanning(Now);
        job.AttachGeneratedScript("script", Now);
        job.MarkNarrating(Now);
        job.MarkRendering(Now);
        job.MarkCompleted("first.mp4", Now);

        job.MarkRendering(Now);
        Assert.Equal(VideoJobStatus.Rendering, job.Status);
        Assert.Equal("first.mp4", job.FinalVideoPath);

        job.MarkCompleted("second.mp4", Now);
        Assert.Equal("second.mp4", job.FinalVideoPath);
    }

    [Fact]
    public void MarkRendering_FromCreated_Throws()
    {
        var job = VideoJob.Create("Topic", null, Now);

        Assert.Throws<InvalidOperationException>(() => job.MarkRendering(Now));
    }

    [Fact]
    public void MarkNarrating_FromCompleted_AllowsRegeneratingTheVoiceOver()
    {
        var job = VideoJob.Create("Topic", null, Now);
        job.MarkPlanning(Now);
        job.AttachGeneratedScript("script", Now);
        job.MarkNarrating(Now);
        job.MarkRendering(Now);
        job.MarkCompleted("final.mp4", Now);

        job.MarkNarrating(Now);

        Assert.Equal(VideoJobStatus.Narrating, job.Status);
        Assert.Equal("final.mp4", job.FinalVideoPath);
    }

    [Fact]
    public void MarkCompleted_FromNarrating_Throws()
    {
        var job = VideoJob.Create("Topic", null, Now);
        job.MarkPlanning(Now);
        job.AttachGeneratedScript("script", Now);
        job.MarkNarrating(Now);

        Assert.Throws<InvalidOperationException>(() => job.MarkCompleted("path.mp4", Now));
    }

    [Fact]
    public void AttachGeneratedScript_WhileNotPlanning_Throws()
    {
        var job = VideoJob.Create("Topic", null, Now);

        Assert.Throws<InvalidOperationException>(() => job.AttachGeneratedScript("script", Now));
    }

    [Fact]
    public void AttachGeneratedScript_WithoutText_Throws()
    {
        var job = VideoJob.Create("Topic", null, Now);
        job.MarkPlanning(Now);

        Assert.Throws<ArgumentException>(() => job.AttachGeneratedScript("   ", Now));
    }

    [Fact]
    public void MarkNarrating_WithoutScriptAttached_StillSucceeds_StatusOnlyTransition()
    {
        // MarkNarrating is a pure status transition - CLAUDE.md's pipeline doesn't require
        // a script to exist before Planning -> Narrating at the domain level; the
        // application layer (ScenePlanningService/ScriptGenerationService) is responsible
        // for only calling it once a script and plan actually exist.
        var job = VideoJob.Create("Topic", null, Now);
        job.MarkPlanning(Now);

        job.MarkNarrating(Now);

        Assert.Equal(VideoJobStatus.Narrating, job.Status);
    }

    [Fact]
    public void SetOutputDirectory_WithoutValue_Throws()
    {
        var job = VideoJob.Create("Topic", null, Now);

        Assert.Throws<ArgumentException>(() => job.SetOutputDirectory("  ", Now));
    }

    [Fact]
    public void SetOutputDirectory_WithValue_SetsItWithoutChangingStatus()
    {
        var job = VideoJob.Create("Topic", null, Now);

        job.SetOutputDirectory("output/123", Now);

        Assert.Equal("output/123", job.OutputDirectory);
        Assert.Equal(VideoJobStatus.Created, job.Status);
    }

    [Theory]
    [InlineData(VideoJobStatus.Created)]
    [InlineData(VideoJobStatus.Planning)]
    [InlineData(VideoJobStatus.Narrating)]
    [InlineData(VideoJobStatus.Rendering)]
    public void MarkFailed_FromAnyNonTerminalStatus_Succeeds(VideoJobStatus startStatus)
    {
        var job = VideoJob.Create("Topic", null, Now);
        AdvanceTo(job, startStatus);

        job.MarkFailed("Ollama unavailable", Now);

        Assert.Equal(VideoJobStatus.Failed, job.Status);
        Assert.Equal("Ollama unavailable", job.ErrorMessage);
    }

    [Fact]
    public void MarkFailed_FromCompleted_Throws()
    {
        var job = VideoJob.Create("Topic", null, Now);
        job.MarkPlanning(Now);
        job.AttachGeneratedScript("script", Now);
        job.MarkNarrating(Now);
        job.MarkRendering(Now);
        job.MarkCompleted("path.mp4", Now);

        Assert.Throws<InvalidOperationException>(() => job.MarkFailed("too late", Now));
    }

    [Fact]
    public void MarkFailed_FromFailed_Throws()
    {
        var job = VideoJob.Create("Topic", null, Now);
        job.MarkFailed("first failure", Now);

        Assert.Throws<InvalidOperationException>(() => job.MarkFailed("second failure", Now));
    }

    private static void AdvanceTo(VideoJob job, VideoJobStatus status)
    {
        if (status is VideoJobStatus.Created)
        {
            return;
        }

        job.MarkPlanning(Now);
        if (status is VideoJobStatus.Planning)
        {
            return;
        }

        job.AttachGeneratedScript("script", Now);
        job.MarkNarrating(Now);
        if (status is VideoJobStatus.Narrating)
        {
            return;
        }

        job.MarkRendering(Now);
    }
}
