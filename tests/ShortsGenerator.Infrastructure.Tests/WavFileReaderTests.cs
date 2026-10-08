using ShortsGenerator.Infrastructure.Media;

namespace ShortsGenerator.Infrastructure.Tests;

public sealed class WavFileReaderTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"wav-test-{Guid.NewGuid()}.wav");

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void Read_ReturnsFormatAndExactDuration()
    {
        TestWav.Write(_path, 2.5, sampleRate: 24000);

        var info = WavFileReader.Read(_path);

        Assert.Equal(24000, info.SampleRate);
        Assert.Equal(1, info.Channels);
        Assert.Equal(16, info.BitsPerSample);
        Assert.Equal(2.5, info.DurationSeconds, precision: 6);
    }

    [Fact]
    public void Read_SkipsUnknownChunksBeforeData()
    {
        TestWav.Write(_path, 1.25, withListChunk: true);

        Assert.Equal(1.25, WavFileReader.Read(_path).DurationSeconds, precision: 6);
    }

    [Fact]
    public void Read_NotAWavFile_ThrowsInvalidData()
    {
        File.WriteAllText(_path, "definitely not audio");

        Assert.Throws<InvalidDataException>(() => WavFileReader.Read(_path));
    }

    [Fact]
    public void Read_TruncatedData_UsesWhatIsActuallyInTheFile()
    {
        TestWav.Write(_path, 2.0);
        using (var stream = new FileStream(_path, FileMode.Open))
        {
            stream.SetLength(stream.Length - 24000); // drop 0.5s of 16-bit mono samples
        }

        Assert.Equal(1.5, WavFileReader.Read(_path).DurationSeconds, precision: 6);
    }
}
