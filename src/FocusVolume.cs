// Focus Volume: a dial controls the volume of the app that owns the foreground window.
// Works as a plugin for both Elgato Stream Deck (+) and MiraBox Stream Dock.
// Event-driven only: nothing polls, so idle CPU use is zero.
// Builds with the C# 5 compiler that ships with .NET Framework 4.x; no dependencies.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

class Settings
{
    public int Step = 5;      // percent per dial tick
    public bool Osd = true;   // show the on-screen display
}

static class L
{
    public static bool Zh = false; // English unless the host app runs in Chinese
    public static string Muted { get { return Zh ? "已静音" : "Muted"; } }
    public static string NoSession { get { return Zh ? "没有音频会话" : "No audio session"; } }
    public static string Unknown { get { return Zh ? "未知应用" : "Unknown app"; } }
    public static string PluginName { get { return Zh ? "前台应用音量" : "Focus Volume"; } }
}

static class Program
{
    static Osd osd;
    static ClientWebSocket ws;
    static readonly object sendLock = new object();
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    static readonly Dictionary<string, Settings> Contexts = new Dictionary<string, Settings>();
    static readonly HashSet<string> TouchDials = new HashSet<string>(); // Stream Deck+ dials (have a touch strip)
    static int lastMuteTick = -10000;
    static System.Windows.Forms.Timer trimTimer;

    [STAThread]
    static int Main(string[] args)
    {
        Native.SetProcessDPIAware();
        if (args.Length > 0 && args[0] == "--test") return TestMode(args);

        string port = Arg(args, "-port"), uuid = Arg(args, "-pluginUUID"), reg = Arg(args, "-registerEvent");
        if (port == null || uuid == null || reg == null) return 1;
        ReadLanguage(Arg(args, "-info"));

        osd = new Osd();
        IntPtr h = osd.Handle; // create the handle on the UI thread so BeginInvoke can marshal to it
        // A few seconds after the last event, hand unused memory back to Windows
        trimTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        trimTimer.Tick += delegate { trimTimer.Stop(); Native.SetProcessWorkingSetSize(Native.GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1)); };
        trimTimer.Start();
        var t = new Thread(delegate () { SocketLoop(port, uuid, reg); });
        t.IsBackground = true;
        t.Start();
        Application.Run();
        return 0;
    }

    static string Arg(string[] a, string name)
    {
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
        return null;
    }

    // Both hosts pass {"application":{"language":"zh_CN",...}} in -info
    static void ReadLanguage(string info)
    {
        try
        {
            var app = Get(Json.Deserialize<Dictionary<string, object>>(info), "application") as Dictionary<string, object>;
            var lang = Get(app, "language") as string;
            if (!string.IsNullOrEmpty(lang)) L.Zh = lang.StartsWith("zh");
        }
        catch { }
    }

    // ---------- host connection ----------

    static void SocketLoop(string port, string uuid, string reg)
    {
        try
        {
            ws = new ClientWebSocket();
            ws.ConnectAsync(new Uri("ws://127.0.0.1:" + port), CancellationToken.None).Wait();
            Send(new Dictionary<string, object> { { "event", reg }, { "uuid", uuid } });

            var buf = new byte[16384];
            var ms = new MemoryStream();
            while (ws.State == WebSocketState.Open)
            {
                var r = ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None).Result;
                if (r.MessageType == WebSocketMessageType.Close) break;
                ms.Write(buf, 0, r.Count);
                if (!r.EndOfMessage) continue;
                string msg = Encoding.UTF8.GetString(ms.ToArray());
                ms.SetLength(0);
                osd.BeginInvoke((Action)delegate { Handle(msg); });
            }
        }
        catch (Exception e) { Log(e.ToString()); }
        // The host restarts the plugin when it needs it again
        try { osd.BeginInvoke((Action)Application.Exit); } catch { Environment.Exit(0); }
    }

    static void Send(Dictionary<string, object> msg)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(Json.Serialize(msg));
            lock (sendLock)
                ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).Wait();
        }
        catch (Exception e) { Log(e.ToString()); }
    }

    static void Handle(string msg)
    {
        trimTimer.Stop();
        trimTimer.Start();
        try
        {
            var m = Json.Deserialize<Dictionary<string, object>>(msg);
            string ev = Get(m, "event") as string;
            string context = (Get(m, "context") as string) ?? "";
            var payload = Get(m, "payload") as Dictionary<string, object>;
            if (payload != null && payload.ContainsKey("settings"))
                Contexts[context] = ParseSettings(payload["settings"] as Dictionary<string, object>);
            if ((Get(payload, "controller") as string) == "Encoder") TouchDials.Add(context);

            Settings s;
            if (!Contexts.TryGetValue(context, out s)) s = new Settings();

            switch (ev)
            {
                case "willAppear":
                    if (TouchDials.Contains(context)) Feedback(context, L.PluginName, "", -1, false);
                    break;
                case "dialRotate":
                    int ticks = Convert.ToInt32(Get(payload, "ticks") ?? 0, CultureInfo.InvariantCulture);
                    if (ticks != 0) Rotate(s, ticks, null, context);
                    break;
                case "dialPress": // older Stream Dock firmware: one event with pressed true/false
                    object pressed = Get(payload, "pressed");
                    if (pressed is bool && !(bool)pressed) break;
                    ToggleMuteOnce(s, context);
                    break;
                case "dialDown":
                case "touchTap":
                case "keyDown":
                    ToggleMuteOnce(s, context);
                    break;
                case "willDisappear":
                    Contexts.Remove(context);
                    TouchDials.Remove(context);
                    break;
            }
        }
        catch (Exception e) { Log(e.ToString()); }
    }

    static object Get(Dictionary<string, object> d, string k)
    {
        object v;
        return d != null && d.TryGetValue(k, out v) ? v : null;
    }

    static Settings ParseSettings(Dictionary<string, object> d)
    {
        var s = new Settings();
        if (d == null) return s;
        try
        {
            object step = Get(d, "step");
            if (step != null && Convert.ToString(step, CultureInfo.InvariantCulture) != "")
                s.Step = Math.Max(1, Math.Min(50, (int)Math.Round(Convert.ToDouble(step, CultureInfo.InvariantCulture))));
        }
        catch { }
        object o = Get(d, "osd");
        if (o is bool) s.Osd = (bool)o;
        else if (o is string) s.Osd = (string)o != "false";
        return s;
    }

    // Some Stream Dock firmware sends both dialDown and dialPress for one press
    static void ToggleMuteOnce(Settings s, string context)
    {
        int now = Environment.TickCount;
        if (unchecked(now - lastMuteTick) < 300) return;
        lastMuteTick = now;
        ToggleMute(s, null, context);
    }

    // ---------- volume ----------

    static void Rotate(Settings s, int ticks, string forceName, string context)
    {
        var fg = Target.Resolve(forceName);
        var vols = Audio.FindSessions(fg.Matches);
        try
        {
            if (vols.Count == 0) { Notify(s, fg, -1, false, L.NoSession, context); return; }
            float cur; bool muted;
            vols[0].GetMasterVolume(out cur);
            vols[0].GetMute(out muted);
            double pct = Math.Max(0, Math.Min(100, Math.Round(cur * 100.0 + ticks * s.Step)));
            Guid g = Guid.Empty;
            bool unmute = muted && ticks > 0; // turning up unmutes, like the Windows volume flyout
            foreach (var v in vols)
            {
                v.SetMasterVolume((float)(pct / 100.0), ref g);
                if (unmute) v.SetMute(false, ref g);
            }
            Notify(s, fg, pct, muted && !unmute, null, context);
        }
        finally { Audio.Release(vols); }
    }

    static void ToggleMute(Settings s, string forceName, string context)
    {
        var fg = Target.Resolve(forceName);
        var vols = Audio.FindSessions(fg.Matches);
        try
        {
            if (vols.Count == 0) { Notify(s, fg, -1, false, L.NoSession, context); return; }
            float cur; bool muted;
            vols[0].GetMasterVolume(out cur);
            vols[0].GetMute(out muted);
            Guid g = Guid.Empty;
            foreach (var v in vols) v.SetMute(!muted, ref g);
            Notify(s, fg, Math.Round(cur * 100.0), !muted, null, context);
        }
        finally { Audio.Release(vols); }
    }

    static void Notify(Settings s, Target fg, double pct, bool muted, string message, string context)
    {
        string value = message ?? (muted ? L.Muted : pct + "%");
        if (testOut != null)
        {
            testOut.WriteLine("{0} (pid {1}): {2}", fg.DisplayName, fg.Pid, (message == null ? pct + "% " : "") + (message ?? (muted ? L.Muted : "")));
            return;
        }
        if (context != null && TouchDials.Contains(context)) Feedback(context, fg.DisplayName, value, message == null ? pct : -1, muted);
        if (s.Osd) osd.Popup(fg.DisplayName, pct, muted, message, fg.Hwnd);
    }

    // Stream Deck+ touch strip, built-in layout $B1 (title, icon, value, indicator bar)
    static void Feedback(string context, string title, string value, double pct, bool muted)
    {
        var p = new Dictionary<string, object>
        {
            { "title", title },
            { "value", value },
            { "indicator", new Dictionary<string, object> { { "value", pct < 0 ? 0 : pct }, { "opacity", muted || pct < 0 ? 0.3 : 1.0 } } }
        };
        Send(new Dictionary<string, object> { { "event", "setFeedback" }, { "context", context }, { "payload", p } });
    }

    // ---------- test mode (development only) ----------
    // FocusVolume.exe --test list
    // FocusVolume.exe --test <ticks|mute> <process.exe> [osd]
    // Output goes to %TEMP%\FocusVolume-test.txt
    static TextWriter testOut;

    static int TestMode(string[] a)
    {
        string logPath = Path.Combine(Path.GetTempPath(), "FocusVolume-test.txt");
        testOut = new StreamWriter(logPath, false, Encoding.UTF8);
        try
        {
            if (a.Length >= 2 && a[1] == "list")
            {
                var procs = Target.Snapshot();
                var vols = Audio.FindSessions(delegate (int pid)
                {
                    Target.Proc p;
                    testOut.WriteLine("session pid {0} {1}", pid, procs.TryGetValue(pid, out p) ? p.Name : "?");
                    return false;
                });
                Audio.Release(vols);
            }
            else if (a.Length >= 3)
            {
                var s = new Settings();
                var w = testOut;
                bool showOsd = a.Length >= 4 && a[3] == "osd";
                if (showOsd) { osd = new Osd(); IntPtr h = osd.Handle; testOut = null; }
                if (a[1] == "mute") ToggleMute(s, a[2], null); else Rotate(s, int.Parse(a[1]), a[2], null);
                testOut = w;
                if (showOsd)
                {
                    var t = new System.Windows.Forms.Timer { Interval = 1500 };
                    t.Tick += delegate { Application.Exit(); };
                    t.Start();
                    Application.Run();
                }
            }
        }
        finally { testOut.Close(); }
        return 0;
    }

    static void Log(string text)
    {
        try
        {
            string p = Path.Combine(Path.GetTempPath(), "FocusVolume.log");
            if (File.Exists(p) && new FileInfo(p).Length > 256 * 1024) File.Delete(p);
            File.AppendAllText(p, DateTime.Now.ToString("s") + " " + text + Environment.NewLine);
        }
        catch { }
    }
}

// ---------- foreground app ----------

class Target
{
    public struct Proc { public int Parent; public string Name; }

    public int Pid;
    public string Name;        // lower-case exe name
    public string DisplayName;
    public IntPtr Hwnd;
    Dictionary<int, Proc> procs;

    static readonly Dictionary<string, string> NameCache = new Dictionary<string, string>();

    public static Target Resolve(string forceName)
    {
        var t = new Target();
        t.procs = Snapshot();
        if (forceName != null)
        {
            t.Name = forceName.ToLowerInvariant();
            foreach (var kv in t.procs) if (kv.Value.Name == t.Name) { t.Pid = kv.Key; break; }
        }
        else
        {
            t.Hwnd = Native.GetForegroundWindow();
            uint pid;
            Native.GetWindowThreadProcessId(t.Hwnd, out pid);
            t.Pid = (int)pid;
            t.Name = t.NameOf(t.Pid);
            // UWP apps: the frame window belongs to ApplicationFrameHost, the real app owns a child window
            if (t.Name == "applicationframehost.exe")
            {
                int host = t.Pid, child = 0;
                Native.EnumChildWindows(t.Hwnd, delegate (IntPtr h, IntPtr l)
                {
                    uint p; Native.GetWindowThreadProcessId(h, out p);
                    if (p != host) { child = (int)p; return false; }
                    return true;
                }, IntPtr.Zero);
                if (child != 0) { t.Pid = child; t.Name = t.NameOf(child); }
            }
        }
        t.DisplayName = FriendlyName(t.Pid, t.Name);
        return t;
    }

    string NameOf(int pid)
    {
        Proc p;
        return procs.TryGetValue(pid, out p) ? p.Name : "";
    }

    // A session belongs to the foreground app if it is the same process, a process with the
    // same exe name (multi-process apps like Chrome, Edge, Discord), or a descendant of it.
    public bool Matches(int sessionPid)
    {
        if (Pid == 0) return false;
        if (sessionPid == Pid) return true;
        Proc p;
        if (!procs.TryGetValue(sessionPid, out p)) return false;
        if (Name != "" && p.Name == Name) return true;
        for (int i = 0, cur = p.Parent; i < 8 && cur != 0; i++)
        {
            if (cur == Pid) return true;
            Proc pp;
            if (!procs.TryGetValue(cur, out pp)) break;
            cur = pp.Parent;
        }
        return false;
    }

    public static Dictionary<int, Proc> Snapshot()
    {
        var d = new Dictionary<int, Proc>();
        IntPtr snap = Native.CreateToolhelp32Snapshot(2, 0);
        if (snap == (IntPtr)(-1)) return d;
        try
        {
            var e = new Native.PROCESSENTRY32W();
            e.dwSize = (uint)Marshal.SizeOf(typeof(Native.PROCESSENTRY32W));
            for (bool ok = Native.Process32FirstW(snap, ref e); ok; ok = Native.Process32NextW(snap, ref e))
                d[(int)e.th32ProcessID] = new Proc { Parent = (int)e.th32ParentProcessID, Name = (e.szExeFile ?? "").ToLowerInvariant() };
        }
        finally { Native.CloseHandle(snap); }
        return d;
    }

    // Use the exe's FileDescription ("Google Chrome"), falling back to the exe name
    static string FriendlyName(int pid, string exe)
    {
        if (exe == "") return L.Unknown;
        string cached;
        if (NameCache.TryGetValue(exe, out cached)) return cached;
        string name = exe.EndsWith(".exe") ? exe.Substring(0, exe.Length - 4) : exe;
        IntPtr h = Native.OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (h != IntPtr.Zero)
        {
            try
            {
                var sb = new StringBuilder(1024);
                int len = sb.Capacity;
                if (Native.QueryFullProcessImageNameW(h, 0, sb, ref len))
                {
                    string desc = FileVersionInfo.GetVersionInfo(sb.ToString()).FileDescription;
                    if (!string.IsNullOrEmpty(desc) && desc.Trim().Length > 0) name = desc.Trim();
                }
            }
            catch { }
            finally { Native.CloseHandle(h); }
        }
        NameCache[exe] = name;
        return name;
    }
}

// ---------- Windows Core Audio ----------

static class Audio
{
    // Walk every active render device and return the volume interfaces of matching sessions
    public static List<ISimpleAudioVolume> FindSessions(Func<int, bool> match)
    {
        var res = new List<ISimpleAudioVolume>();
        var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
        IMMDeviceCollection col = null;
        try
        {
            en.EnumAudioEndpoints(0 /*eRender*/, 1 /*DEVICE_STATE_ACTIVE*/, out col);
            int n; col.GetCount(out n);
            for (int i = 0; i < n; i++)
            {
                IMMDevice dev = null; object mo = null; IAudioSessionEnumerator se = null;
                try
                {
                    col.Item(i, out dev);
                    Guid iid = typeof(IAudioSessionManager2).GUID;
                    dev.Activate(ref iid, 23 /*CLSCTX_ALL*/, IntPtr.Zero, out mo);
                    ((IAudioSessionManager2)mo).GetSessionEnumerator(out se);
                    int c; se.GetCount(out c);
                    for (int j = 0; j < c; j++)
                    {
                        object so; se.GetSession(j, out so);
                        var ctl = (IAudioSessionControl2)so;
                        int state; uint pid;
                        ctl.GetState(out state);
                        ctl.GetProcessId(out pid);
                        if (state != 2 /*Expired*/ && pid != 0 && match((int)pid)) res.Add((ISimpleAudioVolume)so);
                        else Marshal.ReleaseComObject(so);
                    }
                }
                catch { }
                finally { Rel(se); Rel(mo); Rel(dev); }
            }
        }
        finally { Rel(col); Rel(en); }
        return res;
    }

    public static void Release(List<ISimpleAudioVolume> l) { foreach (var v in l) Rel(v); }
    static void Rel(object o) { if (o != null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o); }
}

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
class MMDeviceEnumeratorCom { }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator
{
    void EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
}

[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceCollection
{
    void GetCount(out int count);
    void Item(int index, out IMMDevice device);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice
{
    void Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
}

[ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionManager2
{
    [PreserveSig] int GetAudioSessionControl();  // vtable placeholder
    [PreserveSig] int GetSimpleAudioVolume();    // vtable placeholder
    void GetSessionEnumerator(out IAudioSessionEnumerator e);
}

[ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionEnumerator
{
    void GetCount(out int count);
    void GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
}

[ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionControl2
{
    void GetState(out int state);
    [PreserveSig] int GetDisplayName();          // placeholders below keep the vtable aligned
    [PreserveSig] int SetDisplayName();
    [PreserveSig] int GetIconPath();
    [PreserveSig] int SetIconPath();
    [PreserveSig] int GetGroupingParam();
    [PreserveSig] int SetGroupingParam();
    [PreserveSig] int RegisterAudioSessionNotification();
    [PreserveSig] int UnregisterAudioSessionNotification();
    [PreserveSig] int GetSessionIdentifier();
    [PreserveSig] int GetSessionInstanceIdentifier();
    [PreserveSig] int GetProcessId(out uint pid);
}

[ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface ISimpleAudioVolume
{
    void SetMasterVolume(float level, ref Guid eventContext);
    void GetMasterVolume(out float level);
    void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
    void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}

// ---------- on-screen display: never takes focus, click-through ----------

class Osd : Form
{
    readonly System.Windows.Forms.Timer hideTimer = new System.Windows.Forms.Timer { Interval = 1300 };
    readonly float scale;
    readonly Font titleFont, valueFont;
    string title = "", message;
    double pct;
    bool muted;

    public Osd()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(30, 30, 30);
        Opacity = 0.93; // also makes it a layered window, which click-through needs
        DoubleBuffered = true;
        using (var g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
        titleFont = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
        valueFont = new Font("Microsoft YaHei UI", 10.5f);
        hideTimer.Tick += delegate { hideTimer.Stop(); Native.ShowWindow(Handle, 0); };
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_TRANSPARENT
            cp.ExStyle |= 0x08000000 | 0x80 | 0x8 | 0x20;
            return cp;
        }
    }

    public void Popup(string appName, double percent, bool isMuted, string msg, IntPtr fgHwnd)
    {
        title = appName; pct = percent; muted = isMuted; message = msg;
        int w = (int)(300 * scale), h = (int)(78 * scale);
        Rectangle area = (fgHwnd != IntPtr.Zero ? Screen.FromHandle(fgHwnd) : Screen.PrimaryScreen).WorkingArea;
        int x = area.X + (area.Width - w) / 2, y = area.Bottom - h - (int)(70 * scale);
        if (Width != w || Height != h)
        {
            Size = new Size(w, h);
            using (var path = Rounded(new Rectangle(0, 0, w, h), (int)(12 * scale))) Region = new Region(path);
        }
        // SWP_NOACTIVATE | SWP_SHOWWINDOW: topmost without activation, so fullscreen games keep focus
        Native.SetWindowPos(Handle, (IntPtr)(-1), x, y, w, h, 0x10 | 0x40);
        Invalidate();
        Update();
        hideTimer.Stop();
        hideTimer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        float pad = 18 * scale;
        var white = Color.FromArgb(240, 240, 240);
        var dim = Color.FromArgb(150, 150, 150);

        var titleRect = new RectangleF(pad, 12 * scale, Width - pad * 2 - 90 * scale, 26 * scale);
        TextRenderer.DrawText(g, title, titleFont, Rectangle.Round(titleRect), white,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        string right = message != null ? "" : (muted ? L.Muted : pct + "%");
        TextRenderer.DrawText(g, right, valueFont, Rectangle.Round(new RectangleF(Width - pad - 90 * scale, 12 * scale, 90 * scale, 26 * scale)),
            muted ? Color.FromArgb(255, 120, 110) : white, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

        if (message != null)
        {
            TextRenderer.DrawText(g, message, valueFont, Rectangle.Round(new RectangleF(pad, 42 * scale, Width - pad * 2, 24 * scale)),
                dim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            return;
        }

        var bar = new RectangleF(pad, 50 * scale, Width - pad * 2, 7 * scale);
        using (var p = Rounded(Rectangle.Round(bar), (int)(4 * scale)))
        using (var b = new SolidBrush(Color.FromArgb(70, 70, 70))) g.FillPath(b, p);
        float fw = (float)(bar.Width * pct / 100.0);
        if (fw >= 1)
        {
            var fill = new RectangleF(bar.X, bar.Y, Math.Max(fw, bar.Height), bar.Height);
            using (var p = Rounded(Rectangle.Round(fill), (int)(4 * scale)))
            using (var b = new SolidBrush(muted ? dim : Color.FromArgb(76, 160, 255))) g.FillPath(b, p);
        }
    }

    static GraphicsPath Rounded(Rectangle r, int radius)
    {
        int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

static class Native
{
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] public static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool Process32FirstW(IntPtr snap, ref PROCESSENTRY32W e);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool Process32NextW(IntPtr snap, ref PROCESSENTRY32W e);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageNameW(IntPtr h, int flags, StringBuilder name, ref int size);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PROCESSENTRY32W
    {
        public uint dwSize, cntUsage, th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID, cntThreads, th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }
}
