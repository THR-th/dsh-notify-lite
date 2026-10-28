using System;
using System.IO;
using System.Media;
using System.Text;

namespace DshNotifyPet
{
    public sealed class ReminderSound : IDisposable
    {
        private readonly object gate = new object();
        private SoundPlayer player;
        private MemoryStream stream;
        private bool disposed;

        public bool TryPlay(int volume, bool attention)
        {
            lock (gate)
            {
                if (disposed) return false;
                StopCore();
                volume = Math.Max(0, Math.Min(100, volume));
                if (volume == 0) return true;
                try
                {
                    stream = new MemoryStream(BuildWave(volume, attention), false);
                    player = new SoundPlayer(stream);
                    // Loading is bounded to our tiny in-memory PCM, then playback is asynchronous.
                    player.Load();
                    player.Play();
                    return true;
                }
                catch
                {
                    StopCore();
                    return false;
                }
            }
        }

        public void Stop() { lock (gate) { StopCore(); } }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                StopCore();
                disposed = true;
            }
        }

        private void StopCore()
        {
            if (player != null)
            {
                try { player.Stop(); } catch { }
                try { player.Dispose(); } catch { }
                player = null;
            }
            if (stream != null)
            {
                try { stream.Dispose(); } catch { }
                stream = null;
            }
        }

        internal static byte[] BuildWave(int volume, bool attention)
        {
            const int sampleRate = 22050;
            const int bitsPerSample = 16;
            volume = Math.Max(0, Math.Min(100, volume));
            double firstDuration = attention ? 0.20 : 0.16;
            double gap = attention ? 0.07 : 0.065;
            double secondDuration = attention ? 0.24 : 0.22;
            double firstFrequency = attention ? 660.0 : 523.25;
            double secondFrequency = attention ? 880.0 : 783.99;
            int sampleCount = (int)Math.Ceiling((firstDuration + gap + secondDuration) * sampleRate);
            int dataBytes = sampleCount * 2;
            using (MemoryStream data = new MemoryStream(44 + dataBytes))
            using (BinaryWriter writer = new BinaryWriter(data, Encoding.ASCII))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataBytes);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(sampleRate);
                writer.Write(sampleRate * 2);
                writer.Write((short)2);
                writer.Write((short)bitsPerSample);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(dataBytes);
                double amplitude = 0.32 * Int16.MaxValue * (volume / 100.0);
                for (int i = 0; i < sampleCount; i++)
                {
                    double time = i / (double)sampleRate;
                    double sample = 0;
                    if (time < firstDuration) sample = Tone(time, firstDuration, firstFrequency);
                    else if (time >= firstDuration + gap) sample = Tone(time - firstDuration - gap, secondDuration, secondFrequency);
                    writer.Write((short)Math.Round(amplitude * sample));
                }
                writer.Flush();
                return data.ToArray();
            }
        }

        private static double Tone(double time, double duration, double frequency)
        {
            if (time < 0 || time >= duration) return 0;
            double attack = Math.Min(1.0, time / 0.016);
            double release = Math.Min(1.0, (duration - time) / 0.065);
            double envelope = Math.Max(0, Math.Min(attack, release));
            // Smooth edges keep the brief two-tone chime free of abrupt clicks.
            envelope = envelope * envelope * (3.0 - 2.0 * envelope);
            return Math.Sin(2.0 * Math.PI * frequency * time) * envelope;
        }
    }
}