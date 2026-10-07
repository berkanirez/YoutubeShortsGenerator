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
    public void MarkRendering_FromCreated_Throws()
    {
        var job = VideoJob.Create("Topic", null, Now);

        Assert.Throws<InvalidOperationException>(() => job.MarkRendering(Now));
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
