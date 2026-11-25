// Loco Tester - small software mixer on top of the Windows waveOut API.
// Supports 8/16/24/32-bit PCM WAV, any sample rate, mono/stereo, MSTS cue-point loops,
// per-voice pitch and volume.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace LocoTester
{
    public class WavData
    {
        public float[] Samples;     // interleaved
        public int Channels;
        public int Rate;
        public int Frames;
        public List<int> Cues = new List<int>();  // frame offsets
        public string Path;
        public string Error;

        static readonly Dictionary<string, WavData> cache = new Dictionary<string, WavData>(StringComparer.OrdinalIgnoreCase);

        public static WavData Get(string path)
        {
            lock (cache)
            {
                WavData w;
                if (cache.TryGetValue(path, out w)) return w;
            }
            WavData d = Load(path);
            lock (cache) { cache[path] = d; }
            return d;
        }

        public static void ClearCache() { lock (cache) cache.Clear(); }

        /// <summary>Fast check of the WAV header only (used by the health scan). Returns null when playable.</summary>
        public static string CheckHeader(string path)
        {
            try
            {
                byte[] b = new byte[4096];
                int n;
                using (FileStream fs = File.OpenRead(path)) n = fs.Read(b, 0, b.Length);
                if (n < 12 || b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F') return "Not a RIFF/WAV file";
                int pos = 12;
                while (pos + 8 <= n)
                {
                    string id = System.Text.Encoding.ASCII.GetString(b, pos, 4);
                    int sz = BitConverter.ToInt32(b, pos + 4);
                    if (id == "fmt ")
                    {
                        int fmt = BitConverter.ToInt16(b, pos + 8), ch = BitConverter.ToInt16(b, pos + 10), bits = BitConverter.ToInt16(b, pos + 22);
                        if (fmt == -2 && pos + 32 < n) fmt = BitConverter.ToInt16(b, pos + 32);
                        if (fmt != 1 && fmt != 3) return "Unsupported WAV encoding (format " + fmt + ")";
                        if (ch < 1 || ch > 2) return "Unsupported channel count " + ch;
                        if (bits != 8 && bits != 16 && bits != 24 && bits != 32) return "Unsupported bit depth " + bits;
                        return null;
                    }
                    if (sz < 0) break;
                    pos += 8 + sz + (sz & 1);
                }
                return "WAV header has no format block";
            }
            catch (Exception ex) { return ex.Message; }
        }

        public static WavData Load(string path)
        {
            WavData w = new WavData();
            w.Path = path;
            try
            {
                byte[] b = File.ReadAllBytes(path);
                if (b.Length < 12 || b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F') { w.Error = "Not a RIFF/WAV file"; return w; }
                int pos = 12, fmt = 0, bits = 0, dataOff = -1, dataLen = 0;
                while (pos + 8 <= b.Length)
                {
                    string id = System.Text.Encoding.ASCII.GetString(b, pos, 4);
                    int sz = BitConverter.ToInt32(b, pos + 4);
                    if (sz < 0) break;
                    int body = pos + 8;
                    if (id == "fmt ")
                    {
                        fmt = BitConverter.ToInt16(b, body);
                        w.Channels = BitConverter.ToInt16(b, body + 2);
                        w.Rate = BitConverter.ToInt32(b, body + 4);
                        bits = BitConverter.ToInt16(b, body + 14);
                        if (fmt == -2 && sz >= 40) fmt = BitConverter.ToInt16(b, body + 24); // WAVE_FORMAT_EXTENSIBLE
                    }
                    else if (id == "data") { dataOff = body; dataLen = Math.Min(sz, b.Length - body); }
                    else if (id == "cue ")
                    {
                        int n = BitConverter.ToInt32(b, body);
                        for (int i = 0; i < n && body + 4 + i * 24 + 24 <= b.Length; i++)
                            w.Cues.Add(BitConverter.ToInt32(b, body + 4 + i * 24 + 20)); // sample offset
                    }
                    pos = body + sz + (sz & 1);
                }
                if (dataOff < 0) { w.Error = "No audio data"; return w; }
                if (fmt != 1 && fmt != 3) { w.Error = "Unsupported WAV encoding (format " + fmt + ")"; return w; }
                if (w.Channels < 1 || w.Channels > 2) { w.Error = "Unsupported channel count " + w.Channels; return w; }
                int bps = bits / 8;
                if (bps < 1) { w.Error = "Bad bits per sample"; return w; }
                int count = dataLen / bps;
                w.Samples = new float[count];
                for (int i = 0; i < count; i++)
                {
                    int o = dataOff + i * bps;
                    float s;
                    if (fmt == 3 && bps == 4) s = BitConverter.ToSingle(b, o);
                    else if (bps == 1) s = (b[o] - 128) / 128f;
                    else if (bps == 2) s = BitConverter.ToInt16(b, o) / 32768f;
                    else if (bps == 3) s = ((b[o] << 8 | b[o + 1] << 16 | b[o + 2] << 24) >> 8) / 8388608f;
                    else s = BitConverter.ToInt32(b, o) / 2147483648f;
                    w.Samples[i] = s;
                }
                w.Frames = count / w.Channels;
                w.Cues.Sort();
            }
            catch (Exception ex) { w.Error = ex.Message; }
            return w;
        }
    }

    public class Voice
    {
        public WavData Wav;
        public double Pos;           // frame position
        public float Volume = 1;
        public double Pitch = 1;     // playback speed multiplier
        public bool Loop;            // loop between LoopStart and LoopEnd
        public int LoopStart, LoopEnd;
        public bool Releasing;       // finish current pass then stop (or play tail)
        public bool Stopped;
        public float Fade = 1, FadeTarget = 1;
        public float FadeRate = 0.002f;   // per output frame; 0.002 ≈ 11 ms, 0.0001 ≈ 0.23 s
        public object Tag;

        /// <summary>True when the file has an ending part after the loop (MSTS cue points: attack | loop | tail).</summary>
        public bool HasTail { get { return Wav != null && LoopEnd > LoopStart && LoopEnd < Wav.Frames - 256; } }

        /// <summary>The button was released (horn, bell, sander ...), like MSTS / Open Rails:
        /// a sound with cue points stops looping and plays its ending part (with "jump": straight away,
        /// without finishing the current loop pass); a sound without an ending part fades out smoothly.</summary>
        public void Release(bool jump)
        {
            if (Releasing) return;
            Releasing = true;
            if (Loop && HasTail)
            {
                Loop = false;
                if (jump && Pos < LoopEnd) Pos = LoopEnd;
            }
            else if (Loop)
            {
                Loop = false;
                FadeRate = 0.0001f;
                FadeTarget = 0;
            }
        }

        public void Release() { Release(false); }
    }

    public sealed class AudioEngine : IDisposable
    {
        const int Rate = 44100, Frames = 1024, NumBuffers = 4;
        IntPtr hWave = IntPtr.Zero;
        IntPtr[] headers = new IntPtr[NumBuffers];
        IntPtr[] buffers = new IntPtr[NumBuffers];
        readonly List<Voice> voices = new List<Voice>();
        Thread thread;
        volatile bool running;
        public float MasterVolume = 0.8f;
        public string InitError;
        public float PeakLevel;       // for the UI level meter

        [StructLayout(LayoutKind.Sequential)]
        struct WAVEFORMATEX { public short wFormatTag, nChannels; public int nSamplesPerSec, nAvgBytesPerSec; public short nBlockAlign, wBitsPerSample, cbSize; }
        [StructLayout(LayoutKind.Sequential)]
        struct WAVEHDR { public IntPtr lpData; public int dwBufferLength, dwBytesRecorded; public IntPtr dwUser; public int dwFlags, dwLoops; public IntPtr lpNext, reserved; }

        [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr h, int dev, ref WAVEFORMATEX fmt, IntPtr cb, IntPtr inst, int flags);
        [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutUnprepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr h);

        const int WHDR_DONE = 1;

        public AudioEngine()
        {
            try
            {
                WAVEFORMATEX f = new WAVEFORMATEX();
                f.wFormatTag = 1; f.nChannels = 2; f.nSamplesPerSec = Rate; f.wBitsPerSample = 16;
                f.nBlockAlign = 4; f.nAvgBytesPerSec = Rate * 4;
                int r = waveOutOpen(out hWave, -1, ref f, IntPtr.Zero, IntPtr.Zero, 0);
                if (r != 0) { InitError = "No audio output device (error " + r + ")"; hWave = IntPtr.Zero; return; }
                int hsz = Marshal.SizeOf(typeof(WAVEHDR));
                for (int i = 0; i < NumBuffers; i++)
                {
                    buffers[i] = Marshal.AllocHGlobal(Frames * 4);
                    headers[i] = Marshal.AllocHGlobal(hsz);
                    WAVEHDR h = new WAVEHDR();
                    h.lpData = buffers[i]; h.dwBufferLength = Frames * 4; h.dwFlags = 0;
                    Marshal.StructureToPtr(h, headers[i], false);
                    waveOutPrepareHeader(hWave, headers[i], hsz);
                    // mark as done so the loop fills it straight away
                    WAVEHDR h2 = (WAVEHDR)Marshal.PtrToStructure(headers[i], typeof(WAVEHDR));
                    h2.dwFlags |= WHDR_DONE;
                    Marshal.StructureToPtr(h2, headers[i], false);
                }
                running = true;
                thread = new Thread(Loop);
                thread.IsBackground = true;
                thread.Priority = ThreadPriority.AboveNormal;
                thread.Name = "Audio mixer";
                thread.Start();
            }
            catch (Exception ex) { InitError = ex.Message; }
        }

        public Voice Play(WavData w, bool loop, float volume, double pitch, object tag)
        {
            if (w == null || w.Samples == null || w.Frames == 0) return null;
            Voice v = new Voice();
            v.Wav = w; v.Loop = loop; v.Volume = volume; v.Pitch = pitch; v.Tag = tag;
            v.LoopStart = 0; v.LoopEnd = w.Frames;
            lock (voices) voices.Add(v);
            return v;
        }

        /// <summary>Loop between the first two cue points if the file has them (MSTS horns, bells).</summary>
        public Voice PlayCueLoop(WavData w, float volume, double pitch, object tag)
        {
            Voice v = Play(w, true, volume, pitch, tag);
            if (v != null && w.Cues.Count >= 2 && w.Cues[1] > w.Cues[0] && w.Cues[1] <= w.Frames)
            {
                v.LoopStart = w.Cues[0]; v.LoopEnd = w.Cues[1];
            }
            return v;
        }

        public void StopAll()
        {
            lock (voices) { foreach (Voice v in voices) v.Stopped = true; voices.Clear(); }
        }

        public void StopTag(object tag)
        {
            lock (voices) { foreach (Voice v in voices) if (Equals(v.Tag, tag)) v.Stopped = true; voices.RemoveAll(delegate(Voice v) { return v.Stopped; }); }
        }

        public int ActiveVoices { get { lock (voices) return voices.Count; } }

        void Loop()
        {
            float[] mix = new float[Frames * 2];
            short[] pcm = new short[Frames * 2];
            int hsz = Marshal.SizeOf(typeof(WAVEHDR));
            while (running)
            {
                bool wrote = false;
                for (int i = 0; i < NumBuffers && running; i++)
                {
                    WAVEHDR h = (WAVEHDR)Marshal.PtrToStructure(headers[i], typeof(WAVEHDR));
                    if ((h.dwFlags & WHDR_DONE) == 0) continue;
                    Mix(mix);
                    float peak = 0;
                    for (int k = 0; k < mix.Length; k++)
                    {
                        float s = mix[k] * MasterVolume;
                        // soft clip
                        if (s > 1f) s = 1f; else if (s < -1f) s = -1f;
                        float a = s < 0 ? -s : s; if (a > peak) peak = a;
                        pcm[k] = (short)(s * 32000);
                    }
                    PeakLevel = Math.Max(peak, PeakLevel * 0.85f);
                    Marshal.Copy(pcm, 0, buffers[i], pcm.Length);
                    h.dwFlags &= ~WHDR_DONE;
                    Marshal.StructureToPtr(h, headers[i], false);
                    waveOutWrite(hWave, headers[i], hsz);
                    wrote = true;
                }
                if (!wrote) Thread.Sleep(4);
            }
        }

        void Mix(float[] mix)
        {
            Array.Clear(mix, 0, mix.Length);
            Voice[] vs;
            lock (voices) vs = voices.ToArray();
            foreach (Voice v in vs)
            {
                if (v.Stopped) continue;
                WavData w = v.Wav;
                double step = v.Pitch * w.Rate / Rate;
                if (step <= 0) step = 0.0001;
                int ch = w.Channels;
                float[] s = w.Samples;
                // last frame we can interpolate from is Frames-2, so a whole-file loop must wrap at Frames-1.
                // (Wrapping at Frames let the end-of-file check below stop every loop without cue points after
                // one pass: diesel engine sounds such as the WDP4D's went silent after a few seconds.)
                int loopEnd = Math.Min(v.LoopEnd, w.Frames - 1);
                int loopStart = Math.Max(0, Math.Min(v.LoopStart, loopEnd - 1));
                for (int f = 0; f < Frames; f++)
                {
                    // fade smoothing (avoids clicks when volume jumps)
                    v.Fade += (v.FadeTarget - v.Fade) * v.FadeRate;
                    double p = v.Pos;
                    int i0 = (int)p;
                    if (v.Loop && loopEnd > loopStart && i0 >= loopEnd) { v.Pos = loopStart + (p - loopEnd); p = v.Pos; i0 = (int)p; }
                    if (i0 >= w.Frames - 1) { v.Stopped = true; break; }
                    float t = (float)(p - i0);
                    float g = v.Volume * v.Fade;
                    float l, r;
                    if (ch == 1)
                    {
                        float a = s[i0] + (s[i0 + 1] - s[i0]) * t;
                        l = r = a;
                    }
                    else
                    {
                        int k = i0 * 2;
                        l = s[k] + (s[k + 2] - s[k]) * t;
                        r = s[k + 1] + (s[k + 3] - s[k + 1]) * t;
                    }
                    mix[f * 2] += l * g;
                    mix[f * 2 + 1] += r * g;
                    v.Pos = p + step;
                    if (v.FadeTarget <= 0 && v.Fade < 0.001f) { v.Stopped = true; break; }
                }
            }
            lock (voices) voices.RemoveAll(delegate(Voice v) { return v.Stopped; });
        }

        public void Dispose()
        {
            running = false;
            if (thread != null) thread.Join(500);
            if (hWave != IntPtr.Zero)
            {
                waveOutReset(hWave);
                int hsz = Marshal.SizeOf(typeof(WAVEHDR));
                for (int i = 0; i < NumBuffers; i++) { waveOutUnprepareHeader(hWave, headers[i], hsz); Marshal.FreeHGlobal(headers[i]); Marshal.FreeHGlobal(buffers[i]); }
                waveOutClose(hWave);
                hWave = IntPtr.Zero;
            }
        }
    }
}
