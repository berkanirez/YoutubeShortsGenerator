using System.Text;
using Microsoft.Extensions.Logging;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Infrastructure.Logging;

// Day 26: every log entry written while a job is being processed also goes to that job's own
// output/<job-id>/job.log - next to its plan.json and final.mp4, so "why did this video fail?"
// is answered by opening its folder instead of scrolling a console that may be gone.
// It relies on the logging *scope* the pipeline opens (BeginScope { JobId }): scopes flow
// with async calls, so the LLM, TTS and renderer adapters' logs land in the file too
// without any of them knowing about it.
public sealed class JobLogFileLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    public const string FileName = "job.log";

    private readonly IJobOutputLocator _locator;
    private readonly object _writeLock = new();
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public JobLogFileLoggerProvider(IJobOutputLocator locator)
    {
        _locator = locator;
    }

    public ILogger CreateLogger(string categoryName) => new JobLogger(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
    }

    internal static bool IsEnabled(string category, LogLevel level) =>
        level != LogLevel.None &&
        // Framework internals (EF Core logs every SQL command at Information) only when they matter.
        level >= (category.StartsWith("Microsoft.", StringComparison.Ordinal) || category.StartsWith("System.", StringComparison.Ordinal)
            ? LogLevel.Warning
            : LogLevel.Information);

    private Guid? CurrentJobId()
    {
        Guid? jobId = null;
        _scopes.ForEachScope((scope, _) =>
        {
            if (scope is IEnumerable<KeyValuePair<string, object>> values)
            {
                foreach (var (key, value) in values)
                {
                    if (key == "JobId" && value is Guid id)
                    {
                        jobId = id;
                    }
                }
            }
        }, (object?)null);
        return jobId;
    }

    private void Write(Guid jobId, string category, LogLevel level, string message, Exception? exception)
    {
        var line = new StringBuilder()
            .Append(DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture))
            .Append(' ').Append(level.ToString().ToUpperInvariant().PadRight(11))
            .Append(' ').Append(category[(category.LastIndexOf('.') + 1)..])
            .Append(": ").Append(message);
        if (exception is not null)
        {
            // The full exception is fine here: this file stays on the user's machine, and the
            // stack trace is exactly what a bug report needs (the API response never has it).
            line.AppendLine().Append(exception);
        }

        line.AppendLine();
        try
        {
            var directory = _locator.GetJobDirectory(jobId);
            lock (_writeLock)
            {
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, FileName), line.ToString(), new UTF8Encoding(false));
            }
        }
        catch (IOException)
        {
            // Logging must never break the work it describes.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class JobLogger : ILogger
    {
        private readonly JobLogFileLoggerProvider _provider;
        private readonly string _category;

        public JobLogger(JobLogFileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _provider._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => JobLogFileLoggerProvider.IsEnabled(_category, logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel) || _provider.CurrentJobId() is not { } jobId)
            {
                return;
            }

            _provider.Write(jobId, _category, logLevel, formatter(state, exception), exception);
        }
    }
}
