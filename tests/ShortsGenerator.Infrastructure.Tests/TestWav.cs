using System.Text;

namespace ShortsGenerator.Infrastructure.Tests;

// Writes a real (silent) 16-bit PCM mono .wav file, byte by byte, so WAV-reading code is
// tested against the actual format rather than against itself.
internal static class TestWav
{
    public static void Write(string path, double durationSeconds, int sampleRate = 24000, bool withListChunk = false)
    {
        var dataBytes = (int)Math.Round(durationSeconds * sampleRate) * 2;
        using var writer = new BinaryWriter(File.Create(path));

        var listChunk = withListChunk ? Encoding.ASCII.GetBytes("INFOISFT\u0005\0\0\0test\0\0") : [];
        var riffSize = 4 + (8 + 16) + (withListChunk ? 8 + listChunk.Length : 0) + (8 + dataBytes);

        writer.Write("RIFF"u8);
        writer.Write(riffSize);
        writer.Write("WAVE"u8);

        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);              // PCM
        writer.Write((short)1);              // mono
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);        // byte rate
        writer.Write((short)2);              // block align
        writer.Write((short)16);             // bits per sample

        if (withListChunk)
        {
            writer.Write("LIST"u8);
            writer.Write(listChunk.Length);
            writer.Write(listChunk);
        }

        writer.Write("data"u8);
        writer.Write(dataBytes);
        writer.Write(new byte[dataBytes]);
    }
}
