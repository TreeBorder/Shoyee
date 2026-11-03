namespace Shoyee.Voice.Apple;

/// <summary>裸 PCM16 mono → 标准 44 字节 WAV 头（silk 解码后要自己包一层，Speech 框架才认）。</summary>
internal static class WavPacking
{
    /// <summary>给 PCM 字节加上 WAV 头。</summary>
    /// <param name="pcm">PCM16 单声道裸数据。</param>
    /// <param name="sampleRate">采样率。</param>
    public static byte[] Wrap(byte[] pcm, int sampleRate)
    {
        var wav = new byte[44 + pcm.Length];
        using var stream = new MemoryStream(wav);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + pcm.Length);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);           // PCM
        writer.Write((short)1);           // mono
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);     // byte rate
        writer.Write((short)2);           // block align
        writer.Write((short)16);          // bits
        writer.Write("data"u8);
        writer.Write(pcm.Length);
        writer.Write(pcm);
        return wav;
    }
}
