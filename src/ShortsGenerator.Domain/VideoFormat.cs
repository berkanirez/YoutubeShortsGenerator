namespace ShortsGenerator.Domain;

// The one output format V1 renders (CLAUDE.md section 11). The renderer registers its
// composition with the same values; timing math on the backend has to use the same fps
// or scene boundaries would land between frames.
public static class VideoFormat
{
    public const int Width = 1080;
    public const int Height = 1920;
    public const int Fps = 30;
}
