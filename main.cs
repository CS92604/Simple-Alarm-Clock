using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

// Build: csc /target:winexe /out:AlarmClock.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll AlarmClock.cs

class Dev { public string Id, Name; public override string ToString() { return Name; } }

[ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPolicyConfig {
    [PreserveSig] int a(); [PreserveSig] int b(); [PreserveSig] int c(); [PreserveSig] int d(); [PreserveSig] int e();
    [PreserveSig] int f(); [PreserveSig] int g(); [PreserveSig] int h(); [PreserveSig] int i(); [PreserveSig] int j();
    [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
}
[ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")] class PolicyConfigClient { }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator {
    [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out IntPtr devs);
    [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice dev);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice dev);
}
[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice {
    [PreserveSig] int Activate(ref Guid iid, int ctx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o);
    [PreserveSig] int OpenPropertyStore(int access, out IntPtr ps);
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
}

[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioEndpointVolume {
    [PreserveSig] int RegisterControlChangeNotify(IntPtr p);
    [PreserveSig] int UnregisterControlChangeNotify(IntPtr p);
    [PreserveSig] int GetChannelCount(out uint n);
    [PreserveSig] int SetMasterVolumeLevel(float db, ref Guid ctx);
    [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
    [PreserveSig] int GetMasterVolumeLevel(out float db);
    [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    [PreserveSig] int SetChannelVolumeLevel(uint ch, float db, ref Guid ctx);
    [PreserveSig] int SetChannelVolumeLevelScalar(uint ch, float level, ref Guid ctx);
    [PreserveSig] int GetChannelVolumeLevel(uint ch, out float db);
    [PreserveSig] int GetChannelVolumeLevelScalar(uint ch, out float level);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
}

static class Win {
    [DllImport("kernel32.dll")] static extern IntPtr CreateJobObject(IntPtr a, string n);
    [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr j, int c, IntPtr i, int l);
    [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr j, IntPtr p);
    static IntPtr job;

    // child dies when this process dies for any reason (close, crash, kill)
    public static void BindToMe(Process child) {
        if (job == IntPtr.Zero) {
            job = CreateJobObject(IntPtr.Zero, null);
            int size = IntPtr.Size == 8 ? 144 : 112;
            IntPtr buf = Marshal.AllocHGlobal(size);
            for (int i = 0; i < size; i++) Marshal.WriteByte(buf, i, 0);
            Marshal.WriteInt32(buf, 16, 0x2000);   // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            SetInformationJobObject(job, 9, buf, size);
        }
        AssignProcessToJobObject(job, child.Handle);
    }

    [DllImport("user32.dll")] public static extern void mouse_event(int f, int dx, int dy, int d, int e);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int m, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint f);
}

static class Audio {
    const string K = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render";
    public static readonly Regex Blocked = new Regex("G407|Z407", RegexOptions.IgnoreCase);

    public static string Prefix(string guid) { return "{0.0.0.00000000}." + guid; }

    public static List<Dev> Active() {
        var list = new List<Dev>();
        using (var root = Registry.LocalMachine.OpenSubKey(K)) {
            foreach (var g in root.GetSubKeyNames()) {
                using (var d = root.OpenSubKey(g)) {
                    if (!(d.GetValue("DeviceState") is int) || (int)d.GetValue("DeviceState") != 1) continue;
                    using (var p = d.OpenSubKey("Properties")) {
                        var n = p.GetValue("{a45c254e-df1c-4efd-8020-67d146a850e0},2") as string;
                        var c = p.GetValue("{b3f8fa53-0004-438e-9003-51a46e139bfc},6") as string;
                        list.Add(new Dev { Id = g, Name = n + " (" + c + ")" });
                    }
                }
            }
        }
        return list;
    }

    public static bool IsActive(string guid) {
        foreach (var d in Active()) if (d.Id == guid) return true;
        return false;
    }

    public static string GetDefault() {
        var e = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        IMMDevice dev; string id;
        Marshal.ThrowExceptionForHR(e.GetDefaultAudioEndpoint(0, 1, out dev));
        Marshal.ThrowExceptionForHR(dev.GetId(out id));
        return id;
    }

    public static void SetDefault(string fullId) {
        var p = (IPolicyConfig)new PolicyConfigClient();
        for (int r = 0; r < 3; r++) Marshal.ThrowExceptionForHR(p.SetDefaultEndpoint(fullId, r));
    }

    public static void SetVolume(string fullId, int pct) {
        var e = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        IMMDevice dev; object o;
        var iid = typeof(IAudioEndpointVolume).GUID; var ctx = Guid.Empty;
        Marshal.ThrowExceptionForHR(e.GetDevice(fullId, out dev));
        Marshal.ThrowExceptionForHR(dev.Activate(ref iid, 23, IntPtr.Zero, out o));
        var v = (IAudioEndpointVolume)o;
        Marshal.ThrowExceptionForHR(v.SetMute(false, ref ctx));
        Marshal.ThrowExceptionForHR(v.SetMasterVolumeLevelScalar(pct / 100f, ref ctx));
    }
}

class Cfg {
    static string Path_ { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "alarm.ini"); } }
    public string Time = "07:00", Url = "https://youtu.be/eGoqaO21KgY", Dev = "";
    public string Url2 = "https://youtu.be/rr3-dnq7yyM";   // escalation clip
    public string Days ="Monday,Tuesday,Wednesday,Thursday,Friday,Saturday,Sunday";
    public int Vol = 60;

    public static Cfg Load() {
        var c = new Cfg();
        if (!File.Exists(Path_)) return c;
        foreach (var line in File.ReadAllLines(Path_)) {
            int i = line.IndexOf('='); if (i < 0) continue;
            string k = line.Substring(0, i), v = line.Substring(i + 1);
            if (k == "time") c.Time = v; else if (k == "url") c.Url = v; else if (k == "dev") c.Dev = v; else if (k == "days") c.Days = v; else if (k == "url2") c.Url2 = v;
            else if (k == "vol") int.TryParse(v, out c.Vol);
        }
        return c;
    }
    public void Save() {
        File.WriteAllLines(Path_, new[] { "time=" + Time, "url=" + Url, "dev=" + Dev, "vol=" + Vol, "days=" + Days, "url2=" + Url2 });
    }
}

static class Log {
    public static void W(string s) {
        try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "alarm.log"), DateTime.Now.ToString("HH:mm:ss ") + s + "\r\n"); } catch { }
    }
}

// downloaded audio is cached next to the exe so the alarm never needs the network
static class Clips {
    static string Key(string url) {
        using (var md5 = System.Security.Cryptography.MD5.Create()) {
            var h = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(url));
            return BitConverter.ToString(h, 0, 5).Replace("-", "").ToLower();
        }
    }
    public static string PathFor(string url, bool loop) {
        var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, (loop ? "loop_" : "clip_") + Key(url) + ".wav");
    }
    static string Exec(string exe, string args) {
        var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true });
        string err = p.StandardError.ReadToEnd(); p.WaitForExit(); return err;
    }
    public static string Ensure(string url, bool loop) {
        string dest = PathFor(url, loop);
        if (File.Exists(dest)) return dest;
        string yt = Tools.Find("yt-dlp"), pre = "", ff = Tools.Find("ffmpeg");
        if (yt == null) { yt = Tools.Find("python"); pre = "-m yt_dlp "; }
        if (yt == null || ff == null) { Log.W("yt-dlp or ffmpeg not found"); return null; }
        string tmp = Path.Combine(Path.GetTempPath(), "alarm_dl_" + Key(url)), src = tmp + ".wav";
        if (File.Exists(src)) File.Delete(src);
        // plain first, then with browser cookies (youtube sometimes asks for a sign in)
        foreach (string ck in new[] { "", " --cookies-from-browser chrome", " --cookies-from-browser edge", " --cookies-from-browser firefox", " --cookies-from-browser opera" }) {
            string err = Exec(yt, pre + "-x --audio-format wav" + ck + " -o \"" + tmp + ".%(ext)s\" \"" + url + "\"");
            if (File.Exists(src)) break;
            Log.W("yt-dlp failed" + ck + ": " + err.Trim().Split('\n')[err.Trim().Split('\n').Length - 1]);
        }
        if (!File.Exists(src)) return null;
        if (loop) {   // forward then backward
            Exec(ff, "-y -i \"" + src + "\" -filter_complex \"[0:a]asplit[a][b];[b]areverse[r];[a][r]concat=n=2:v=0:a=1\" \"" + dest + "\"");
            File.Delete(src);
        } else File.Move(src, dest);
        return File.Exists(dest) ? dest : null;
    }
}

static class Tools {
    public static string Find(string exe) {
        var links = Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "", @"Microsoft\WinGet\Links");
        foreach (var d in ((Environment.GetEnvironmentVariable("PATH") ?? "") + ";" + links).Split(';')) {
            try { var p = Path.Combine(d.Trim(), exe + ".exe"); if (File.Exists(p)) return p; } catch { }
        }
        return null;
    }
}

// ---------- look & feel: soft pastel cards, purple/blue gradient ----------
static class Ui {
    public static readonly Color Bg = Color.FromArgb(243, 241, 255), Ink = Color.FromArgb(52, 46, 110),
        Muted = Color.FromArgb(150, 146, 200), A1 = Color.FromArgb(139, 108, 240), A2 = Color.FromArgb(84, 140, 255),
        Track = Color.FromArgb(228, 225, 247);

    // soft rounded look: Arial Rounded (bold-only) if installed, else Segoe UI Variable / Segoe UI
    static string Pick(params string[] names) {
        var have = new System.Drawing.Text.InstalledFontCollection().Families;
        foreach (var n in names) foreach (var f in have) if (f.Name == n) return n;
        return "Segoe UI";
    }
    static readonly string Round_ = Pick("Nunito", "Varela Round", "Arial Rounded MT Bold", "Segoe UI Variable Display");
    static readonly string Body_ = Pick("Segoe UI Variable Text", "Segoe UI");
    public static Font F(float size) { return new Font(Round_, size, Round_.StartsWith("Segoe") ? FontStyle.Bold : FontStyle.Regular); }
    public static Font Body(float size) { return new Font(Body_, size); }

    public static GraphicsPath Round(Rectangle r, int rad) {
        var p = new GraphicsPath(); int d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }
    public static void Shadow(Graphics g, Rectangle r, int rad) {
        for (int i = 8; i >= 1; i--) {
            using (var p = Round(new Rectangle(r.X - i, r.Y - i + 5, r.Width + 2 * i, r.Height + 2 * i), rad + i))
            using (var b = new SolidBrush(Color.FromArgb(6, 110, 90, 220))) g.FillPath(b, p);
        }
    }
    public static LinearGradientBrush Grad(Rectangle r) {
        return new LinearGradientBrush(new Rectangle(r.X, r.Y, Math.Max(1, r.Width), Math.Max(1, r.Height)), A1, A2, 20f);
    }
    public static void Card(Graphics g, Rectangle r, int rad, bool fill) {
        Shadow(g, r, rad);
        using (var p = Round(r, rad)) {
            if (fill) g.FillPath(Brushes.White, p);
        }
    }
    public static Rectangle Inner(Control c) { return new Rectangle(10, 6, c.Width - 20, c.Height - 18); }
    public static void Smooth(Graphics g) {
        g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
    }

    static readonly double[][] Dots = {
        new double[]{.06,.03,7,0}, new double[]{.93,.02,9,1}, new double[]{.97,.30,6,2}, new double[]{.02,.42,8,3},
        new double[]{.96,.55,7,0}, new double[]{.03,.70,6,1}, new double[]{.94,.82,9,3}, new double[]{.08,.97,8,2}, new double[]{.55,.985,6,0} };
    public static void Confetti(Graphics g, Size s) {
        Smooth(g);
        Color[] cs = { A1, A2, Color.FromArgb(196, 140, 255), Color.FromArgb(110, 200, 255) };
        foreach (var d in Dots)
            using (var b = new SolidBrush(Color.FromArgb(150, cs[(int)d[3]])))
                g.FillEllipse(b, (float)(d[0] * s.Width), (float)(d[1] * s.Height), (float)d[2], (float)d[2]);
    }

    // alarm-clock glyph on a rounded gradient tile
    public static Bitmap IconBmp(int s) {
        var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
            var all = new Rectangle(0, 0, s, s);
            using (var p = Round(all, (int)(s * 0.24)))
            using (var b = new LinearGradientBrush(all, A1, A2, 45f)) g.FillPath(b, p);
            float cx = s * .5f, cy = s * .56f, r = s * .27f, br = s * .11f;
            using (var bell = new SolidBrush(Color.FromArgb(235, 228, 255))) {
                g.FillEllipse(bell, cx - r * 1.05f - br * .3f, s * .12f, br * 2, br * 2);
                g.FillEllipse(bell, cx + r * 1.05f - br * 1.7f, s * .12f, br * 2, br * 2);
            }
            g.FillEllipse(Brushes.White, cx - r, cy - r, r * 2, r * 2);
            using (var pen = new Pen(Ink, Math.Max(1.2f, s * .05f)) { StartCap = LineCap.Round, EndCap = LineCap.Round }) {
                g.DrawLine(pen, cx, cy, cx, cy - r * .62f);
                g.DrawLine(pen, cx, cy, cx + r * .48f, cy + r * .28f);
            }
            using (var pen = new Pen(Color.White, Math.Max(1.2f, s * .05f)) { StartCap = LineCap.Round, EndCap = LineCap.Round }) {
                g.DrawLine(pen, cx - r * .7f, cy + r * 1.05f, cx - r * .95f, cy + r * 1.3f);
                g.DrawLine(pen, cx + r * .7f, cy + r * 1.05f, cx + r * .95f, cy + r * 1.3f);
            }
        }
        return bmp;
    }
    public static Icon AppIcon() { using (var b = IconBmp(32)) return Icon.FromHandle(b.GetHicon()); }

    // build-time helper: AlarmClock.exe --icon app.ico
    public static void WriteIco(string path) {
        int[] sizes = { 256, 48, 32, 16 }; var png = new List<byte[]>();
        foreach (int s in sizes) using (var b = IconBmp(s)) using (var ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); png.Add(ms.ToArray()); }
        using (var w = new BinaryWriter(File.Create(path))) {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int off = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++) {
                w.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                w.Write(png[i].Length); w.Write(off); off += png[i].Length;
            }
            foreach (var d in png) w.Write(d);
        }
    }
}

class StyledForm : Form {
    public StyledForm() {
        DoubleBuffered = true; AutoScaleMode = AutoScaleMode.None; Font = Ui.Body(10);
        BackColor = Ui.Bg; Icon = Ui.AppIcon(); StartPosition = FormStartPosition.CenterScreen;
    }
    protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Ui.Bg); }
}

class Buf : Control {
    public Buf() {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
        BackColor = Color.Transparent;
    }
}

class PillButton : Buf {
    public bool Primary; bool hover;
    public PillButton() { Cursor = Cursors.Hand; Font = Ui.F(10); }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics; Ui.Smooth(g);
        var r = Ui.Inner(this); Ui.Shadow(g, r, r.Height / 2);
        using (var p = Ui.Round(r, r.Height / 2)) {
            if (Primary) using (var b = Ui.Grad(r)) g.FillPath(b, p); else g.FillPath(Brushes.White, p);
            if (hover) using (var b = new SolidBrush(Color.FromArgb(Primary ? 35 : 40, Primary ? Color.White : Ui.A1))) g.FillPath(b, p);
        }
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        using (var b = new SolidBrush(Primary ? Color.White : Ui.A1))
            g.DrawString(Text.ToUpper(), Font, b, r, sf);
    }
}

class Chip : Buf {
    public bool Checked;
    public Chip() { Cursor = Cursors.Hand; Font = Ui.F(9.5f); }
    protected override void OnClick(EventArgs e) { Checked = !Checked; Invalidate(); base.OnClick(e); }
    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics; Ui.Smooth(g);
        var r = new Rectangle(3, 3, Width - 6, Height - 9);
        if (!Checked) Ui.Shadow(g, r, r.Height / 2);
        using (var p = Ui.Round(r, r.Height / 2)) {
            if (Checked) using (var b = Ui.Grad(r)) g.FillPath(b, p); else g.FillPath(Brushes.White, p);
        }
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        using (var b = new SolidBrush(Checked ? Color.White : Ui.Muted)) g.DrawString(Text.ToUpper(), Font, b, r, sf);
    }
}

class Picker : Buf {
    public List<Dev> Items = new List<Dev>(); public int Index = -1;
    public Dev Selected { get { return Index >= 0 && Index < Items.Count ? Items[Index] : null; } }
    public Picker() { Cursor = Cursors.Hand; Font = Ui.F(10); }
    void Step(int d) { if (Items.Count == 0) return; Index = (Index + d + Items.Count) % Items.Count; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { Step(e.X < Width / 2 ? -1 : 1); base.OnMouseDown(e); }
    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics; Ui.Smooth(g); var r = Ui.Inner(this); Ui.Card(g, r, r.Height / 2, true);
        using (var f = new Font("Segoe UI", 18, FontStyle.Bold)) using (var b = new SolidBrush(Ui.A1))
        using (var c = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) {
            g.DrawString("‹", f, b, new Rectangle(r.X + 8, r.Y - 2, 32, r.Height), c);
            g.DrawString("›", f, b, new Rectangle(r.Right - 40, r.Y - 2, 32, r.Height), c);
        }
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
        using (var b = new SolidBrush(Ui.Ink))
            g.DrawString(Selected == null ? "No output devices" : Selected.Name, Font, b, new Rectangle(r.X + 40, r.Y, r.Width - 80, r.Height), sf);
    }
}

class Slider : Buf {
    public int Value = 60; bool drag;
    public Slider() { Cursor = Cursors.Hand; }
    Rectangle Track() { var r = Ui.Inner(this); return new Rectangle(r.X + 22, r.Y + r.Height / 2 - 5, r.Width - 44 - 52, 10); }
    void SetFrom(int x) { var t = Track(); Value = Math.Max(1, Math.Min(100, 1 + (int)Math.Round((x - t.X) * 99.0 / t.Width))); Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { drag = true; Capture = true; SetFrom(e.X); base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (drag) SetFrom(e.X); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { drag = false; Capture = false; base.OnMouseUp(e); }
    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics; Ui.Smooth(g); var r = Ui.Inner(this); Ui.Card(g, r, r.Height / 2, true);
        var t = Track(); int kx = t.X + (int)((Value - 1) / 99.0 * t.Width);
        using (var p = Ui.Round(t, 5)) using (var b = new SolidBrush(Ui.Track)) g.FillPath(b, p);
        if (kx > t.X + 2) { var f = new Rectangle(t.X, t.Y, kx - t.X, t.Height); using (var p = Ui.Round(f, 5)) using (var b = Ui.Grad(f)) g.FillPath(b, p); }
        var kr = new Rectangle(kx - 13, t.Y + 5 - 13, 26, 26); Ui.Shadow(g, kr, 13);
        g.FillEllipse(Brushes.White, kr);
        var dr = new Rectangle(kx - 6, t.Y + 5 - 6, 12, 12); using (var b = Ui.Grad(dr)) g.FillEllipse(b, dr);
        using (var f = Ui.F(13)) using (var b = new SolidBrush(Ui.A1))
        using (var sf = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center })
            g.DrawString(Value + "%", f, b, new Rectangle(r.Right - 66, r.Y, 52, r.Height), sf);
    }
}

class Field : Panel {
    TextBox tb = new TextBox();
    public string Value { get { return tb.Text; } set { tb.Text = value; } }
    public Field() {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        tb.BorderStyle = BorderStyle.None; tb.BackColor = Color.White; tb.ForeColor = Ui.Ink; tb.Font = Ui.Body(10);
        Controls.Add(tb);
    }
    protected override void OnLayout(LayoutEventArgs e) {
        tb.Width = Width - 20 - 44; tb.Left = 32; tb.Top = 6 + (Height - 18 - tb.Height) / 2; base.OnLayout(e);
    }
    protected override void OnPaint(PaintEventArgs e) { Ui.Smooth(e.Graphics); var r = Ui.Inner(this); Ui.Card(e.Graphics, r, r.Height / 2, true); }
}

class TimePick : Buf {
    int h12 = 7, min = 0; bool pm;
    public string Time24 { get { return (((h12 % 12) + (pm ? 12 : 0))).ToString("00") + ":" + min.ToString("00"); } }
    public string Nice { get { return h12 + ":" + min.ToString("00") + (pm ? " PM" : " AM"); } }
    public void Set(DateTime t) { pm = t.Hour >= 12; h12 = t.Hour % 12; if (h12 == 0) h12 = 12; min = t.Minute; Invalidate(); }
    int Col(int x) { var r = Ui.Inner(this); double f = (x - r.X) / (double)r.Width; return f < .36 ? 0 : f < .72 ? 1 : 2; }
    void Bump(int col, int dir, int step) {
        if (col == 0) h12 = (h12 - 1 + dir + 12) % 12 + 1;
        else if (col == 1) min = (min + dir * step + 60) % 60;
        else pm = !pm;
        Invalidate();
    }
    protected override void OnMouseDown(MouseEventArgs e) {
        var r = Ui.Inner(this); int c = Col(e.X);
        if (c == 2) Bump(2, 1, 0);
        else if (e.Y < r.Y + r.Height * .35) Bump(c, 1, 5);
        else if (e.Y > r.Y + r.Height * .65) Bump(c, -1, 5);
        base.OnMouseDown(e);
    }
    protected override void OnMouseWheel(MouseEventArgs e) { Bump(Col(e.X), e.Delta > 0 ? 1 : -1, 1); base.OnMouseWheel(e); }
    void Arrow(Graphics g, float cx, float cy, bool up) {
        float d = up ? -1 : 1;
        using (var b = new SolidBrush(Ui.Muted))
            g.FillPolygon(b, new[] { new PointF(cx - 7, cy - 3 * d), new PointF(cx + 7, cy - 3 * d), new PointF(cx, cy + 4 * d) });
    }
    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics; Ui.Smooth(g); var r = Ui.Inner(this); Ui.Card(g, r, 30, true);
        float w = r.Width; float[] cx = { r.X + w * .18f, r.X + w * .54f, r.X + w * .86f };
        string[] txt = { h12.ToString("00"), min.ToString("00"), pm ? "PM" : "AM" };
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        using (var big = Ui.F(36)) using (var sm = Ui.F(22))
        using (var ink = new SolidBrush(Ui.Ink)) using (var acc = new SolidBrush(Ui.A1)) using (var mu = new SolidBrush(Ui.Muted)) {
            float my = r.Y + r.Height / 2f;
            g.DrawString(txt[0], big, ink, cx[0], my, sf); g.DrawString(txt[1], big, ink, cx[1], my, sf);
            g.DrawString(":", big, mu, r.X + w * .36f, my - 3, sf); g.DrawString(txt[2], sm, acc, cx[2], my, sf);
        }
        Arrow(g, cx[0], r.Y + 14, true); Arrow(g, cx[0], r.Bottom - 14, false);
        Arrow(g, cx[1], r.Y + 14, true); Arrow(g, cx[1], r.Bottom - 14, false);
    }
}

class FireForm : StyledForm {
    Label status = new Label();
    Cfg cfg; Process player, escP; volatile bool stopped; string prevDefault, fxPath;

    public FireForm(Cfg c) {
        cfg = c;
        Text = "Alarm"; ClientSize = new Size(380, 220); TopMost = true; FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
        var big = new Label { Text = "ALARM", Font = Ui.F(30), ForeColor = Ui.Ink, BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleCenter }; big.SetBounds(0, 22, 380, 62);
        status.SetBounds(0, 86, 380, 24); status.Text = "Starting..."; status.TextAlign = ContentAlignment.MiddleCenter;
        status.BackColor = Color.Transparent; status.ForeColor = Ui.Muted; status.Font = Ui.F(9.5f);
        var stop = new PillButton { Text = "Stop", Primary = true }; stop.SetBounds(20, 124, 340, 76);
        stop.Click += delegate { Finish(); };
        Controls.AddRange(new Control[] { big, status, stop });
        Shown += delegate { new Thread(Run) { IsBackground = true }.Start(); };
        ControlBox = false;   // no X: only Stop ends the alarm, so FxSound always gets restored
        Active = true; Current = this;
        FormClosing += delegate(object s, FormClosingEventArgs e) {
            if (!done && e.CloseReason != CloseReason.WindowsShutDown) { e.Cancel = true; return; }
            Log.W("closing, done=" + done + " reason=" + e.CloseReason); stopped = true; KillPlayer(); Restore(); Active = false;
        };
    }
    public static bool Active;
    public static FireForm Current;
    bool done;
    public void StopNow() { done = true; Close(); }

    void Say(string s) { try { BeginInvoke((Action)delegate { status.Text = s; }); } catch { } }
    void KillPlayer() {
        try { if (player != null && !player.HasExited) player.Kill(); } catch { }
        try { if (escP != null && !escP.HasExited) escP.Kill(); } catch { }
    }
    void Restore() {
        try { if (prevDefault != null) { Audio.SetDefault(prevDefault); prevDefault = null; } } catch { }
        try { if (fxPath != null) { Process.Start(fxPath); fxPath = null; } } catch { }
    }
    void Finish() { done = true; try { BeginInvoke((Action)Close); } catch { } }

    static void Run(string exe, string args) {
        var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true });
        p.StandardError.ReadToEnd(); p.WaitForExit();
    }

    void Run() {
        try {
            Log.W("fire start");
            Win.SetThreadExecutionState(0x80000003);                       // stay awake
            Win.PostMessage((IntPtr)0xFFFF, 0x112, (IntPtr)0xF170, (IntPtr)(-1)); // monitors on
            Win.mouse_event(1, 1, 0, 0, 0); Win.mouse_event(1, -1, 0, 0, 0);
            Say("Waking monitors...");

            for (int i = 0; i < 40 && !Audio.IsActive(cfg.Dev); i++) Thread.Sleep(500);
            if (!Audio.IsActive(cfg.Dev)) throw new Exception("Output device not available");
            foreach (var d in Audio.Active())
                if (d.Id == cfg.Dev && Audio.Blocked.IsMatch(d.Name)) throw new Exception("Blocked device: " + d.Name);

            string full = Audio.Prefix(cfg.Dev);
            prevDefault = Audio.GetDefault();
            // FxSound forces itself back as default output, so close it for the alarm (relaunched in Restore)
            foreach (var p in Process.GetProcessesByName("FxSound")) {
                try { fxPath = p.MainModule.FileName; p.Kill(); p.WaitForExit(3000); } catch { }
            }
            // watchdog outlives us if we get killed: restores default output + FxSound
            Process.Start(new ProcessStartInfo(Application.ExecutablePath,
                "--guard " + Process.GetCurrentProcess().Id + " \"" + prevDefault + "\" \"" + (fxPath ?? "-") + "\"") {
                UseShellExecute = false, CreateNoWindow = true });
            Thread.Sleep(1000);
            Log.W("guard started, setting default"); Audio.SetDefault(full); Log.W("default set");
            Audio.SetVolume(full, cfg.Vol); Log.W("volume set");

            string ffplay = Tools.Find("ffplay");
            if (ffplay == null) throw new Exception("ffplay not found");
            Say("Getting audio...");
            string loop = Clips.Ensure(cfg.Url, true);
            if (loop == null) throw new Exception("no alarm audio (see alarm.log)");
            string esc = cfg.Url2.StartsWith("http") ? Clips.Ensure(cfg.Url2, false) : null;   // optional
            bool hasEsc = esc != null;
            if (stopped) return;

            Log.W("playing"); Say("Playing forward/backward (" + cfg.Vol + "%)");
            // after 15 min: play the escalation clip (overlapping the main loop) after a random 0-5 min delay, then re-roll
            var sw = Stopwatch.StartNew(); var rnd = new Random();
            double next = 900 + rnd.Next(300);
            // only Stop ends the alarm: if ffplay dies (audio device reset etc) start it again
            while (!stopped) {
                player = Process.Start(new ProcessStartInfo(ffplay, "-nodisp -nostats -loop 0 -loglevel warning -volume 100 \"" + loop + "\"") {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true });
                player.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) Log.W("ffplay: " + e.Data); };
                player.BeginErrorReadLine();
                Win.BindToMe(player);
                Log.W("ffplay pid " + player.Id);
                while (!player.WaitForExit(1000)) {
                    if (hasEsc && sw.Elapsed.TotalSeconds >= next) {
                        escP = Process.Start(new ProcessStartInfo(ffplay, "-nodisp -nostats -autoexit -loglevel quiet -volume 100 \"" + esc + "\"") {
                            UseShellExecute = false, CreateNoWindow = true });
                        Win.BindToMe(escP);
                        next = sw.Elapsed.TotalSeconds + rnd.Next(300);   // timer resets on play: can fire again soon
                    }
                }
                if (stopped) break;
                Log.W("ffplay exited by itself, code " + player.ExitCode + ", restarting");
                Thread.Sleep(1500);
                try { Audio.SetDefault(full); } catch { }
            }
        } catch (Exception ex) { Log.W("ERROR " + ex); Say("Error: " + ex.Message); Restore(); }
    }
}

class MainForm : StyledForm {
    TimePick time = new TimePick();
    Field url = new Field(), url2 = new Field();
    Picker picker = new Picker();
    Slider vol = new Slider();
    Label status = new Label();
    Chip[] days = new Chip[7];

    Label Lbl(string text, int y) {
        var l = new Label { Text = text.ToUpper(), Left = 26, Top = y, AutoSize = true, BackColor = Color.Transparent,
            ForeColor = Ui.Muted, Font = Ui.F(8.25f) };
        Controls.Add(l); return l;
    }
    T Place<T>(T c, int y, int h) where T : Control { c.SetBounds(10, y, 420, h); Controls.Add(c); return c; }

    public MainForm() {
        Text = "Alarm Clock"; ClientSize = new Size(440, 680); FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
        var cfg = Cfg.Load();

        Lbl("Wake me at", 16);
        Place(time, 34, 116);
        DateTime t; if (DateTime.TryParse(cfg.Time, out t)) time.Set(t);
        new ToolTip().SetToolTip(time, "Click arrows: minutes step by 5. Scroll wheel: step by 1.");

        Lbl("Alarm sound (YouTube link)", 160); Place(url, 178, 58); url.Value = cfg.Url;

        Lbl("Output (never Z407)", 244);
        Place(picker, 262, 58);
        foreach (var d in Audio.Active()) {
            if (Audio.Blocked.IsMatch(d.Name)) continue;
            picker.Items.Add(d);
            if (d.Id == cfg.Dev || (cfg.Dev == "" && d.Name.StartsWith("SAMSUNG") && picker.Index < 0)) picker.Index = picker.Items.Count - 1;
        }
        if (picker.Index < 0 && picker.Items.Count > 0) picker.Index = 0;

        Lbl("Volume", 328); Place(vol, 346, 58); vol.Value = Math.Min(100, Math.Max(1, cfg.Vol));

        Lbl("After 15 min: play this randomly (blank = off)", 412); Place(url2, 430, 58); url2.Value = cfg.Url2;

        Lbl("Repeat on", 496);
        for (int i = 0; i < 7; i++) {
            string full = ((DayOfWeek)((i + 1) % 7)).ToString();   // Mon..Sun
            days[i] = new Chip { Text = full.Substring(0, 3), Tag = full, Checked = ("," + cfg.Days + ",").Contains("," + full + ",") };
            days[i].SetBounds(17 + i * 58, 514, 58, 42); Controls.Add(days[i]);   // visible chips span 20..420 like the cards
        }

        var arm = new PillButton { Text = "Save & Arm", Primary = true }; arm.SetBounds(10, 570, 180, 62);
        var off = new PillButton { Text = "Disarm" }; off.SetBounds(180, 570, 128, 62);
        var test = new PillButton { Text = "Test now" }; test.SetBounds(298, 570, 132, 62);
        Controls.AddRange(new Control[] { arm, off, test });
        status.SetBounds(0, 640, 440, 24); status.TextAlign = ContentAlignment.MiddleCenter; status.BackColor = Color.Transparent;
        status.ForeColor = Ui.Muted; status.Font = Ui.F(9);
        status.Text = Tools.Find("yt-dlp") == null && Tools.Find("python") == null ? "yt-dlp missing: alarm opens browser instead" : "Ready";
        Controls.Add(status);

        arm.Click += delegate { DoArm(); };
        off.Click += delegate {
            if (FireForm.Active && FireForm.Current != null) FireForm.Current.StopNow();   // test alarm in this window
            Ps("Unregister-ScheduledTask AlarmClock,AlarmClockOpen -Confirm:$false");
            foreach (var p in Process.GetProcessesByName("ffplay")) try { p.Kill(); } catch { }
            foreach (var p in Process.GetProcessesByName("AlarmClock"))
                if (p.Id != Process.GetCurrentProcess().Id && p.MainWindowHandle != IntPtr.Zero) try { p.Kill(); } catch { }   // windowless = guard, let it restore
            status.Text = "Disarmed, alarm stopped";
        };
        test.Click += delegate { if (Save() != null) new FireForm(Cfg.Load()).Show(); };
        FormClosing += delegate(object s, FormClosingEventArgs e) {
            if (FireForm.Active) { e.Cancel = true; status.Text = "Stop the alarm first (Disarm or Stop)"; }
        };
    }

    Cfg Save() {
        if (!Regex.IsMatch(url.Value, @"^https?://")) { status.Text = "Enter a valid http(s) link"; return null; }
        if (url2.Value.Trim() != "" && !Regex.IsMatch(url2.Value, @"^https?://")) { status.Text = "Escalation link must be http(s) or blank"; return null; }
        var d = picker.Selected;
        if (d == null) { status.Text = "Pick an output device"; return null; }
        var sel = new List<string>();
        foreach (var cb in days) if (cb.Checked) sel.Add((string)cb.Tag);
        if (sel.Count == 0) { status.Text = "Pick at least one day"; return null; }
        var c = new Cfg { Time = time.Time24, Url = url.Value.Trim(), Dev = d.Id, Vol = vol.Value, Url2 = url2.Value.Trim(), Days = string.Join(",", sel.ToArray()) };
        c.Save(); return c;
    }

    void DoArm() {
        var c = Save(); if (c == null) return;
        string exe = Application.ExecutablePath;
        DateTime at = DateTime.ParseExact(c.Time, "HH:mm", null), open = at.AddMinutes(-30);
        string openDays = c.Days;
        if (open.Day != at.Day) {
            var sh = new List<string>();
            foreach (var d in c.Days.Split(',')) sh.Add(((DayOfWeek)(((int)Enum.Parse(typeof(DayOfWeek), d) + 6) % 7)).ToString());
            openDays = string.Join(",", sh.ToArray());
        }
        string err = Ps("$a=New-ScheduledTaskAction -Execute '" + exe + "' -Argument '--fire';" +
            "$t=New-ScheduledTaskTrigger -Weekly -DaysOfWeek " + c.Days + " -At '" + c.Time + "';" +
            "$s=New-ScheduledTaskSettingsSet -WakeToRun -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero);" +
            "Register-ScheduledTask AlarmClock -Action $a -Trigger $t -Settings $s -Force | Out-Null;" +
            // second task: open this window 30 min before (days shift back a day if that crosses midnight)
            "$a2=New-ScheduledTaskAction -Execute '" + exe + "';" +
            "$t2=New-ScheduledTaskTrigger -Weekly -DaysOfWeek " + openDays + " -At '" + open.ToString("HH:mm") + "';" +
            "Register-ScheduledTask AlarmClockOpen -Action $a2 -Trigger $t2 -Settings $s -Force | Out-Null");
        if (err != "") { status.Text = "Error: " + err; return; }
        string armed = "Armed " + time.Nice + ": " + c.Days.Replace("day", "");
        status.Text = armed + " | downloading audio...";
        new Thread(delegate() {
            bool ok = Clips.Ensure(c.Url, true) != null && (!c.Url2.StartsWith("http") || Clips.Ensure(c.Url2, false) != null);
            try { BeginInvoke((Action)delegate { status.Text = ok ? armed + " | audio ready" : "Armed, but audio download failed (see alarm.log)"; }); } catch { }
        }) { IsBackground = true }.Start();
    }

    static string Ps(string cmd) {
        var p = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -Command \"" + cmd.Replace("\"", "\\\"") + "\"") {
            UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true });
        string err = p.StandardError.ReadToEnd(); p.WaitForExit();
        return err.Trim();
    }
}

static class Program {
    [STAThread]
    static void Main(string[] args) {
        if (args.Length == 2 && args[0] == "--icon") { Ui.WriteIco(args[1]); return; }
        AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e) { Log.W("CRASH " + e.ExceptionObject); };
        Application.EnableVisualStyles();
        if (args.Length == 4 && args[0] == "--guard") {
            try { Process.GetProcessById(int.Parse(args[1])).WaitForExit(); } catch { }
            Thread.Sleep(2000);
            if (Process.GetProcessesByName("FxSound").Length > 0) return;   // normal stop already restored
            try { Audio.SetDefault(args[2]); } catch { }
            try { if (args[3] != "-") Process.Start(args[3]); } catch { }
            return;
        }
        if (args.Length > 0 && args[0] == "--fire") Application.Run(new FireForm(Cfg.Load()));
        else Application.Run(new MainForm());
    }
}
