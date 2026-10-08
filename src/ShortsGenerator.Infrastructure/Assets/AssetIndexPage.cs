using System.Net;
using System.Text;
using System.Text.Json;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Infrastructure.Assets;

// A one-file HTML contact sheet of the whole catalog: every asset drawn, with its id, tags
// and scene types - the quickest way to see what the LLM can pick from, or to spot a
// drawing that doesn't match its tags.
public static class AssetIndexPage
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Build(string manifestPath)
    {
        var manifestDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        var assets = JsonSerializer.Deserialize<List<AssetMetadata>>(File.ReadAllText(manifestPath), JsonOptions) ?? [];

        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><title>Asset index</title><style>");
        sb.AppendLine("body{font-family:Arial,sans-serif;background:#0f172a;color:#e2e8f0;margin:24px}");
        sb.AppendLine(".grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(220px,1fr));gap:16px}");
        sb.AppendLine(".card{background:#1e293b;border-radius:12px;padding:12px}.art{height:180px;display:flex;align-items:center;justify-content:center}");
        sb.AppendLine(".art svg{max-width:100%;max-height:100%}.id{font-weight:bold;margin-top:8px}.meta{font-size:12px;color:#94a3b8}");
        sb.AppendLine("</style></head><body>");
        sb.AppendLine($"<h1>Asset index ({assets.Count})</h1><div class=\"grid\">");
        foreach (var asset in assets.OrderBy(a => a.Category).ThenBy(a => a.Id))
        {
            var path = Path.GetFullPath(Path.Combine(manifestDirectory, asset.FilePath));
            var art = File.Exists(path) ? File.ReadAllText(path) : "<em>missing file</em>";
            sb.AppendLine("<div class=\"card\">");
            sb.AppendLine($"<div class=\"art\">{art}</div>");
            sb.AppendLine($"<div class=\"id\">{WebUtility.HtmlEncode(asset.Id)}</div>");
            sb.AppendLine($"<div class=\"meta\">{WebUtility.HtmlEncode(asset.Category)} · {WebUtility.HtmlEncode(string.Join(", ", asset.Tags))}</div>");
            sb.AppendLine($"<div class=\"meta\">{WebUtility.HtmlEncode(string.Join(", ", asset.CompatibleSceneTypes))}</div>");
            sb.AppendLine("</div>");
        }

        sb.AppendLine("</div></body></html>");
        return sb.ToString();
    }
}
