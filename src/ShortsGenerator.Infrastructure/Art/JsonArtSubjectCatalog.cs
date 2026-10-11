using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Infrastructure.Art;

// subjects.json, validated: a subject id becomes an asset id and a file name, so it must be
// plain kebab-case. Re-read when the file changes, so a new subject needs no restart.
public sealed partial class JsonArtSubjectCatalog : IArtSubjectCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly object _gate = new();
    private (DateTime Stamp, IReadOnlyList<ArtSubject> Subjects) _snapshot;

    public JsonArtSubjectCatalog(IOptions<ArtImportOptions> options)
    {
        _path = options.Value.SubjectsPath;
        _snapshot = (File.GetLastWriteTimeUtc(_path), Load(_path));
    }

    public Task<IReadOnlyList<ArtSubject>> GetAllAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var stamp = File.GetLastWriteTimeUtc(_path);
            if (stamp != _snapshot.Stamp)
            {
                _snapshot = (stamp, Load(_path));
            }

            return Task.FromResult(_snapshot.Subjects);
        }
    }

    public static IReadOnlyList<ArtSubject> Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Art subjects file not found at '{Path.GetFullPath(path)}'.");
        }

        List<ArtSubject>? subjects;
        try
        {
            subjects = JsonSerializer.Deserialize<List<ArtSubject>>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Art subjects file '{path}' is not valid: {ex.Message}", ex);
        }

        var problems = new List<string>();
        foreach (var subject in subjects ?? [])
        {
            if (!SafeId().IsMatch(subject.Id ?? string.Empty))
            {
                problems.Add($"'{subject.Id}': ids must be lowercase kebab-case (they become file names)");
            }

            if (subject.Terms is null || subject.Terms.Count == 0 || string.IsNullOrWhiteSpace(subject.Describe))
            {
                problems.Add($"'{subject.Id}': needs terms and a describe text");
            }

            if (subject.Kind == ArtKind.MascotPose && !(subject.Id ?? string.Empty).StartsWith("mascot-", StringComparison.Ordinal))
            {
                problems.Add($"'{subject.Id}': a MascotPose id starts with 'mascot-'");
            }

            if (subject.Kind == ArtKind.Cycle && (string.IsNullOrWhiteSpace(subject.Top) || string.IsNullOrWhiteSpace(subject.Bottom)))
            {
                problems.Add($"'{subject.Id}': a Cycle needs top (frame 1) and bottom (frame 4)");
            }

            if (subject.Kind == ArtKind.ExercisePair && (string.IsNullOrWhiteSpace(subject.Top) || string.IsNullOrWhiteSpace(subject.Bottom)))
            {
                problems.Add($"'{subject.Id}': an ExercisePair needs top and bottom poses");
            }
        }

        var ids = (subjects ?? []).Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var mistake in (subjects ?? []).Where(s => s.Kind == ArtKind.Mistake))
        {
            if (mistake.Exercise is null || !ids.Contains(mistake.Exercise))
            {
                problems.Add($"'{mistake.Id}': a Mistake needs an existing exercise subject (got '{mistake.Exercise}')");
            }

            if (string.IsNullOrWhiteSpace(mistake.Label) || string.IsNullOrWhiteSpace(mistake.FixLabel) || mistake.Point is not { Count: 2 })
            {
                problems.Add($"'{mistake.Id}': a Mistake needs a label, a fixLabel and a point [x, y]");
            }
        }

        foreach (var duplicate in (subjects ?? []).GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            problems.Add($"'{duplicate.Key}' is defined {duplicate.Count()} times");
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException($"Art subjects file '{path}' has problems: {string.Join("; ", problems)}.");
        }

        return subjects ?? [];
    }

    [GeneratedRegex("^[a-z][a-z0-9]*(-[a-z0-9]+)*$")]
    private static partial Regex SafeId();
}
