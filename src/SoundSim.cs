// Loco Tester - plays an .sms sound group the way Open Rails does (simplified):
// initial / discrete / variable triggers, loops with cue points, volume and frequency curves.
using System;
using System.Collections.Generic;

namespace LocoTester
{
    public class SimState
    {
        public double Speed;        // m/s
        public double Variable1;    // throttle, 0..1
        public double Variable2;    // diesel engine RPM / motor load, 0..1
        public double Variable3;    // dynamic brake, 0..1
        public double BrakeCyl;     // brake cylinder (psi-like 0-100)
        public double Distance;
        public double CurveForce;

        public double Get(string name)
        {
            switch ((name ?? "").ToLowerInvariant())
            {
                case "speed": return Math.Abs(Speed);
                case "variable1": return Variable1;
                case "variable2": return Variable2;
                case "variable3": return Variable3;
                case "brakecyl": return BrakeCyl;
                case "distance": return Distance;
                case "curveforce": return CurveForce;
                default: return 0;
            }
        }

        public double ForCurve(string control)
        {
            string c = (control ?? "").ToLowerInvariant();
            if (c.StartsWith("speed")) return Speed;   // speed curves can be signed
            if (c.StartsWith("variable1")) return Variable1;
            if (c.StartsWith("variable2")) return Variable2;
            if (c.StartsWith("variable3")) return Variable3;
            if (c.StartsWith("brakecyl")) return BrakeCyl;
            if (c.StartsWith("distance")) return Distance;
            if (c.StartsWith("curveforce")) return CurveForce;
            return 0;
        }

        public SimState Clone() { return (SimState)MemberwiseClone(); }
    }

    public class StreamPlayer
    {
        public SmsStream Stream;
        public Voice Current;
        public double StreamVolume = 1;
        public bool[] Disabled;
        public string LastFile;
        public string LastEvent = "";
        public bool IsEngine;      // current voice is an engine loop (follows SoundSim.Spool)
        int seq;
        static readonly Random rnd = new Random();

        public string NextFile(SmsTrigger t)
        {
            if (t.Files.Count == 0) return null;
            if (t.RandomSelection) return t.Files[rnd.Next(t.Files.Count)];
            string f = t.Files[seq % t.Files.Count];
            seq++;
            return f;
        }
    }

    public class SoundSim
    {
        readonly AudioEngine audio;
        readonly SmsFile sms;
        readonly SmsGroup group;
        readonly string orRoot;
        readonly List<StreamPlayer> players = new List<StreamPlayer>();
        SimState last;
        public readonly List<string> Problems = new List<string>();
        public event Action<string> Log;

        // Variable1..3 are 0..1 inside the sim; sound files use either 0..1 or 0..100. Detected per file.
        readonly double[] varScale = { 1, 1, 1, 1 };

        public SoundSim(AudioEngine audio, SmsFile sms, bool cab, string orRoot)
        {
            this.audio = audio;
            this.sms = sms;
            this.orRoot = orRoot;
            group = sms.BestGroup(cab);
            if (group == null) return;
            double[] max = new double[4];
            foreach (SmsStream s in group.Streams)
            {
                StreamPlayer p = new StreamPlayer();
                p.Stream = s;
                p.Disabled = new bool[s.Triggers.Count];
                players.Add(p);
                foreach (SmsTrigger t in s.Triggers) if (t.Kind == TrigKind.Variable) { int vi = VarIndex(t.Variable); if (vi > 0) max[vi] = Math.Max(max[vi], Math.Abs(t.Threshold)); }
                List<SmsCurve> curves = new List<SmsCurve>(s.VolumeCurves);
                if (s.FrequencyCurve != null) curves.Add(s.FrequencyCurve);
                foreach (SmsCurve c in curves) { int vi = VarIndex(c.Control); if (vi > 0) foreach (double x in c.X) max[vi] = Math.Max(max[vi], Math.Abs(x)); }
            }
            for (int i = 1; i <= 3; i++) varScale[i] = max[i] > 1.5 ? 100 : 1;
        }

        static int VarIndex(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            if (n.StartsWith("variable1")) return 1;
            if (n.StartsWith("variable2")) return 2;
            if (n.StartsWith("variable3")) return 3;
            return 0;
        }

        /// <summary>Scale used for VariableN in this file: 1 (0..1) or 100 (percent).</summary>
        public double VarScale(int n) { return varScale[n]; }

        double Value(SimState s, string name) { int vi = VarIndex(name); return vi > 0 ? s.Get(name) * varScale[vi] : s.Get(name); }
        double CurveValue(SimState s, string control) { int vi = VarIndex(control); return vi > 0 ? s.ForCurve(control) * varScale[vi] : s.ForCurve(control); }

        public SmsGroup Group { get { return group; } }
        public IList<StreamPlayer> Players { get { return players; } }

        /// <summary>True between Start() and Stop(): the engine sound is running.</summary>
        public bool Running { get; private set; }

        /// <summary>Engine spin, 0..1: 1 = running normally. Power on ramps it up from 0 (start-up: slow,
        /// low-pitched cranking that rises to idle); power off ramps it down to 0 (run-down).
        /// It scales the volume and pitch of the engine streams (loops started by Initial / Variable triggers).</summary>
        public double Spool = 1;
        /// <summary>Seconds since Start(), used for the starter-motor chug while Spool is low.</summary>
        public double SinceStart;

        public void Start(SimState s)
        {
            Running = true;
            SinceStart = 0;
            last = s.Clone();
            foreach (StreamPlayer p in players)
                for (int i = 0; i < p.Stream.Triggers.Count; i++)
                {
                    SmsTrigger t = p.Stream.Triggers[i];
                    if (t.Kind == TrigKind.Initial) Run(p, t, "start");
                    // variables already past their threshold at start (e.g. speed 0 loops)
                }
            Update(s);
        }

        public void Stop()
        {
            Running = false;
            foreach (StreamPlayer p in players) { if (p.Current != null) p.Current.FadeTarget = 0; p.Current = null; p.IsEngine = false; }
        }

        /// <summary>Fires a discrete trigger (horn, bell, pantograph, brakes ...). Returns how many streams reacted.</summary>
        public int Fire(int trigger)
        {
            int n = 0;
            foreach (StreamPlayer p in players)
                for (int i = 0; i < p.Stream.Triggers.Count; i++)
                {
                    SmsTrigger t = p.Stream.Triggers[i];
                    if (t.Kind == TrigKind.Discrete && t.Discrete == trigger && !p.Disabled[i]) { Run(p, t, TriggerNames.Name(trigger)); n++; }
                }
            return n;
        }

        public bool Has(int trigger)
        {
            foreach (StreamPlayer p in players)
                foreach (SmsTrigger t in p.Stream.Triggers)
                    if (t.Kind == TrigKind.Discrete && t.Discrete == trigger) return true;
            return false;
        }

        public void Update(SimState s)
        {
            if (last == null) last = s.Clone();
            foreach (StreamPlayer p in players)
            {
                for (int i = 0; i < p.Stream.Triggers.Count; i++)
                {
                    SmsTrigger t = p.Stream.Triggers[i];
                    if (t.Kind != TrigKind.Variable || p.Disabled[i]) continue;
                    double a = Value(last, t.Variable), b = Value(s, t.Variable);
                    bool fire = t.Increasing ? (a < t.Threshold && b >= t.Threshold) : (a > t.Threshold && b <= t.Threshold);
                    if (fire) Run(p, t, t.Variable + (t.Increasing ? " > " : " < ") + t.Threshold);
                }
                ApplyCurves(p, s);
            }
            last = s.Clone();
        }

        void ApplyCurves(StreamPlayer p, SimState s)
        {
            if (p.Current == null) return;
            double vol = p.Stream.Volume * group.Volume * p.StreamVolume;
            foreach (SmsCurve c in p.Stream.VolumeCurves) vol *= c.Eval(CurveValue(s, c.Control));
            double pitch = 1;
            if (p.Stream.FrequencyCurve != null && p.Current.Wav != null && p.Current.Wav.Rate > 0)
            {
                double hz = p.Stream.FrequencyCurve.Eval(CurveValue(s, p.Stream.FrequencyCurve.Control));
                if (hz <= 1) hz = p.Current.Wav.Rate;   // curve point "0" means: play at the file's own rate
                pitch = Math.Abs(hz) / p.Current.Wav.Rate;
            }
            if (p.IsEngine && Spool < 0.999)
            {
                double sp = Math.Max(0, Spool);
                // cranking: the starter turns the engine over in pulses until it fires (first ~1.6 s of a start)
                double chug = SinceStart < 1.6 && sp < 0.6 ? 0.75 + 0.25 * Math.Abs(Math.Sin(SinceStart * Math.PI * 3.2)) : 1;
                vol *= Math.Min(1, sp * 1.4) * chug;
                pitch *= 0.72 + 0.28 * sp;     // a real engine turns over slower, not a slowed-down tape
            }
            p.Current.Volume = (float)Math.Max(0, Math.Min(4, vol)) * 0.5f;
            if (p.Stream.FrequencyCurve != null || p.IsEngine) p.Current.Pitch = Math.Max(0.05, Math.Min(4, pitch));
            if (p.Current.Stopped) p.Current = null;
        }

        /// <summary>Re-applies volume / pitch now (used while the engine spools up or down).</summary>
        public void Refresh(SimState s) { foreach (StreamPlayer p in players) ApplyCurves(p, s); }

        void Run(StreamPlayer p, SmsTrigger t, string why)
        {
            switch (t.Command)
            {
                case CmdKind.StartLoop:
                case CmdKind.StartLoopRelease:
                case CmdKind.PlayOneShot:
                {
                    string f = p.NextFile(t);
                    if (f == null) return;
                    string path = sms.ResolveWav(f, orRoot);
                    if (path == null) { Report("Missing sound file: " + f + " (stream " + (p.Stream.Index + 1) + ")"); return; }
                    WavData w = WavData.Get(path);
                    if (w.Error != null) { Report(f + ": " + w.Error); return; }
                    if (p.Current != null && t.Command != CmdKind.PlayOneShot) p.Current.FadeTarget = 0;
                    Voice v;
                    if (t.Command == CmdKind.PlayOneShot) v = audio.Play(w, false, 1, 1, p);
                    else if (t.Command == CmdKind.StartLoopRelease) v = audio.PlayCueLoop(w, 1, 1, p);
                    else v = audio.Play(w, true, 1, 1, p);
                    if (v != null)
                    {
                        v.Fade = t.Command == CmdKind.StartLoop ? 0.3f : 1f;
                        p.Current = v;
                        p.LastFile = f;
                        // engine = loops the sim starts by itself (Initial / Variable triggers), not horns, sanders ...
                        p.IsEngine = t.Command == CmdKind.StartLoop && t.Kind != TrigKind.Discrete;
                    }
                    p.LastEvent = why;
                    if (Log != null) Log(why + " → " + f);
                    if (last != null) ApplyCurves(p, last);
                    break;
                }
                case CmdKind.ReleaseLoopRelease:
                case CmdKind.ReleaseLoopReleaseWithJump:
                    // stop looping and let the sound play to its end (the tail after the cue loop, or
                    // the rest of the file) instead of cutting it off
                    if (p.Current != null) p.Current.Release(t.Command == CmdKind.ReleaseLoopReleaseWithJump);
                    p.LastEvent = why + " (release)";
                    break;
                case CmdKind.SetStreamVolume:
                    p.StreamVolume = t.Value;
                    break;
                case CmdKind.EnableTrigger:
                case CmdKind.DisableTrigger:
                {
                    int idx = (int)t.Value - 1;  // MSTS trigger index is 1-based
                    if (idx >= 0 && idx < p.Disabled.Length) p.Disabled[idx] = t.Command == CmdKind.DisableTrigger;
                    break;
                }
            }
        }

        void Report(string msg)
        {
            if (!Problems.Contains(msg)) Problems.Add(msg);
            if (Log != null) Log("⚠ " + msg);
        }
    }
}
