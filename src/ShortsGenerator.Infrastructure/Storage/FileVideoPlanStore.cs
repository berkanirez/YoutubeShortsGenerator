using System.Text.Json;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Infrastructure.Storage;

public sealed class FileVideoPlanStore : IVideoPlanStore
{
    private const string FileName = "plan.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly IJobOutputLocator _locator;

    public FileVideoPlanStore(IJobOutputLocator locator)
    {
        _locator = locator;
    }

    public async Task<string> SaveAsync(Guid jobId, VideoPlan plan, CancellationToken cancellationToken)
    {
        var jobDirectory = _locator.GetJobDirectory(jobId);
        Directory.CreateDirectory(jobDirectory);

        // Written next to the real file, then moved over it: File.Create would truncate the
        // existing plan first, so a crash (or a cancelled edit) mid-write would destroy it, and
        // the dashboard polling GET /plan could read half a file.
        var filePath = Path.Combine(jobDirectory, FileName);
        var tempPath = filePath + ".tmp";
        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, plan, JsonOptions, cancellationToken);
            }

            File.Move(tempPath, filePath, overwrite: true);
        }
        finally
        {
            File.Delete(tempPath);
        }

        return jobDirectory;
    }

    public async Task<VideoPlan?> LoadAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(_locator.GetJobDirectory(jobId), FileName);
        if (!File.Exists(filePath))
        {
            return null;
        }

        VideoPlan? plan;
        try
        {
            await using var stream = File.OpenRead(filePath);
            plan = await JsonSerializer.DeserializeAsync<VideoPlan>(stream, JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(CorruptMessage(jobId, filePath, ex.Message), ex);
        }

        // A file containing just "null" would otherwise read as "no plan yet" and silently
        // send the job back to planning.
        return plan ?? throw new InvalidDataException(CorruptMessage(jobId, filePath, "it contains no plan"));
    }

    private static string CorruptMessage(Guid jobId, string filePath, string detail) =>
        $"The scene plan of video job '{jobId}' ({filePath}) can't be read: {detail.TrimEnd('.')}. " +
        "If it was edited by hand, fix or delete plan.json and generate the plan again.";
}
