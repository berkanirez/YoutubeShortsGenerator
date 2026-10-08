namespace ShortsGenerator.Domain;

// The one place that decides where a job's files live (output/<job-id>/), so plan.json,
// narration.wav and later final.mp4 can't drift into different folders.
public interface IJobOutputLocator
{
    /// <summary>Absolute directory for this job's generated files (not created by this call).</summary>
    string GetJobDirectory(Guid jobId);
}
