using System;
using System.IO;
using System.Text;

namespace AgentEscape.VoiceAI
{
    public static class WavEncoder
    {
        /// <summary>-1~1 범위의 float 샘플을 16bit PCM WAV로 변환한다.</summary>
        public static byte[] Encode(float[] samples, int sampleCount, int sampleRate, int channels)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (sampleCount < 0 || sampleCount > samples.Length) throw new ArgumentOutOfRangeException(nameof(sampleCount));
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));

            const int bytesPerSample = 2;
            int dataSize = sampleCount * bytesPerSample;

            using (var stream = new MemoryStream(44 + dataSize))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataSize);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)channels);
                writer.Write(sampleRate);
                writer.Write(sampleRate * channels * bytesPerSample);
                writer.Write((short)(channels * bytesPerSample));
                writer.Write((short)(bytesPerSample * 8));
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(dataSize);

                for (int i = 0; i < sampleCount; i++)
                {
                    float clamped = Math.Max(-1f, Math.Min(1f, samples[i]));
                    writer.Write((short)Math.Round(clamped * short.MaxValue));
                }

                writer.Flush();
                return stream.ToArray();
            }
        }

        /// <summary>음량(RMS). 마이크 권한이 없으면 0에 가깝게 나온다.</summary>
        public static float ComputeRms(float[] samples, int sampleCount)
        {
            if (samples == null || sampleCount <= 0) return 0f;
            double sum = 0;
            for (int i = 0; i < sampleCount; i++) sum += samples[i] * samples[i];
            return (float)Math.Sqrt(sum / sampleCount);
        }
    }
}
