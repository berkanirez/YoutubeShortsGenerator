using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShortsGenerator.Domain;
using ShortsGenerator.Infrastructure.Processes;
using ShortsGenerator.Infrastructure.Rendering;

namespace ShortsGenerator.Infrastructure.Tests;

public sealed class RemotionRendererTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "remotion-tests-" + Guid.NewGuid());
    private readonly string _rendererDirectory;
    private readonly string _jobDirectory;
    private readonly RemotionOptions _options;
    private readonly Guid _jobId = Guid.NewGuid();

    public RemotionRendererTests()
    {
        _rendererDirectory = Path.Combine(_root, "renderer");
        _jobDirectory = Path.Combine(_root, "output", _jobId.ToString());
        Directory.CreateDirectory(Path.Combine(_rendererDirectory, "node_modules", "@remotion", "cli"));
        Directory.CreateDirectory(Path.Combine(_rendererDirectory, "src"));
        Directory.CreateDirectory(Path.Combine(_rendererDirectory, "scripts"));
        Directory.CreateDirectory(_jobDirectory);
        File.WriteAllText(Path.Combine(_rendererDirectory, "node_modules", "@remotion", "cli", "remotion-cli.js"), "");
        File.WriteAllText(Path.Combine(_rendererDirectory, "src", "index.ts"), "");
        File.WriteAllText(Path.Combine(_rendererDirectory, "scripts", "sync-public-assets.mjs"), "");
        File.WriteAllText(Path.Combine(_jobDirectory, "plan.json"), """{"title":"x"}""");
        File.WriteAllBytes(Path.Combine(_jobDirectory, "narration.wav"), [1, 2, 3]);

        _options = new RemotionOptions { RendererDirectory = _rendererDirectory };
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private RenderRequest Request => new(
        _jobId,
        Path.Combine(_jobDirectory, "plan.json"),
        Path.Combine(_jobDirectory, "narration.wav"),
        Path.Combine(_jobDirectory, "final.mp4"));

    private string StagingDirectory => Path.Combine(_rendererDirectory, "public", "jobs", _jobId.ToString());

    [Fact]
    public async Task RenderAsync_RunsNodeDirectlyWithArgumentListAndAPropsFile()
    {
        string? propsJson = null;
        var runner = new ScriptedRunner(onRender: spec =>
        {
            propsJson = File.ReadAllText(spec.Arguments[spec.Arguments.ToList().IndexOf("--props") + 1]);
            WriteVideo(spec);
            return Ok();
        });

        await CreateRenderer(runner).RenderAsync(Request, CancellationToken.None);

        var render = runner.RenderCall!;
        Assert.Equal("node", render.FileName);
        Assert.Equal(_rendererDirectory, render.WorkingDirectory);
        Assert.Equal(
            [
                Path.Combine(_rendererDirectory, "node_modules", "@remotion", "cli", "remotion-cli.js"),
                "render", "src/index.ts", "ShortComposition",
                Path.Combine(_jobDirectory, "final.rendering.mp4"),
                "--props", Path.Combine(StagingDirectory, "render-props.json"),
                "--log", "warn",
            ],
            render.Arguments);

        using var props = JsonDocument.Parse(propsJson!);
        Assert.Equal($"jobs/{_jobId}/plan.json", props.RootElement.GetProperty("planPath").GetString());
        Assert.Equal($"jobs/{_jobId}/narration.wav", props.RootElement.GetProperty("narrationPath").GetString());
    }

    [Fact]
    public async Task RenderAsync_StagesJobFilesDuringRender_AndCleansUpAfterwards()
    {
        var stagedFiles = new List<string>();
        var runner = new ScriptedRunner(onRender: spec =>
        {
            stagedFiles.AddRange(Directory.GetFiles(StagingDirectory).Select(Path.GetFileName)!);
            WriteVideo(spec);
            return Ok();
        });

        var result = await CreateRenderer(runner).RenderAsync(Request, CancellationToken.None);

        Assert.Equal(["narration.wav", "plan.json", "render-props.json"], stagedFiles.Order());
        Assert.False(Directory.Exists(StagingDirectory));
        Assert.True(File.Exists(Path.Combine(_jobDirectory, "final.mp4")));
        Assert.False(File.Exists(Path.Combine(_jobDirectory, "final.rendering.mp4")));
        Assert.Equal(4, result.FileSizeBytes);
    }

    [Fact]
    public async Task RenderAsync_RunsAssetSyncBeforeRendering()
    {
        var runner = new ScriptedRunner(onRender: spec => { WriteVideo(spec); return Ok(); });

        await CreateRenderer(runner).RenderAsync(Request, CancellationToken.None);

        var syncIndex = runner.Calls.FindIndex(c => c.Arguments.Any(a => a.EndsWith("sync-public-assets.mjs")));
        var renderIndex = runner.Calls.FindIndex(c => c.Arguments.Contains("render"));
        Assert.InRange(syncIndex, 0, renderIndex - 1);
    }

    [Fact]
    public async Task RenderAsync_NonZeroExit_ThrowsWithTheEndOfStderr_AndKeepsNoPartialFile()
    {
        var stderr = string.Join('\n', Enumerable.Range(1, 40).Select(i => $"stack line {i}")) + "\nError: Asset 'x' not found";
        var runner = new ScriptedRunner(onRender: spec =>
        {
            File.WriteAllText(spec.Arguments[4], "partial");
            return new ProcessResult(1, "", stderr, TimeSpan.FromSeconds(3));
        });

        var ex = await Assert.ThrowsAsync<RenderFailedException>(() =>
            CreateRenderer(runner).RenderAsync(Request, CancellationToken.None));

        Assert.Contains("Asset 'x' not found", ex.Message);
        Assert.DoesNotContain("stack line 1 ", ex.Message + " ");
        Assert.False(File.Exists(Path.Combine(_jobDirectory, "final.rendering.mp4")));
        Assert.False(File.Exists(Path.Combine(_jobDirectory, "final.mp4")));
        Assert.False(Directory.Exists(StagingDirectory));
    }

    [Fact]
    public async Task RenderAsync_RealRemotionFailureOutput_ReportsTheRealErrorNotThePuppeteerNoise()
    {
        // Shape captured from an actual failed render of a plan with an invalid animation id.
        const string stderr = """
            Was not able to close puppeteer page ProtocolError: Protocol error (Target.closeTarget): No target found for targetId
                at createProtocolError (C:\renderer\node_modules\@remotion\renderer\dist\browser\Connection.js:227:25)
                at #onMessage (C:\renderer\node_modules\@remotion\renderer\dist\browser\Connection.js:103:37)
            {
              code: undefined,
              originalMessage: 'No target found for targetId'
            }
             Error  Error: Invalid plan JSON (jobs/x/plan.json): scenes[1].animation: Invalid option: expected one of "fade"|"slide"
            """;
        var runner = new ScriptedRunner(onRender: _ =>
            new ProcessResult(1, "Bundling 100%\nGetting composition\nAn error occurred:\n", stderr, TimeSpan.FromSeconds(9)));

        var ex = await Assert.ThrowsAsync<RenderFailedException>(() =>
            CreateRenderer(runner).RenderAsync(Request, CancellationToken.None));

        Assert.Equal(
            "Remotion render failed (exit code 1): Error: Invalid plan JSON (jobs/x/plan.json): " +
            "scenes[1].animation: Invalid option: expected one of \"fade\"|\"slide\"",
            ex.Message);
    }

    [Fact]
    public async Task RenderAsync_SuccessExitButNoVideo_Throws()
    {
        var runner = new ScriptedRunner(onRender: _ => Ok());

        await Assert.ThrowsAsync<RenderFailedException>(() =>
            CreateRenderer(runner).RenderAsync(Request, CancellationToken.None));
    }

    [Fact]
    public async Task RenderAsync_Timeout_ThrowsRenderFailedMentioningTheSetting()
    {
        var runner = new ScriptedRunner(onRender: _ => throw new TimeoutException());

        var ex = await Assert.ThrowsAsync<RenderFailedException>(() =>
            CreateRenderer(runner).RenderAsync(Request, CancellationToken.None));

        Assert.Contains("Remotion:TimeoutSeconds", ex.Message);
    }

    [Fact]
    public async Task RenderAsync_NodeMissing_ThrowsUnavailableWithoutRendering()
    {
        var runner = new ScriptedRunner(onRender: _ => Ok(), nodeVersion: _ => throw new ProcessLaunchException("not found"));

        var ex = await Assert.ThrowsAsync<RendererUnavailableException>(() =>
            CreateRenderer(runner).RenderAsync(Request, CancellationToken.None));

        Assert.Contains("Node.js", ex.Message);
        Assert.Null(runner.RenderCall);
    }

    [Fact]
    public async Task CheckAvailabilityAsync_NodeModulesMissing_ExplainsNpmInstall()
    {
        Directory.Delete(Path.Combine(_rendererDirectory, "node_modules"), recursive: true);

        var availability = await CreateRenderer(new ScriptedRunner(onRender: _ => Ok())).CheckAvailabilityAsync(CancellationToken.None);

        Assert.False(availability.IsReady);
        Assert.Contains(availability.Problems, p => p.Contains("node_modules"));
        Assert.Contains("npm install", availability.SetupHint);
    }

    [Fact]
    public async Task RenderAsync_RelativeOrNonMp4Output_IsRejected()
    {
        var renderer = CreateRenderer(new ScriptedRunner(onRender: _ => Ok()));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            renderer.RenderAsync(Request with { OutputPath = "final.mp4" }, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            renderer.RenderAsync(Request with { OutputPath = Path.Combine(_jobDirectory, "final.exe") }, CancellationToken.None));
    }

    // --- Day 27: skip a render whose inputs haven't changed ---

    [Fact]
    public async Task RenderAsync_SameInputsAsTheExistingVideo_SkipsTheRender()
    {
        var runner = new ScriptedRunner(onRender: spec => { WriteVideo(spec); return Ok(); });
        var renderer = CreateRenderer(runner);
        await renderer.RenderAsync(Request, CancellationToken.None);
        var renders = runner.Calls.Count(c => c.Arguments.Contains("render"));

        var again = await renderer.RenderAsync(Request, CancellationToken.None);

        Assert.Equal(renders, runner.Calls.Count(c => c.Arguments.Contains("render")));
        Assert.Equal(4, again.FileSizeBytes);
        Assert.True(again.Skipped);
    }

    [Theory]
    [InlineData("plan.json")]       // a plan edit
    [InlineData("narration.wav")]   // a new voice-over
    [InlineData("renderer-src")]    // a change in the renderer's own code
    [InlineData("asset")]           // a replaced drawing
    public async Task RenderAsync_AnyInputChanged_RendersAgain(string change)
    {
        var assetsDirectory = Path.Combine(_root, "assets");
        Directory.CreateDirectory(assetsDirectory);
        File.WriteAllText(Path.Combine(assetsDirectory, "mascot.svg"), "<svg/>");
        var runner = new ScriptedRunner(onRender: spec => { WriteVideo(spec); return Ok(); });
        var renderer = CreateRenderer(runner);
        await renderer.RenderAsync(Request, CancellationToken.None);

        switch (change)
        {
            case "plan.json": File.WriteAllText(Request.PlanPath, """{"title":"edited"}"""); break;
            case "narration.wav": File.WriteAllBytes(Request.NarrationPath!, [9, 9]); break;
            case "renderer-src": File.WriteAllText(Path.Combine(_rendererDirectory, "src", "index.ts"), "// changed"); break;
            case "asset": File.WriteAllText(Path.Combine(assetsDirectory, "mascot.svg"), "<svg viewBox='0 0 1 1'/>"); break;
        }

        var again = await renderer.RenderAsync(Request, CancellationToken.None);

        Assert.Equal(2, runner.Calls.Count(c => c.Arguments.Contains("render")));
        Assert.False(again.Skipped);
    }

    [Fact]
    public async Task RenderAsync_VideoDeletedSinceTheLastRender_RendersAgain()
    {
        var runner = new ScriptedRunner(onRender: spec => { WriteVideo(spec); return Ok(); });
        var renderer = CreateRenderer(runner);
        await renderer.RenderAsync(Request, CancellationToken.None);
        File.Delete(Request.OutputPath);

        await renderer.RenderAsync(Request, CancellationToken.None);

        Assert.Equal(2, runner.Calls.Count(c => c.Arguments.Contains("render")));
    }

    [Fact]
    public async Task RenderAsync_SkippingDisabled_AlwaysRenders()
    {
        _options.SkipUnchangedRenders = false;
        var runner = new ScriptedRunner(onRender: spec => { WriteVideo(spec); return Ok(); });
        var renderer = CreateRenderer(runner);

        await renderer.RenderAsync(Request, CancellationToken.None);
        await renderer.RenderAsync(Request, CancellationToken.None);

        Assert.Equal(2, runner.Calls.Count(c => c.Arguments.Contains("render")));
    }

    [Fact]
    public async Task RenderAsync_ConfiguredConcurrency_IsPassedToRemotion()
    {
        _options.Concurrency = 4;
        var runner = new ScriptedRunner(onRender: spec => { WriteVideo(spec); return Ok(); });

        await CreateRenderer(runner).RenderAsync(Request, CancellationToken.None);

        var args = runner.RenderCall!.Arguments.ToList();
        Assert.Equal("4", args[args.IndexOf("--concurrency") + 1]);
    }

    private RemotionRenderer CreateRenderer(IProcessRunner runner) =>
        new(runner, Options.Create(_options), NullLogger<RemotionRenderer>.Instance);

    private static ProcessResult Ok() => new(0, "", "", TimeSpan.FromSeconds(1));

    private static void WriteVideo(ProcessSpec renderSpec) => File.WriteAllBytes(renderSpec.Arguments[4], [0, 0, 0, 1]);

    // Answers `node --version` and the asset sync with success, and hands the actual
    // `remotion render` call to the test.
    private sealed class ScriptedRunner : IProcessRunner
    {
        private readonly Func<ProcessSpec, ProcessResult> _onRender;
        private readonly Func<ProcessSpec, ProcessResult> _nodeVersion;

        public ScriptedRunner(Func<ProcessSpec, ProcessResult> onRender, Func<ProcessSpec, ProcessResult>? nodeVersion = null)
        {
            _onRender = onRender;
            _nodeVersion = nodeVersion ?? (_ => new ProcessResult(0, "v24.15.0", "", TimeSpan.Zero));
        }

        public List<ProcessSpec> Calls { get; } = [];
        public ProcessSpec? RenderCall { get; private set; }

        public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken)
        {
            Calls.Add(spec);
            if (spec.Arguments is ["--version"])
            {
                return Task.FromResult(_nodeVersion(spec));
            }

            if (spec.Arguments.Contains("render"))
            {
                RenderCall = spec;
                return Task.FromResult(_onRender(spec));
            }

            return Task.FromResult(Ok());
        }
    }
}
