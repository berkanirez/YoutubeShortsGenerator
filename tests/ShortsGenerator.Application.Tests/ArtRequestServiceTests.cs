using Microsoft.Extensions.Logging.Abstractions;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Application.Tests;

public sealed class ArtRequestServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    private static readonly ArtSubject Biceps = new("biceps", ArtKind.Muscle, ["biceps"], "BICEPS on both upper arms");
    private static readonly ArtSubject Forearms = new("forearms", ArtKind.Muscle, ["forearms", "forearm"], "FOREARMS");

    private readonly InMemoryVideoJobRepository _repository = new();
    private readonly TempJobOutputLocator _locator = new();
    private readonly InMemoryVideoPlanStore _planStore;
    private readonly TempArtifactWriter _artifacts;
    private readonly MutableAssetRepository _assets = new(Asset("biceps-highlight"), Asset("mascot-idle"));
    private readonly FakeArtImporter _importer;

    public ArtRequestServiceTests()
    {
        _planStore = new InMemoryVideoPlanStore(_locator);
        _artifacts = new TempArtifactWriter(_locator);
        _importer = new FakeArtImporter(_assets);
    }

    public void Dispose() => _locator.Dispose();

    [Fact]
    public async Task HoldForArt_AMissingPicture_PausesTheJobAndWritesTheReadyPrompt()
    {
        var job = await AddPlannedJobAsync("Your forearms give out first.");

        var held = await Service().HoldForArtAsync(job.Id, CancellationToken.None);

        Assert.True(held);
        Assert.Equal(VideoJobStatus.WaitingForArt, (await Get(job)).Status);
        var (fileName, content) = Assert.Single(_artifacts.Written);
        Assert.Equal(ArtRequestService.RequestsFileName, fileName);
        Assert.Contains("`forearms-highlight.png`", content);
        Assert.Contains("The FOREARMS are colored solid bright red", content);
        Assert.Contains("Attach `assets/mascot/mascot-idle.png`", content);
    }

    [Fact]
    public async Task HoldForArt_EverythingInTheCatalog_GoesOnAndPointsTheSceneAtIt()
    {
        var job = await AddPlannedJobAsync("Your biceps flex the elbow.", asset: "mascot-idle");

        var held = await Service().HoldForArtAsync(job.Id, CancellationToken.None);

        Assert.False(held);
        var plan = (await _planStore.LoadAsync(job.Id, CancellationToken.None))!;
        Assert.True(plan.ArtChecked);
        Assert.Equal(["biceps-highlight"], plan.Scenes[1].AssetIds);
        Assert.Equal(VideoJobStatus.Planning, (await Get(job)).Status);
    }

    [Fact]
    public async Task Continue_PictureStillMissing_KeepsWaitingAndSaysWhich()
    {
        var job = await AddPlannedJobAsync("Your forearms give out first.");
        await Service().HoldForArtAsync(job.Id, CancellationToken.None);

        var result = await Service().ContinueAsync(job.Id, skipMissing: false, CancellationToken.None);

        Assert.False(result.Resumed);
        Assert.Equal("forearms-highlight.png", Assert.Single(result.Missing).Subject.IncomingFileName);
        Assert.Equal(VideoJobStatus.WaitingForArt, (await Get(job)).Status);
    }

    [Fact]
    public async Task Continue_AfterTheUpload_ImportsItAndResumesWithTheNewPicture()
    {
        var job = await AddPlannedJobAsync("Your forearms give out first.");
        var service = Service();
        await service.HoldForArtAsync(job.Id, CancellationToken.None);
        await service.SaveUploadAsync(job.Id, "forearms-highlight.png", new MemoryStream([1, 2, 3]), CancellationToken.None);

        var result = await service.ContinueAsync(job.Id, skipMissing: false, CancellationToken.None);

        Assert.True(result.Resumed);
        Assert.Equal(["forearms-highlight"], result.Imported);
        Assert.Equal(VideoJobStatus.Planning, (await Get(job)).Status);
        var plan = (await _planStore.LoadAsync(job.Id, CancellationToken.None))!;
        Assert.True(plan.ArtChecked);
        Assert.Equal(["forearms-highlight"], plan.Scenes[1].AssetIds);
    }

    [Fact]
    public async Task Continue_SkippingTheMissingPicture_ResumesWithThePlansOwnPick()
    {
        var job = await AddPlannedJobAsync("Your forearms give out first.");
        await Service().HoldForArtAsync(job.Id, CancellationToken.None);

        var result = await Service().ContinueAsync(job.Id, skipMissing: true, CancellationToken.None);

        Assert.True(result.Resumed);
        Assert.Single(result.Missing);
        var plan = (await _planStore.LoadAsync(job.Id, CancellationToken.None))!;
        Assert.True(plan.ArtChecked);
        Assert.Equal(["mascot-idle"], plan.Scenes[1].AssetIds);
    }

    [Fact]
    public async Task Upload_UnderAFileNameTheJobDidNotAskFor_IsRefused()
    {
        var job = await AddPlannedJobAsync("Your forearms give out first.");

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            Service().SaveUploadAsync(job.Id, "../../appsettings.json", new MemoryStream([1]), CancellationToken.None));
        Assert.Empty(_importer.Saved);
    }

    [Fact]
    public async Task Pipeline_PlanNeedsAPicture_StopsBeforeTheVoiceOver()
    {
        var job = await AddPlannedJobAsync("Your forearms give out first.");
        var tts = new FakeTtsProvider { WriteOutputFile = true };
        var pipeline = new VideoGenerationPipeline(
            _repository,
            _planStore,
            new ScriptGenerationService(new FakeLlmProvider(), _repository, TimeProvider.System),
            new ScenePlanningService(new FakeLlmProvider(), _repository, _planStore, new InMemoryAssetRepository(), TimeProvider.System),
            new NarrationService(tts, _repository, _planStore, _locator, TimeProvider.System),
            new RenderService(new FakeRenderer(), _repository, _locator, TimeProvider.System, _planStore),
            TimeProvider.System,
            NullLogger<VideoGenerationPipeline>.Instance,
            art: Service());

        await pipeline.ProcessAsync(new VideoJobWorkItem(job.Id, VideoJobWorkKind.Generate), CancellationToken.None);

        Assert.Equal(VideoJobStatus.WaitingForArt, (await Get(job)).Status);
        Assert.Empty(tts.Requests);
    }

    private ArtRequestService Service() => new(
        _repository, _planStore, _assets, new StaticArtSubjects(Biceps, Forearms), _importer, _artifacts,
        TimeProvider.System, NullLogger<ArtRequestService>.Instance);

    private async Task<VideoJob> AddPlannedJobAsync(string anatomyNarration, string asset = "mascot-idle")
    {
        var job = VideoJob.Create("Grip strength", null, Now);
        job.MarkPlanning(Now);
        job.AttachGeneratedScript($"Is your grip weak? {anatomyNarration} Train it twice a week.", Now);
        await _repository.AddAsync(job, CancellationToken.None);
        IReadOnlyList<Scene> scenes =
        [
            new("s1", 1, SceneType.Hook, 3, "Is your grip weak?", "Weak grip?", [], AnimationId.PunchZoom, null, null, null),
            new("s2", 2, SceneType.AnatomyHighlight, 4, anatomyNarration, null, [asset], AnimationId.Pop, null, null, null),
            new("s3", 3, SceneType.Outro, 3, "Train it twice a week.", "Train it", [], AnimationId.Fade, null, null, null),
        ];
        await _planStore.SaveAsync(job.Id, new VideoPlan("Grip", "Is your grip weak?", 30, job.GeneratedScript!, scenes, null, null, null), CancellationToken.None);
        return job;
    }

    private async Task<VideoJob> Get(VideoJob job) => (await _repository.GetByIdAsync(job.Id, CancellationToken.None))!;

    private static AssetMetadata Asset(string id) =>
        new(id, $"{id}.png", "anatomy", [id], [SceneType.AnatomyHighlight], AssetOrientation.Square, "center");

    private sealed class StaticArtSubjects(params ArtSubject[] subjects) : IArtSubjectCatalog
    {
        public Task<IReadOnlyList<ArtSubject>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ArtSubject>>(subjects);
    }

    private sealed class MutableAssetRepository(params AssetMetadata[] assets) : IAssetRepository
    {
        public List<AssetMetadata> Assets { get; } = [.. assets];

        public Task<IReadOnlyList<AssetMetadata>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AssetMetadata>>(Assets.ToList());

        public Task<AssetMetadata?> GetByIdAsync(string id, CancellationToken cancellationToken) =>
            Task.FromResult(Assets.FirstOrDefault(a => a.Id == id));
    }

    private sealed class FakeArtImporter(MutableAssetRepository catalog) : IArtImporter
    {
        public HashSet<string> Saved { get; } = [];

        public bool HasSource(ArtSubject subject) => Saved.Contains(subject.IncomingFileName);

        public Task SaveSourceAsync(ArtSubject subject, Stream content, CancellationToken cancellationToken)
        {
            Saved.Add(subject.IncomingFileName);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ImportAsync(ArtSubject subject, CancellationToken cancellationToken)
        {
            catalog.Assets.AddRange(subject.AssetIds.Select(Asset));
            return Task.FromResult(subject.AssetIds);
        }
    }
}
