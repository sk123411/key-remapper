// Key Remapper - remap dead keys on ONE chosen keyboard (e.g. an external USB keyboard)
// without touching the laptop's built-in keyboard.
// Uses the open-source Interception driver (https://github.com/oblitum/Interception),
// which the app downloads and installs for you.
//
// Written in C# 5 so the compiler that ships with every Windows PC can build it.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace KeyRemapperApp
{
    // ---------------------------------------------------------------- native API
    static class Native
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int DevicePredicate(int device);

        [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr interception_create_context();

        [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void interception_destroy_context(IntPtr context);

        [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void interception_set_filter(IntPtr context, DevicePredicate predicate, ushort filter);

        [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int interception_wait_with_timeout(IntPtr context, uint milliseconds);

        [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int interception_receive(IntPtr context, int device, IntPtr stroke, uint nstroke);

        [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int interception_send(IntPtr context, int device, IntPtr stroke, uint nstroke);

        [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern uint interception_get_hardware_id(IntPtr context, int device, byte[] buffer, uint bufferSize);
    }

    // ---------------------------------------------------------------- key names
    // A key id is: scan code (low byte) + 0x100 when it is an "extended" (E0) key.
    class KeyDef
    {
        public string Name;
        public int Id;
        public KeyDef(string name, int id) { Name = name; Id = id; }
        public override string ToString() { return Name; }
    }

    static class KeyTable
    {
        public const int Disabled = 0xFFFF;        // special target: swallow the key
        public static readonly List<KeyDef> All = new List<KeyDef>();
        static readonly Dictionary<int, string> names = new Dictionary<int, string>();

        static void Add(string name, int code, bool ext)
        {
            int id = code | (ext ? 0x100 : 0);
            All.Add(new KeyDef(name, id));
            names[id] = name;
        }

        static KeyTable()
        {
            string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            int[] lc = { 0x1E, 0x30, 0x2E, 0x20, 0x12, 0x21, 0x22, 0x23, 0x17, 0x24, 0x25, 0x26, 0x32,
                         0x31, 0x18, 0x19, 0x10, 0x13, 0x1F, 0x14, 0x16, 0x2F, 0x11, 0x2D, 0x15, 0x2C };
            for (int i = 0; i < 26; i++) Add(letters[i].ToString(), lc[i], false);

            for (int i = 0; i < 10; i++) Add(((i + 1) % 10).ToString(), 0x02 + i, false);

            for (int i = 0; i < 10; i++) Add("F" + (i + 1), 0x3B + i, false);
            Add("F11", 0x57, false);
            Add("F12", 0x58, false);

            Add("Escape", 0x01, false);
            Add("Backspace", 0x0E, false);
            Add("Tab", 0x0F, false);
            Add("Enter", 0x1C, false);
            Add("Space", 0x39, false);
            Add("Caps Lock", 0x3A, false);

            Add("Minus (-)", 0x0C, false);
            Add("Equals (=)", 0x0D, false);
            Add("Left bracket ([)", 0x1A, false);
            Add("Right bracket (])", 0x1B, false);
            Add("Semicolon (;)", 0x27, false);
            Add("Apostrophe (')", 0x28, false);
            Add("Backtick (`)", 0x29, false);
            Add("Backslash (\\)", 0x2B, false);
            Add("Comma (,)", 0x33, false);
            Add("Period (.)", 0x34, false);
            Add("Slash (/)", 0x35, false);

            Add("Left Shift", 0x2A, false);
            Add("Right Shift", 0x36, false);
            Add("Left Ctrl", 0x1D, false);
            Add("Right Ctrl", 0x1D, true);
            Add("Left Alt", 0x38, false);
            Add("Right Alt", 0x38, true);
            Add("Left Windows", 0x5B, true);
            Add("Right Windows", 0x5C, true);
            Add("Menu key", 0x5D, true);

            Add("Print Screen", 0x37, true);
            Add("Scroll Lock", 0x46, false);
            Add("Num Lock", 0x45, false);
            Add("Insert", 0x52, true);
            Add("Delete", 0x53, true);
            Add("Home", 0x47, true);
            Add("End", 0x4F, true);
            Add("Page Up", 0x49, true);
            Add("Page Down", 0x51, true);
            Add("Arrow Up", 0x48, true);
            Add("Arrow Down", 0x50, true);
            Add("Arrow Left", 0x4B, true);
            Add("Arrow Right", 0x4D, true);

            int[] np = { 0x52, 0x4F, 0x50, 0x51, 0x4B, 0x4C, 0x4D, 0x47, 0x48, 0x49 };
            for (int i = 0; i < 10; i++) Add("Numpad " + i, np[i], false);
            Add("Numpad .", 0x53, false);
            Add("Numpad +", 0x4E, false);
            Add("Numpad -", 0x4A, false);
            Add("Numpad *", 0x37, false);
            Add("Numpad /", 0x35, true);
            Add("Numpad Enter", 0x1C, true);
        }

        public static string NameOf(int id)
        {
            if (id == Disabled) return "(disabled - does nothing)";
            string n;
            if (names.TryGetValue(id, out n)) return n;
            return "Key 0x" + id.ToString("X");
        }
    }

    // A "burst": several physical keys that one faulty key press sends almost at once.
    // When all of them arrive together, only Output is typed (or nothing, if Output is Disabled).
    class Pattern
    {
        public List<int> Keys = new List<int>();
        public int Output;
    }

    // ---------------------------------------------------------------- settings file
    class Config
    {
        public string Hwid = "";
        public bool Enabled = true;
        public int BurstMs = 30;
        public Dictionary<int, int> Map = new Dictionary<int, int>();
        public List<Pattern> Patterns = new List<Pattern>();

        static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyRemapper"); }
        }
        static string FilePath { get { return Path.Combine(Dir, "config.txt"); } }

        public static Config Load()
        {
            Config c = new Config();
            try
            {
                if (!File.Exists(FilePath)) return c;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();
                    if (k == "hwid") c.Hwid = v;
                    else if (k == "enabled") c.Enabled = (v != "0");
                    else if (k == "burstms")
                    {
                        int b;
                        if (int.TryParse(v, out b) && b >= 5 && b <= 300) c.BurstMs = b;
                    }
                    else if (k == "pattern")
                    {
                        string[] pp = v.Split('>');
                        if (pp.Length == 2)
                        {
                            Pattern pt = new Pattern();
                            foreach (string ks in pp[0].Split(',')) pt.Keys.Add(Convert.ToInt32(ks, 16));
                            pt.Output = Convert.ToInt32(pp[1], 16);
                            c.Patterns.Add(pt);
                        }
                    }
                    else if (k == "map")
                    {
                        string[] p = v.Split('>');
                        if (p.Length == 2)
                        {
                            int s = Convert.ToInt32(p[0], 16);
                            int d = Convert.ToInt32(p[1], 16);
                            c.Map[s] = d;
                        }
                    }
                }
            }
            catch (Exception) { }
            return c;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                List<string> lines = new List<string>();
                lines.Add("hwid=" + Hwid);
                lines.Add("enabled=" + (Enabled ? "1" : "0"));
                lines.Add("burstms=" + BurstMs);
                foreach (Pattern pt in Patterns)
                {
                    List<string> hex = new List<string>();
                    foreach (int pk in pt.Keys) hex.Add(pk.ToString("X"));
                    lines.Add("pattern=" + string.Join(",", hex.ToArray()) + ">" + pt.Output.ToString("X"));
                }
                foreach (KeyValuePair<int, int> kv in Map)
                    lines.Add("map=" + kv.Key.ToString("X") + ">" + kv.Value.ToString("X"));
                File.WriteAllLines(FilePath, lines.ToArray());
            }
            catch (Exception) { }
        }
    }

    // ---------------------------------------------------------------- remapping engine
    class Engine
    {
        IntPtr ctx = IntPtr.Zero;
        Thread thread;
        volatile bool running;
        static Native.DevicePredicate keyboardPredicate = IsKeyboard;
        static int IsKeyboard(int d) { return (d >= 1 && d <= 10) ? 1 : 0; }

        public volatile bool Enabled = true;
        public volatile string TargetHwid = "";
        volatile Dictionary<int, int> map = new Dictionary<int, int>();
        public Control Ui;

        volatile int captureMode;                 // 0 = off, 1 = detect any keyboard, 2 = capture source key
        Action<int, string> onDetect;
        Action<int> onSource;

        readonly Dictionary<int, string> hw = new Dictionary<int, string>();
        readonly Dictionary<int, int> activeDst = new Dictionary<int, int>();

        // ---- burst ("pattern") filtering state; only touched by the engine thread
        class Held { public int Dev; public ushort Code; public ushort State; public uint Info; }
        static readonly Stopwatch clock = Stopwatch.StartNew();
        static long Now() { return clock.ElapsedMilliseconds; }
        readonly List<Held> pending = new List<Held>();
        long pendingDeadline;
        readonly Dictionary<int, int> swallow = new Dictionary<int, int>();   // physical keys of a matched burst still held
        int swallowOutput;
        long swallowLast;
        bool outputHeld;
        IntPtr scratch = IntPtr.Zero;
        volatile List<Pattern> patterns = new List<Pattern>();
        public volatile int BurstMs = 30;
        readonly List<int> learnBurst = new List<int>();
        long learnStart, learnLast;
        Action<List<int>, int> onPattern;

        public bool Running { get { return running; } }

        public volatile string LastInfo;          // shown live in the window: which device sent which key

        // One physical keyboard often shows up as several HID "interfaces" (MI_00, MI_01, ...)
        // that differ only after the VID/PID part. Compare on VID+PID so they all count as one keyboard.
        static readonly Dictionary<string, string> normCache = new Dictionary<string, string>();
        public static string Norm(string hwid)
        {
            if (string.IsNullOrEmpty(hwid)) return "";
            lock (normCache)
            {
                string n;
                if (normCache.TryGetValue(hwid, out n)) return n;
                Match m = Regex.Match(hwid, "VID_[0-9A-Fa-f]{4}&PID_[0-9A-Fa-f]{4}");
                n = m.Success ? m.Value.ToUpperInvariant() : hwid.ToUpperInvariant();
                normCache[hwid] = n;
                return n;
            }
        }

        public void SetMap(Dictionary<int, int> m) { map = new Dictionary<int, int>(m); }

        // Returns null on success, otherwise a short error code.
        public string Start()
        {
            try { ctx = Native.interception_create_context(); }
            catch (DllNotFoundException) { return "dll"; }
            catch (BadImageFormatException) { return "arch"; }
            catch (Exception ex) { return "err:" + ex.Message; }
            if (ctx == IntPtr.Zero) return "driver";

            Native.interception_set_filter(ctx, keyboardPredicate, 0xFFFF);
            running = true;
            thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Name = "KeyRemapEngine";
            thread.Priority = ThreadPriority.AboveNormal;
            thread.Start();
            return null;
        }

        public void Stop()
        {
            running = false;
            if (thread != null) { thread.Join(1500); thread = null; }
            if (ctx != IntPtr.Zero)
            {
                try { Native.interception_destroy_context(ctx); } catch (Exception) { }
                ctx = IntPtr.Zero;
            }
        }

        public void BeginDetect(Action<int, string> cb) { onDetect = cb; captureMode = 1; }
        public void BeginCaptureSource(Action<int> cb) { onSource = cb; captureMode = 2; }
        public void BeginLearn(Action<int> cb) { onSource = cb; captureMode = 3; }
        public void BeginLearnPattern(Action<List<int>, int> cb) { onPattern = cb; captureMode = 4; }
        public void SetPatterns(List<Pattern> p) { patterns = new List<Pattern>(p); }
        public void CancelCapture() { captureMode = 0; }

        void Post(Action a)
        {
            Control c = Ui;
            if (c != null && c.IsHandleCreated)
            {
                try { c.BeginInvoke(a); } catch (Exception) { }
            }
        }

        string GetHwid(int dev)
        {
            string s;
            if (hw.TryGetValue(dev, out s)) return s;
            s = null;
            try
            {
                byte[] b = new byte[1024];
                uint n = Native.interception_get_hardware_id(ctx, dev, b, (uint)b.Length);
                if (n > 0)
                {
                    s = Encoding.Unicode.GetString(b, 0, (int)Math.Min(n, (uint)b.Length));
                    int z = s.IndexOf('\0');
                    if (z >= 0) s = s.Substring(0, z);
                }
            }
            catch (Exception) { }
            if (string.IsNullOrEmpty(s)) s = "DEVICE#" + dev;
            hw[dev] = s;
            return s;
        }

        void Loop()
        {
            IntPtr buf = Marshal.AllocHGlobal(64);
            scratch = Marshal.AllocHGlobal(64);
            try
            {
                while (running)
                {
                    uint timeout = 250;
                    long now = Now();
                    if (pending.Count > 0)
                    {
                        long left = pendingDeadline - now;
                        if (left <= 0)
                        {
                            try { ResolvePending(); } catch (Exception) { pending.Clear(); }
                            continue;
                        }
                        if (left < timeout) timeout = (uint)left;
                    }
                    if (learnBurst.Count > 0)
                    {
                        if (captureMode != 4) learnBurst.Clear();
                        else if (now - learnLast > 300) { FinishLearnBurst(); continue; }
                        else if (timeout > 40) timeout = 40;
                    }

                    int dev = Native.interception_wait_with_timeout(ctx, timeout);
                    if (dev < 1 || dev > 10) continue;
                    if (Native.interception_receive(ctx, dev, buf, 1) <= 0) continue;

                    bool pass = true;
                    try { pass = Stage(dev, buf); }
                    catch (Exception) { pass = true; }   // on any problem: behave normally

                    if (pass) Native.interception_send(ctx, dev, buf, 1);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
                Marshal.FreeHGlobal(scratch);
                scratch = IntPtr.Zero;
            }
        }

        void SendKey(int dev, int keyId, bool up)
        {
            Marshal.WriteInt16(scratch, 0, (short)(keyId & 0xFF));
            Marshal.WriteInt16(scratch, 2, (short)((up ? 1 : 0) | (((keyId & 0x100) != 0) ? 2 : 0)));
            Marshal.WriteInt32(scratch, 4, 0);
            Native.interception_send(ctx, dev, scratch, 1);
        }

        // Run a held-back stroke through the normal rules and send it on.
        void Replay(Held h)
        {
            Marshal.WriteInt16(scratch, 0, (short)h.Code);
            Marshal.WriteInt16(scratch, 2, (short)h.State);
            Marshal.WriteInt32(scratch, 4, (int)h.Info);
            if (Handle(h.Dev, scratch)) Native.interception_send(ctx, h.Dev, scratch, 1);
        }

        void ReleaseSwallow(int dev)
        {
            if (outputHeld) { SendKey(dev, swallowOutput, true); outputHeld = false; }
            swallow.Clear();
        }

        void FinishLearnBurst()
        {
            List<int> keys = new List<int>(learnBurst);
            int span = (int)(learnLast - learnStart);
            learnBurst.Clear();
            captureMode = 0;
            Action<List<int>, int> cb = onPattern;
            Post(delegate { if (cb != null) cb(keys, span); });
        }

        // Burst filter. A key-down of any key that belongs to a pattern is held for BurstMs.
        // If every key of a pattern arrived in that time, only the pattern's output is typed;
        // otherwise the held keys are released unchanged (just a few ms late).
        bool Stage(int dev, IntPtr buf)
        {
            List<Pattern> pats = patterns;
            if (captureMode != 0 || !Enabled || pats.Count == 0)
            {
                ResolvePending();
                if (outputHeld || swallow.Count > 0) ReleaseSwallow(dev);
                return Handle(dev, buf);
            }

            int code = (ushort)Marshal.ReadInt16(buf, 0);
            int state = (ushort)Marshal.ReadInt16(buf, 2);
            bool up = (state & 1) != 0;
            bool e0 = (state & 2) != 0;
            bool e1 = (state & 4) != 0;
            string hwid = GetHwid(dev);
            bool isTarget = !string.IsNullOrEmpty(TargetHwid) && Norm(hwid) == Norm(TargetHwid);
            if (!isTarget || e1) return Handle(dev, buf);     // other keyboards: never delayed or changed

            int id = code | (e0 ? 0x100 : 0);
            long now = Now();
            if (swallow.Count > 0 && now - swallowLast > 1200) ReleaseSwallow(dev);   // safety: never stay stuck

            if (swallow.ContainsKey(id))                      // belongs to a burst that was already handled
            {
                swallowLast = now;
                if (up)
                {
                    swallow.Remove(id);
                    if (swallow.Count == 0 && outputHeld) { SendKey(dev, swallowOutput, true); outputHeld = false; }
                }
                else if (swallowOutput != KeyTable.Disabled) SendKey(dev, swallowOutput, false);   // key repeat
                return false;
            }

            if (pending.Count == 0)
            {
                if (up) return Handle(dev, buf);
                bool inPattern = false;
                foreach (Pattern p in pats)
                    if (p.Keys.Contains(id)) { inPattern = true; break; }
                if (!inPattern) return Handle(dev, buf);
                pendingDeadline = now + BurstMs;
            }

            Held h = new Held();
            h.Dev = dev;
            h.Code = (ushort)code;
            h.State = (ushort)state;
            h.Info = (uint)Marshal.ReadInt32(buf, 4);
            pending.Add(h);
            return false;                                      // held for now
        }

        void ResolvePending()
        {
            if (pending.Count == 0) return;
            List<Held> items = new List<Held>(pending);
            pending.Clear();

            List<int> downs = new List<int>();
            foreach (Held h in items)
            {
                if ((h.State & 1) != 0) continue;
                int hid = h.Code | (((h.State & 2) != 0) ? 0x100 : 0);
                if (!downs.Contains(hid)) downs.Add(hid);
            }

            Pattern match = null;
            foreach (Pattern p in patterns)
            {
                if (p.Keys.Count < 2) continue;
                bool all = true;
                foreach (int k in p.Keys)
                    if (!downs.Contains(k)) { all = false; break; }
                if (all && (match == null || p.Keys.Count > match.Keys.Count)) match = p;
            }

            if (match == null)
            {
                foreach (Held h in items) Replay(h);
                return;
            }

            int dev0 = items[0].Dev;
            if (outputHeld) { SendKey(dev0, swallowOutput, true); outputHeld = false; }
            swallow.Clear();
            foreach (Held h in items)
            {
                int hid = h.Code | (((h.State & 2) != 0) ? 0x100 : 0);
                bool isUp = (h.State & 1) != 0;
                if (match.Keys.Contains(hid))
                {
                    if (isUp) swallow.Remove(hid); else swallow[hid] = 1;
                }
                else Replay(h);
            }

            swallowOutput = match.Output;
            swallowLast = Now();
            if (match.Output != KeyTable.Disabled)
            {
                SendKey(dev0, match.Output, false);
                if (swallow.Count == 0) SendKey(dev0, match.Output, true);   // already released: a tap
                else outputHeld = true;
            }
        }

        static void Write(IntPtr buf, int dst, int state)
        {
            int ns = (state & ~2) | (((dst & 0x100) != 0) ? 2 : 0);
            Marshal.WriteInt16(buf, 0, (short)(dst & 0xFF));
            Marshal.WriteInt16(buf, 2, (short)ns);
        }

        // Returns true when the (possibly modified) stroke should be passed on.
        bool Handle(int dev, IntPtr buf)
        {
            int code = (ushort)Marshal.ReadInt16(buf, 0);
            int state = (ushort)Marshal.ReadInt16(buf, 2);
            bool up = (state & 1) != 0;
            bool e0 = (state & 2) != 0;
            bool e1 = (state & 4) != 0;
            string hwid = GetHwid(dev);
            int id = code | (e0 ? 0x100 : 0);

            int mode = captureMode;
            if (mode == 1 && !up)
            {
                captureMode = 0;
                Action<int, string> cb = onDetect;
                int d = dev; string h = hwid;
                Post(delegate { if (cb != null) cb(d, h); });
                return false;                      // swallow this key press
            }

            bool isTarget = !string.IsNullOrEmpty(TargetHwid) && Norm(hwid) == Norm(TargetHwid);
            LastInfo = KeyTable.NameOf(id) + (up ? " released" : " pressed") + "  |  device #" + dev +
                       (isTarget ? " [chosen keyboard]" : " [other keyboard - untouched]") + "  " + hwid;

            if (mode == 2 && isTarget && !up && !e1)
            {
                captureMode = 0;
                Action<int> cb2 = onSource;
                int sid = id;
                Post(delegate { if (cb2 != null) cb2(sid); });
                return false;
            }

            if (mode == 3 && isTarget && !e1)      // "learn faulty key": report every key, type nothing
            {
                if (!up)
                {
                    Action<int> cb3 = onSource;
                    int lid = id;
                    Post(delegate { if (cb3 != null) cb3(lid); });
                }
                return false;
            }

            if (mode == 4 && isTarget && !e1)      // "learn pattern": collect the keys of one burst, type nothing
            {
                if (!up)
                {
                    long tnow = Now();
                    if (learnBurst.Count == 0) learnStart = tnow;
                    learnLast = tnow;
                    if (!learnBurst.Contains(id)) learnBurst.Add(id);
                }
                return false;
            }

            if (!isTarget || e1) return true;      // laptop keyboard & everything else: untouched

            int dst;
            if (up)
            {
                if (activeDst.TryGetValue(id, out dst))
                {
                    activeDst.Remove(id);
                    if (dst == KeyTable.Disabled) return false;
                    Write(buf, dst, state);
                }
                return true;
            }

            if (activeDst.TryGetValue(id, out dst))                                            // auto-repeat
            {
                if (dst == KeyTable.Disabled) return false;
                Write(buf, dst, state);
                return true;
            }
            if (Enabled && map.TryGetValue(id, out dst))
            {
                activeDst[id] = dst;
                if (dst == KeyTable.Disabled) return false;                                    // dead key: swallow
                Write(buf, dst, state);
            }
            return true;
        }
    }

    // ---------------------------------------------------------------- driver installer
    static class DriverSetup
    {
        const string Url = "https://github.com/oblitum/Interception/releases/download/v1.0.1/Interception.zip";
        const string Sha256 = "ad038963d6413055765128b0b931f6e765147c9916dba79e65d872b261f9af10";

        public static string AppDir
        {
            get { return Path.GetDirectoryName(Application.ExecutablePath); }
        }
        public static bool DllPresent { get { return File.Exists(Path.Combine(AppDir, "interception.dll")); } }

        // Returns null on success, otherwise an error message.
        public static string Run(bool install, Action<string> log)
        {
            string work = Path.Combine(Path.GetTempPath(), "KeyRemapper_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(work);
                string zip = Path.Combine(work, "Interception.zip");

                log("Downloading the Interception driver package from GitHub...");
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;   // TLS 1.2
                using (WebClient wc = new WebClient()) { wc.DownloadFile(Url, zip); }

                log("Checking the download...");
                string hash;
                using (SHA256 sha = SHA256.Create())
                using (FileStream fs = File.OpenRead(zip))
                {
                    hash = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
                }
                if (hash != Sha256)
                    return "The downloaded file did not match the expected checksum, so nothing was installed.";

                ZipFile.ExtractToDirectory(zip, work);
                string root = Path.Combine(work, "Interception");
                string installer = Path.Combine(Path.Combine(root, "command line installer"), "install-interception.exe");

                if (install)
                {
                    string arch = Environment.Is64BitProcess ? "x64" : "x86";
                    string src = Path.Combine(Path.Combine(Path.Combine(root, "library"), arch), "interception.dll");
                    string dstDll = Path.Combine(AppDir, "interception.dll");
                    try { File.Copy(src, dstDll, true); }
                    catch (Exception) { if (!File.Exists(dstDll)) throw; }   // already loaded = already there
                }

                log(install ? "Installing the driver..." : "Removing the driver...");
                ProcessStartInfo psi = new ProcessStartInfo(installer, install ? "/install" : "/uninstall");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.WorkingDirectory = Path.GetDirectoryName(installer);
                using (Process p = Process.Start(psi))
                {
                    string output = (p.StandardOutput.ReadToEnd() + " " + p.StandardError.ReadToEnd()).Trim();
                    p.WaitForExit();
                    if (p.ExitCode != 0) return "The driver installer reported an error: " + output;
                }
                return null;
            }
            catch (Exception ex) { return ex.Message; }
            finally { try { Directory.Delete(work, true); } catch (Exception) { } }
        }
    }

    // ---------------------------------------------------------------- start with Windows
    static class AutoStart
    {
        const string TaskName = "KeyRemapper";

        static int RunHidden(string file, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(file, args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    return p.ExitCode;
                }
            }
            catch (Exception) { return -1; }
        }

        public static bool IsOn() { return RunHidden("schtasks.exe", "/Query /TN \"" + TaskName + "\"") == 0; }

        // A scheduled task is used because the app needs administrator rights, and a task can
        // start elevated at sign-in without a prompt. Battery restrictions are switched off.
        public static bool Set(bool on)
        {
            if (!on) return RunHidden("schtasks.exe", "/Delete /TN \"" + TaskName + "\" /F") == 0;

            string user = SecurityElement.Escape(Environment.UserDomainName + "\\" + Environment.UserName);
            string exe = SecurityElement.Escape(Application.ExecutablePath);
            string xml =
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
                "  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + user + "</UserId></LogonTrigger></Triggers>\r\n" +
                "  <Principals><Principal id=\"Author\"><UserId>" + user + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\r\n" +
                "  <Settings>\r\n" +
                "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
                "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
                "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
                "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n" +
                "    <Enabled>true</Enabled>\r\n" +
                "  </Settings>\r\n" +
                "  <Actions Context=\"Author\"><Exec><Command>" + exe + "</Command><Arguments>/minimized</Arguments></Exec></Actions>\r\n" +
                "</Task>\r\n";
            string tmp = Path.Combine(Path.GetTempPath(), "KeyRemapperTask.xml");
            try
            {
                File.WriteAllText(tmp, xml, Encoding.Unicode);
                return RunHidden("schtasks.exe", "/Create /TN \"" + TaskName + "\" /XML \"" + tmp + "\" /F") == 0;
            }
            catch (Exception) { return false; }
            finally { try { File.Delete(tmp); } catch (Exception) { } }
        }
    }

    // ---------------------------------------------------------------- "add replacement" dialog
    class AddDialog : Form
    {
        ComboBox cmbSrc, cmbDst;
        Button btnCap;
        Engine engine;
        public int SrcId, DstId;

        public AddDialog(Engine eng, bool canCapture)
        {
            engine = eng;
            Text = "Add a key replacement";
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(470, 230);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            Label l1 = new Label();
            l1.Text = "1) Pick the key on the external keyboard to change (a working spare key, or the dead key itself):";
            l1.SetBounds(14, 12, 444, 18);
            Controls.Add(l1);

            cmbSrc = MakeCombo(14, 34, 230);
            btnCap = new Button();
            btnCap.Text = "Or press it on the keyboard";
            btnCap.SetBounds(254, 32, 204, 28);
            btnCap.Enabled = canCapture;
            btnCap.Click += OnCapture;
            Controls.Add(btnCap);

            Label l2 = new Label();
            l2.Text = "2) What should the PC receive instead? (pick a key, or the first entry to disable the key)";
            l2.SetBounds(14, 82, 444, 18);
            Controls.Add(l2);

            cmbDst = MakeCombo(14, 104, 230);
            cmbDst.Items.Insert(0, new KeyDef("(Disable - do nothing)", KeyTable.Disabled));

            Button ok = new Button();
            ok.Text = "Add";
            ok.SetBounds(262, 180, 90, 32);
            ok.Click += OnOk;
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "Cancel";
            cancel.SetBounds(368, 180, 90, 32);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);
            CancelButton = cancel;
        }

        ComboBox MakeCombo(int x, int y, int w)
        {
            ComboBox c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.SetBounds(x, y, w, 24);
            c.MaxDropDownItems = 16;
            foreach (KeyDef k in KeyTable.All) c.Items.Add(k);
            Controls.Add(c);
            return c;
        }

        void OnCapture(object s, EventArgs e)
        {
            btnCap.Text = "Press the key now...";
            btnCap.Enabled = false;
            engine.BeginCaptureSource(delegate(int id)
            {
                if (IsDisposed) return;
                btnCap.Text = "Or press it on the keyboard";
                btnCap.Enabled = true;
                foreach (object o in cmbSrc.Items)
                {
                    KeyDef k = (KeyDef)o;
                    if (k.Id == id) { cmbSrc.SelectedItem = o; return; }
                }
                MessageBox.Show(this, "That key is not in the list (code 0x" + id.ToString("X") + ").", "Key Remapper");
            });
        }

        void OnOk(object s, EventArgs e)
        {
            if (cmbSrc.SelectedItem == null || cmbDst.SelectedItem == null)
            {
                MessageBox.Show(this, "Please pick both keys.", "Key Remapper");
                return;
            }
            SrcId = ((KeyDef)cmbSrc.SelectedItem).Id;
            DstId = ((KeyDef)cmbDst.SelectedItem).Id;
            if (SrcId == DstId)
            {
                MessageBox.Show(this, "The two keys are the same.", "Key Remapper");
                return;
            }
            DialogResult = DialogResult.OK;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            engine.CancelCapture();
            base.OnFormClosed(e);
        }
    }

    // ---------------------------------------------------------------- "which key to type for this burst"
    class PatternDialog : Form
    {
        ComboBox cmb;
        public int Output;

        public PatternDialog(List<int> keys)
        {
            Text = "Faulty key pattern";
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(470, 190);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < keys.Count; i++)
            {
                if (i > 0) sb.Append(" + ");
                sb.Append(KeyTable.NameOf(keys[i]));
            }
            Label l1 = new Label();
            l1.Text = "One press of the faulty key sent these keys together:\r\n" + sb.ToString();
            l1.SetBounds(14, 12, 444, 40);
            Controls.Add(l1);

            Label l2 = new Label();
            l2.Text = "Whenever this exact burst happens, type only:";
            l2.SetBounds(14, 66, 444, 18);
            Controls.Add(l2);

            cmb = new ComboBox();
            cmb.DropDownStyle = ComboBoxStyle.DropDownList;
            cmb.SetBounds(14, 90, 230, 24);
            foreach (int k in keys) cmb.Items.Add(new KeyDef(KeyTable.NameOf(k), k));
            cmb.Items.Add(new KeyDef("(nothing)", KeyTable.Disabled));
            cmb.SelectedIndex = 0;
            Controls.Add(cmb);

            Button ok = new Button();
            ok.Text = "Add";
            ok.SetBounds(262, 140, 90, 32);
            ok.Click += delegate
            {
                Output = ((KeyDef)cmb.SelectedItem).Id;
                DialogResult = DialogResult.OK;
            };
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "Cancel";
            cancel.SetBounds(368, 140, 90, 32);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);
            CancelButton = cancel;
        }
    }

    // ---------------------------------------------------------------- main window
    class MainForm : Form
    {
        Config cfg;
        Engine engine = new Engine();
        string engineError;
        NotifyIcon tray;
        ToolStripMenuItem trayEnable;
        bool allowShow;
        bool exiting;
        bool balloonShown;

        Label lblDriver, lblDevice, lblStatus, lblLast;
        System.Windows.Forms.Timer liveTimer;
        Button btnInstall, btnUninstall, btnDetect, btnAdd, btnRemove, btnLearn, btnLearnPat;
        NumericUpDown numBurst;
        System.Windows.Forms.Timer patTimer;
        bool learning;
        System.Windows.Forms.Timer learnTimer;
        ListView list;
        CheckBox chkEnable, chkAuto;
        TextBox txtTest;
        System.Windows.Forms.Timer captureTimer;

        public MainForm(bool startHidden)
        {
            allowShow = !startHidden;
            cfg = Config.Load();
            BuildUi();
            BuildTray();
            IntPtr h = this.Handle;      // make sure the window exists even when started hidden
            engine.Ui = this;
            engine.Enabled = cfg.Enabled;
            engine.TargetHwid = cfg.Hwid;
            engine.SetMap(cfg.Map);
            engine.SetPatterns(cfg.Patterns);
            engine.BurstMs = cfg.BurstMs;
            engineError = engine.Start();
            chkAuto.Checked = AutoStart.IsOn();
            chkAuto.CheckedChanged += OnAutoChanged;
            RefreshAll();

            liveTimer = new System.Windows.Forms.Timer();
            liveTimer.Interval = 200;
            liveTimer.Tick += delegate
            {
                string info = engine.LastInfo;
                if (info != null && Visible) lblLast.Text = "Last key seen: " + info;
            };
            liveTimer.Start();
        }

        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(allowShow && value);
        }

        // ---- UI construction
        Button MakeButton(string text, int x, int y, int w, int h, EventHandler click)
        {
            Button b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, h);
            b.Click += click;
            return b;
        }

        void BuildUi()
        {
            Text = "Key Remapper - fix dead keys on an external keyboard";
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(700, 636);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            GroupBox g1 = new GroupBox();
            g1.Text = "Step 1 - Keyboard driver (one time)";
            g1.SetBounds(12, 10, 676, 78);
            Controls.Add(g1);
            lblDriver = new Label();
            lblDriver.SetBounds(14, 22, 440, 50);
            g1.Controls.Add(lblDriver);
            btnInstall = MakeButton("Install driver", 462, 28, 100, 32, OnInstall);
            g1.Controls.Add(btnInstall);
            btnUninstall = MakeButton("Uninstall", 568, 28, 94, 32, OnUninstall);
            g1.Controls.Add(btnUninstall);

            GroupBox g2 = new GroupBox();
            g2.Text = "Step 2 - Which keyboard has the dead keys?";
            g2.SetBounds(12, 96, 676, 82);
            Controls.Add(g2);
            lblDevice = new Label();
            lblDevice.SetBounds(14, 22, 440, 50);
            g2.Controls.Add(lblDevice);
            btnDetect = MakeButton("Detect my external keyboard", 462, 26, 200, 36, OnDetect);
            g2.Controls.Add(btnDetect);

            GroupBox g3 = new GroupBox();
            g3.Text = "Step 3 - Key replacements (only apply to that keyboard)";
            g3.SetBounds(12, 186, 676, 236);
            Controls.Add(g3);
            list = new ListView();
            list.View = View.Details;
            list.FullRowSelect = true;
            list.HideSelection = false;
            list.MultiSelect = true;
            list.Columns.Add("When I press (working key)", 250);
            list.Columns.Add("The PC receives instead", 250);
            list.SetBounds(14, 24, 516, 196);
            g3.Controls.Add(list);
            btnAdd = MakeButton("Add...", 542, 24, 120, 32, OnAdd);
            g3.Controls.Add(btnAdd);
            btnRemove = MakeButton("Remove selected", 542, 62, 120, 32, OnRemove);
            g3.Controls.Add(btnRemove);
            Label hint = new Label();
            hint.Text = "One press sends a burst of keys? Use 'Learn pattern'.";
            hint.SetBounds(542, 176, 124, 56);
            btnLearn = MakeButton("Learn faulty key", 542, 100, 120, 32, OnLearn);
            g3.Controls.Add(btnLearn);
            btnLearnPat = MakeButton("Learn pattern", 542, 138, 120, 32, OnLearnPattern);
            g3.Controls.Add(btnLearnPat);
            g3.Controls.Add(hint);

            chkEnable = new CheckBox();
            chkEnable.Text = "Remapping ON  (untick = every key behaves normally)";
            chkEnable.SetBounds(16, 432, 500, 22);
            chkEnable.Checked = cfg.Enabled;
            chkEnable.CheckedChanged += OnEnableChanged;
            Controls.Add(chkEnable);

            Label lb = new Label();
            lb.Text = "Burst window (ms):";
            lb.SetBounds(520, 436, 112, 18);
            Controls.Add(lb);
            numBurst = new NumericUpDown();
            numBurst.Minimum = 5;
            numBurst.Maximum = 300;
            numBurst.Value = Math.Max(5, Math.Min(300, cfg.BurstMs));
            numBurst.SetBounds(634, 432, 52, 24);
            numBurst.ValueChanged += OnBurstChanged;
            Controls.Add(numBurst);

            chkAuto = new CheckBox();
            chkAuto.Text = "Start automatically when I sign in to Windows";
            chkAuto.SetBounds(16, 458, 500, 22);
            Controls.Add(chkAuto);

            Label lt = new Label();
            lt.Text = "Test area - click here, then type on the external keyboard:";
            lt.SetBounds(16, 490, 600, 18);
            Controls.Add(lt);
            txtTest = new TextBox();
            txtTest.Multiline = true;
            txtTest.SetBounds(16, 512, 668, 58);
            Controls.Add(txtTest);

            lblLast = new Label();
            lblLast.SetBounds(16, 574, 668, 18);
            lblLast.ForeColor = Color.FromArgb(70, 70, 70);
            Controls.Add(lblLast);

            lblStatus = new Label();
            lblStatus.SetBounds(16, 596, 668, 36);
            Controls.Add(lblStatus);
        }

        void BuildTray()
        {
            tray = new NotifyIcon();
            tray.Icon = SystemIcons.Application;
            tray.Text = "Key Remapper";
            tray.Visible = true;
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem open = new ToolStripMenuItem("Open Key Remapper");
            open.Click += delegate { ShowWindow(); };
            trayEnable = new ToolStripMenuItem("Remapping ON");
            trayEnable.CheckOnClick = true;
            trayEnable.Checked = cfg.Enabled;
            trayEnable.Click += delegate { chkEnable.Checked = trayEnable.Checked; };
            ToolStripMenuItem exit = new ToolStripMenuItem("Exit (keys go back to normal)");
            exit.Click += delegate { ExitApp(); };
            menu.Items.Add(open);
            menu.Items.Add(trayEnable);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exit);
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { ShowWindow(); };
        }

        void ShowWindow()
        {
            allowShow = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        void ExitApp()
        {
            exiting = true;
            engine.Stop();
            tray.Visible = false;
            tray.Dispose();
            Application.Exit();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                if (!balloonShown)
                {
                    balloonShown = true;
                    tray.ShowBalloonTip(4000, "Key Remapper",
                        "Still running in the background. Right-click this icon and choose Exit to stop it.",
                        ToolTipIcon.Info);
                }
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            engine.Stop();
            base.OnFormClosed(e);
        }

        // ---- helpers
        public void SetStatus(string s)
        {
            if (!IsHandleCreated) return;
            if (InvokeRequired) { try { BeginInvoke(new Action<string>(SetStatus), s); } catch (Exception) { } return; }
            lblStatus.Text = s;
        }

        void SetBusy(bool busy)
        {
            btnInstall.Enabled = !busy;
            btnUninstall.Enabled = !busy;
            btnDetect.Enabled = !busy && engine.Running;
            UseWaitCursor = busy;
        }

        void RefreshAll()
        {
            if (engine.Running)
            {
                lblDriver.ForeColor = Color.FromArgb(0, 120, 40);
                lblDriver.Text = "Driver is installed and running.";
            }
            else
            {
                lblDriver.ForeColor = Color.FromArgb(180, 30, 30);
                if (engineError == "driver")
                    lblDriver.Text = "Driver is not active. If you just installed it, restart Windows once. " +
                                     "If you already restarted, see the README (Windows 'Memory integrity' can block it).";
                else if (engineError == "arch")
                    lblDriver.Text = "interception.dll does not match this Windows. Click 'Install driver' to repair.";
                else if (engineError != null && engineError.StartsWith("err:"))
                    lblDriver.Text = "Could not start: " + engineError.Substring(4);
                else
                    lblDriver.Text = "The keyboard driver is not installed yet. Click 'Install driver'.";
            }

            if (string.IsNullOrEmpty(cfg.Hwid))
            {
                lblDevice.ForeColor = Color.FromArgb(180, 90, 0);
                lblDevice.Text = "No keyboard chosen yet. Click the button, then press any key on the EXTERNAL keyboard.";
            }
            else
            {
                lblDevice.ForeColor = Color.FromArgb(0, 120, 40);
                lblDevice.Text = "Chosen keyboard: " + cfg.Hwid + "\r\nOnly this keyboard is ever changed.";
            }

            btnDetect.Enabled = engine.Running;
            btnAdd.Enabled = engine.Running && !learning;
            btnLearn.Enabled = engine.Running;
            btnLearnPat.Enabled = engine.Running;
            RefreshList();
            if (lblStatus.Text.Length == 0)
                lblStatus.Text = "Your laptop keyboard is never modified. Closing this window keeps the app running in the tray.";
        }

        void RefreshList()
        {
            list.Items.Clear();
            List<int> keys = new List<int>(cfg.Map.Keys);
            keys.Sort();
            foreach (int k in keys)
            {
                ListViewItem it = new ListViewItem(KeyTable.NameOf(k));
                it.SubItems.Add(KeyTable.NameOf(cfg.Map[k]));
                it.Tag = k;
                list.Items.Add(it);
            }
            foreach (Pattern p in cfg.Patterns)
            {
                StringBuilder sb = new StringBuilder("Burst: ");
                for (int i = 0; i < p.Keys.Count; i++)
                {
                    if (i > 0) sb.Append(" + ");
                    sb.Append(KeyTable.NameOf(p.Keys[i]));
                }
                ListViewItem pit = new ListViewItem(sb.ToString());
                pit.SubItems.Add(p.Output == KeyTable.Disabled ? "(nothing)" : "only " + KeyTable.NameOf(p.Output));
                pit.Tag = p;
                list.Items.Add(pit);
            }
        }

        void Apply()
        {
            cfg.Save();
            engine.Enabled = cfg.Enabled;
            engine.TargetHwid = cfg.Hwid;
            engine.SetMap(cfg.Map);
            engine.SetPatterns(cfg.Patterns);
            engine.BurstMs = cfg.BurstMs;
            RefreshList();
        }

        // ---- events
        void OnInstall(object s, EventArgs e)
        {
            if (MessageBox.Show(this,
                    "This downloads the open-source 'Interception' keyboard driver from GitHub (its checksum is verified) " +
                    "and installs it. Windows must be restarted afterwards.\r\n\r\nContinue?",
                    "Install driver", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            SetBusy(true);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err = DriverSetup.Run(true, SetStatus);
                BeginInvoke(new Action(delegate
                {
                    SetBusy(false);
                    if (err != null)
                    {
                        SetStatus("Install failed: " + err);
                        MessageBox.Show(this, err, "Install failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    SetStatus("Driver installed. Restart Windows to activate it.");
                    if (MessageBox.Show(this, "The driver is installed. Windows must restart to activate it.\r\n\r\nRestart now?",
                            "Restart needed", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    {
                        try { Process.Start("shutdown.exe", "/r /t 10 /c \"Restarting to activate the keyboard driver\""); }
                        catch (Exception) { }
                    }
                }));
            });
        }

        void OnUninstall(object s, EventArgs e)
        {
            if (MessageBox.Show(this, "Remove the keyboard driver? Remapping will stop working until you install it again.",
                    "Uninstall driver", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            SetBusy(true);
            engine.Stop();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err = DriverSetup.Run(false, SetStatus);
                BeginInvoke(new Action(delegate
                {
                    SetBusy(false);
                    if (err != null) MessageBox.Show(this, err, "Uninstall failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    else MessageBox.Show(this, "Driver removed. Restart Windows to finish.", "Done");
                    SetStatus("Driver removed. Restart Windows to finish.");
                    engineError = "driver";
                    RefreshAll();
                }));
            });
        }

        void OnDetect(object s, EventArgs e)
        {
            if (!engine.Running) return;
            btnDetect.Enabled = false;
            btnDetect.Text = "Press any key on the EXTERNAL keyboard...";
            engine.BeginDetect(delegate(int dev, string hwid)
            {
                if (captureTimer != null) captureTimer.Stop();
                cfg.Hwid = hwid;
                Apply();
                btnDetect.Text = "Detect my external keyboard";
                RefreshAll();
                SetStatus("Detected keyboard #" + dev + ". If you pressed a key on the LAPTOP keyboard by mistake, click Detect again.");
            });

            if (captureTimer == null)
            {
                captureTimer = new System.Windows.Forms.Timer();
                captureTimer.Interval = 20000;
                captureTimer.Tick += delegate
                {
                    captureTimer.Stop();
                    engine.CancelCapture();
                    btnDetect.Text = "Detect my external keyboard";
                    btnDetect.Enabled = engine.Running;
                    SetStatus("No key was pressed - detection cancelled.");
                };
            }
            captureTimer.Stop();
            captureTimer.Start();
        }

        void OnAdd(object s, EventArgs e)
        {
            if (string.IsNullOrEmpty(cfg.Hwid))
            {
                MessageBox.Show(this, "Do step 2 first: click 'Detect my external keyboard'.", "Key Remapper");
                return;
            }
            using (AddDialog d = new AddDialog(engine, engine.Running))
            {
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    cfg.Map[d.SrcId] = d.DstId;
                    Apply();
                    SetStatus("Added: " + KeyTable.NameOf(d.SrcId) + " -> " + KeyTable.NameOf(d.DstId) + ". Try it in the test area.");
                }
            }
        }

        void OnBurstChanged(object s, EventArgs e)
        {
            cfg.BurstMs = (int)numBurst.Value;
            Apply();
        }

        void OnLearnPattern(object s, EventArgs e)
        {
            if (!engine.Running || string.IsNullOrEmpty(cfg.Hwid))
            {
                MessageBox.Show(this, "Install the driver and do step 2 first.", "Key Remapper");
                return;
            }
            if (learning) StopLearning();
            btnLearnPat.Enabled = false;
            SetStatus("Press the faulty key ONCE now. (Other keys on the external keyboard type nothing for a moment.)");
            engine.BeginLearnPattern(delegate(List<int> keys, int span)
            {
                if (patTimer != null) patTimer.Stop();
                btnLearnPat.Enabled = engine.Running;
                if (keys.Count < 2)
                {
                    SetStatus("That press produced only one key (" + KeyTable.NameOf(keys[0]) +
                              "), so there is no burst to match. If the key sends a different key each time, use 'Learn faulty key' instead.");
                    return;
                }
                using (PatternDialog d = new PatternDialog(keys))
                {
                    if (d.ShowDialog(this) != DialogResult.OK) { SetStatus("Pattern not added."); return; }
                    Pattern p = new Pattern();
                    p.Keys = keys;
                    p.Output = d.Output;
                    cfg.Patterns.Add(p);
                    int suggest = Math.Min(150, Math.Max(15, span * 2 + 10));
                    if (suggest > cfg.BurstMs) { cfg.BurstMs = suggest; numBurst.Value = suggest; }
                    Apply();
                    SetStatus("Pattern added. The burst lasted " + span + " ms; the burst window is " + cfg.BurstMs + " ms.");
                }
            });
            if (patTimer == null)
            {
                patTimer = new System.Windows.Forms.Timer();
                patTimer.Interval = 20000;
                patTimer.Tick += delegate
                {
                    patTimer.Stop();
                    engine.CancelCapture();
                    btnLearnPat.Enabled = engine.Running;
                    SetStatus("No key was pressed - pattern learning cancelled.");
                };
            }
            patTimer.Stop();
            patTimer.Start();
        }

        void StopLearning()
        {
            if (!learning) return;
            learning = false;
            if (learnTimer != null) learnTimer.Stop();
            engine.CancelCapture();
            btnLearn.Text = "Learn faulty key";
            btnAdd.Enabled = engine.Running;
            SetStatus("Stopped learning. Keys on the external keyboard work normally again.");
        }

        void OnLearn(object s, EventArgs e)
        {
            if (learning) { StopLearning(); return; }
            if (string.IsNullOrEmpty(cfg.Hwid))
            {
                MessageBox.Show(this, "Do step 2 first: click 'Detect my external keyboard'.", "Key Remapper");
                return;
            }
            learning = true;
            btnLearn.Text = "Stop learning";
            btnAdd.Enabled = false;
            SetStatus("LEARNING: press ONLY the faulty key a few times (all other keys on the external keyboard type nothing right now). Click 'Stop learning' when done. Stops by itself after 60 seconds.");
            engine.BeginLearn(delegate(int id)
            {
                if (!learning) return;
                if (cfg.Map.ContainsKey(id)) return;          // already handled by a rule
                cfg.Map[id] = KeyTable.Disabled;
                Apply();
                SetStatus("LEARNING: disabled " + KeyTable.NameOf(id) + ". Keep pressing the faulty key to catch more, then click 'Stop learning'.");
            });
            if (learnTimer == null)
            {
                learnTimer = new System.Windows.Forms.Timer();
                learnTimer.Interval = 60000;
                learnTimer.Tick += delegate { StopLearning(); };
            }
            learnTimer.Stop();
            learnTimer.Start();
        }

        void OnRemove(object s, EventArgs e)
        {
            foreach (ListViewItem it in list.SelectedItems)
            {
                if (it.Tag is Pattern) cfg.Patterns.Remove((Pattern)it.Tag);
                else cfg.Map.Remove((int)it.Tag);
            }
            Apply();
        }

        void OnEnableChanged(object s, EventArgs e)
        {
            cfg.Enabled = chkEnable.Checked;
            trayEnable.Checked = chkEnable.Checked;
            Apply();
            SetStatus(cfg.Enabled ? "Remapping is ON." : "Remapping is OFF - all keys behave normally.");
        }

        void OnAutoChanged(object s, EventArgs e)
        {
            bool want = chkAuto.Checked;
            if (!AutoStart.Set(want))
            {
                chkAuto.CheckedChanged -= OnAutoChanged;
                chkAuto.Checked = !want;
                chkAuto.CheckedChanged += OnAutoChanged;
                MessageBox.Show(this, "Could not change the start-up setting.", "Key Remapper");
            }
        }
    }

    // ---------------------------------------------------------------- entry point
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool created;
            using (Mutex m = new Mutex(true, "Global\\KeyRemapperSingleInstance", out created))
            {
                if (!created)
                {
                    MessageBox.Show("Key Remapper is already running (look for its icon in the system tray near the clock).",
                        "Key Remapper");
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                bool hidden = false;
                foreach (string a in args)
                    if (string.Equals(a, "/minimized", StringComparison.OrdinalIgnoreCase)) hidden = true;
                Application.Run(new MainForm(hidden));
            }
        }
    }
}
