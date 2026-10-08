using Microsoft.Extensions.Hosting;

namespace ShortsGenerator.Infrastructure;

// Relative paths in appsettings.json ("../../output", "../../tools/tts/...") are resolved
// against the Api project's content root, not the process's current directory. Otherwise
// the same config means different folders depending on where `dotnet run` was typed from
// (repo root vs. src/ShortsGenerator.Api) - exactly the kind of "works on my machine"
// bug that's painful to track down later.
internal static class ContentRootPath
{
    public static string Resolve(IHostEnvironment environment, string path) =>
        Path.GetFullPath(path, environment.ContentRootPath);
}
