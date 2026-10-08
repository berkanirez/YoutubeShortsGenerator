namespace ShortsGenerator.Domain;

// Moved here from Application on Day 26: the job now remembers what it was queued for
// (VideoJob.PendingWork), so the kind of work is part of the persisted domain state.
public enum VideoJobWorkKind
{
    // Run every remaining stage (script -> plan -> narration -> render) from wherever the job is.
    Generate,

    // Only (re-)render: the job already has its plan and narration.
    Render,

    // Regenerate the voice-over from the (edited) plan, re-time it, then render (Day 23).
    Renarrate,
}
