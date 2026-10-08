using System.Buffers.Binary;
using System.Text;

namespace ShortsGenerator.Infrastructure.Media;

public sealed record WavInfo(int SampleRate, int Channels, int BitsPerSample, long DataBytes)
{
    public double DurationSeconds => (double)DataBytes / (SampleRate * Channels * (BitsPerSample / 8));
}

// Reads only the header of a PCM .wav file - enough to know its exact duration without
// FFmpeg/ffprobe (not installed, and not worth a dependency just for this). The format is
// simple: a "RIFF....WAVE" preamble followed by chunks of [4-byte id][4-byte LE size][body];
// "fmt " describes the samples, "data" holds them. Other chunks (LIST, ...) are skipped.
public static class WavFileReader
{
    public static WavInfo Read(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[12];
        if (stream.Read(header) != 12 ||
            !header[..4].SequenceEqual("RIFF"u8) ||
            !header[8..12].SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException($"'{path}' is not a RIFF/WAVE file.");
        }

        int? sampleRate = null, channels = null, bitsPerSample = null;
        Span<byte> chunkHeader = stackalloc byte[8];
        Span<byte> format = stackalloc byte[16];
        while (stream.Read(chunkHeader) == 8)
        {
            var id = Encoding.ASCII.GetString(chunkHeader[..4]);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[4..8]);

            if (id == "fmt ")
            {
                if (size < 16 || stream.Read(format) != 16)
                {
                    throw new InvalidDataException($"'{path}' has a truncated fmt chunk.");
                }

                channels = BinaryPrimitives.ReadUInt16LittleEndian(format[2..4]);
                sampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(format[4..8]);
                bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(format[14..16]);
                stream.Seek(size - 16 + (size % 2), SeekOrigin.Current);
            }
            else if (id == "data")
            {
                if (sampleRate is null || channels is null || bitsPerSample is null)
                {
                    throw new InvalidDataException($"'{path}' has a data chunk before its fmt chunk.");
                }

                // Never trust the declared size beyond what's actually in the file.
                var available = stream.Length - stream.Position;
                return new WavInfo(sampleRate.Value, channels.Value, bitsPerSample.Value, Math.Min(size, available));
            }
            else
            {
                // Chunks are word-aligned: an odd-sized chunk is followed by one pad byte.
                stream.Seek(size + (size % 2), SeekOrigin.Current);
            }
        }

        throw new InvalidDataException($"'{path}' has no data chunk.");
    }
}
