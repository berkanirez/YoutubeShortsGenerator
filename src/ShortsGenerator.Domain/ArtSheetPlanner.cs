using System.Text;

namespace ShortsGenerator.Domain;

/// <summary>One image the owner generates with several pictures on it, cut apart on import.</summary>
/// <param name="Subjects">The pictures on the sheet, in reading order (left to right, top to bottom).</param>
public sealed record ArtSheet(string FileName, int Rows, int Cols, IReadOnlyList<ArtSubject> Subjects, string Prompt);

// Generating 70 pictures one prompt at a time is slow; one image can carry 4 characters side by
// side or 9 icons in a grid, and is cut apart on import (tools/images/split_sheet.py). Pictures
// on one sheet also come out more consistent with each other. Standing characters go 4 in a row
// (tall panels keep the most resolution for a tall figure), mixed exercise poses 2x2, icons 3x3.
// Exercise pairs go two per image, one per row (each row is split into its poses as before).
public static class ArtSheetPlanner
{
    public const int MaxCharactersPerSheet = 4;
    public const int MaxIconsPerSheet = 9;

    /// <returns>The sheets, and the pictures that stay one per prompt.</returns>
    public static (IReadOnlyList<ArtSheet> Sheets, IReadOnlyList<ArtSubject> Singles) Plan(IReadOnlyList<ArtSubject> missing)
    {
        var sheets = new List<ArtSheet>();
        var singles = new List<ArtSubject>();

        void AddChunks(IReadOnlyList<ArtSubject> group, int max, Func<IReadOnlyList<ArtSubject>, (int Rows, int Cols)> layout, Func<IReadOnlyList<ArtSubject>, int, int, string> prompt)
        {
            foreach (var chunk in Balanced(group, max))
            {
                if (chunk.Count == 1)
                {
                    singles.Add(chunk[0]);
                    continue;
                }

                var (rows, cols) = layout(chunk);
                sheets.Add(new ArtSheet($"sheet-{chunk[0].Id}.png", rows, cols, chunk, prompt(chunk, rows, cols)));
            }
        }

        AddChunks(missing.Where(s => s.Kind == ArtKind.MascotPose).ToList(), MaxCharactersPerSheet, c => (1, c.Count), CharacterSheet);
        AddChunks(missing.Where(s => s.Kind == ArtKind.Muscle).ToList(), MaxCharactersPerSheet, c => (1, c.Count), CharacterSheet);
        AddChunks(missing.Where(s => s.Kind == ArtKind.Exercise).ToList(), MaxCharactersPerSheet, c => c.Count == 4 ? (2, 2) : (1, c.Count), CharacterSheet);
        AddChunks(
            missing.Where(s => s.Kind is ArtKind.Equipment or ArtKind.Icon).ToList(),
            MaxIconsPerSheet,
            c => c.Count <= 4 ? (2, 2) : c.Count <= 6 ? (2, 3) : (3, 3),
            IconSheet);
        // Two exercise pairs per image, one per row; each row is cut out and then split into
        // its two poses like a single pair image.
        AddChunks(missing.Where(s => s.Kind == ArtKind.ExercisePair).ToList(), 2, c => (c.Count, 1), PairSheet);
        return (sheets, singles);
    }

    private static string PairSheet(IReadOnlyList<ArtSubject> chunk, int rows, int cols)
    {
        var sb = new StringBuilder();
        sb.AppendLine(ArtPromptBuilder.Character);
        sb.AppendLine($"Make one landscape image (3:2) with {chunk.Count} rows separated by plenty of white space. Each row shows the " +
                      "SAME character TWICE side by side - the two positions of one exercise - same size in both, with an empty gap " +
                      "between the two figures:");
        for (var i = 0; i < chunk.Count; i++)
        {
            var s = chunk[i];
            sb.AppendLine($"{(i == 0 ? "Top" : "Bottom")} row, {s.Describe} ({s.Camera ?? "same side-view camera, feet at exactly the same position in both"}): " +
                          $"LEFT: {s.Top}; RIGHT: {s.Bottom}.");
        }

        sb.Append("Pure flat white background, no floor line, no shadows, no borders, no text, no letters, no numbers, no arrows. " +
                  "Full body visible in every pose, nothing cut off.");
        return sb.ToString();
    }

    // 17 into sheets of at most 4 -> 4, 4, 3, 3, 3 (not 4, 4, 4, 4, 1).
    private static IEnumerable<IReadOnlyList<ArtSubject>> Balanced(IReadOnlyList<ArtSubject> group, int max)
    {
        if (group.Count == 0)
        {
            yield break;
        }

        var count = (group.Count + max - 1) / max;
        var index = 0;
        for (var i = 0; i < count; i++)
        {
            var size = group.Count / count + (i < group.Count % count ? 1 : 0);
            yield return group.Skip(index).Take(size).ToList();
            index += size;
        }
    }

    private static string Position(int index, int rows, int cols)
    {
        if (rows == 1)
        {
            return index == 0 ? "far left" : index == cols - 1 ? "far right" : $"{Ordinal(index + 1)} from the left";
        }

        var row = index / cols;
        var col = index % cols;
        var rowName = rows == 2 ? (row == 0 ? "top" : "bottom") : row == 0 ? "top" : row == rows - 1 ? "bottom" : "middle";
        var colName = cols == 2 ? (col == 0 ? "left" : "right") : col == 0 ? "left" : col == cols - 1 ? "right" : "center";
        return $"{rowName} {colName}";
    }

    private static string Ordinal(int n) => n switch { 2 => "second", 3 => "third", _ => $"{n}th" };

    private static string Panel(ArtSubject subject) => subject.Kind switch
    {
        ArtKind.Muscle =>
            (subject.BackView ? "BACK view (seen from behind)" : "front view") +
            $", standing relaxed, arms slightly away from the body; the {subject.Describe} colored solid bright red (#EF4444) " +
            "like an anatomy highlight, everything else in his normal colors",
        _ => subject.Describe,
    };

    private static string CharacterSheet(IReadOnlyList<ArtSubject> chunk, int rows, int cols)
    {
        var sb = new StringBuilder();
        sb.AppendLine(ArtPromptBuilder.Character);
        sb.AppendLine(rows == 1
            ? $"Make one wide landscape image (3:2) with {chunk.Count} panels side by side in a single row, equal width, with plenty of " +
              "white space between the panels - like a character sheet. The SAME character at the SAME size in every panel:"
            : $"Make one square image with a {rows}x{cols} grid of {chunk.Count} panels, equal size, with plenty of white space " +
              "between the panels - like a character sheet. The SAME character at the SAME scale in every panel:");
        for (var i = 0; i < chunk.Count; i++)
        {
            sb.AppendLine($"Panel {i + 1} ({Position(i, rows, cols)}): {Panel(chunk[i])}.");
        }

        sb.Append("Pure flat white background, no panel borders, no grid lines, no floor line, no shadows, no text, no letters, " +
                  "no numbers, no arrows. Full body visible in every panel, nothing cut off.");
        return sb.ToString();
    }

    private static string IconSheet(IReadOnlyList<ArtSubject> chunk, int rows, int cols)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"A sheet of {chunk.Count} separate simple icons arranged in a {rows}x{cols} grid with lots of white space " +
                      "between them, all the same size, each in a clean anime cel-shaded style with bold black outlines:");
        for (var i = 0; i < chunk.Count; i++)
        {
            sb.AppendLine($"{i + 1} ({Position(i, rows, cols)}): {chunk[i].Describe}.");
        }

        sb.Append("Square image, pure flat white background, no grid lines, no borders, no shadows, no text, no letters, no numbers.");
        return sb.ToString();
    }
}
