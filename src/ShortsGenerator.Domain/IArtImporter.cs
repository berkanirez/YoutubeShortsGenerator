namespace ShortsGenerator.Domain;

// Turns an image the owner generated (by hand, e.g. in ChatGPT) into catalog art: HD
// upscale, background removal, pair split, catalog entry. Behind an interface so the
// pipeline's tests don't need Python/GPU tools, and so an automatic generator (docs/
// ART_AUTOMATION_PLAN.md) could later fill the same incoming slot.
public interface IArtImporter
{
    /// <summary>The owner's image for this subject is waiting in the incoming folder.</summary>
    bool HasSource(ArtSubject subject);

    /// <summary>Stores an uploaded image as the subject's incoming file (PNG, JPEG or WebP).</summary>
    /// <exception cref="ArtImportException">Not an image, or too large.</exception>
    Task SaveSourceAsync(ArtSubject subject, Stream content, CancellationToken cancellationToken);

    /// <summary>Processes the incoming image and adds the subject's assets to the catalog.</summary>
    /// <returns>The asset ids added.</returns>
    /// <exception cref="ArtImportException">A step failed; the message says which and why.</exception>
    Task<IReadOnlyList<string>> ImportAsync(ArtSubject subject, CancellationToken cancellationToken);
}

public sealed class ArtImportException : Exception
{
    public ArtImportException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
