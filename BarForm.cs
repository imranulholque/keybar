using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;

namespace KeyBar;

sealed class BarForm : Form
{
    record Key(string Main, string Alt, ushort Vk);

    static readonly Key[] KeyList =
    {
        new("2", "@", 0x32),
        new("4", "$", 0x34),
        new("7", "&", 0x37),
        new("9", "(", 0x39),
        new("T", "T", 0x54),
    };

    static readonly Color Accent = Color.FromArgb(77, 208, 225);
    const double MinOpacity = 0.25;

    readonly float s;
    readonly Rectangle[] keyRects = new Rectangle[5];
    Rectangle gripRect, sliderRect, minRect;
    int trackX, trackW;
    int pressed = -1;
    bool pressMin, shift, dragging, sliding;
    Point dragStart, formStart;

    readonly NotifyIcon tray;
    readonly System.Windows.Forms.Timer shiftTimer = new() { Interval = 40 };
    readonly System.Windows.Forms.Timer topTimer = new() { Interval = 3000 };

    static readonly string SettingsFile =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeyBar", "settings.txt");

    int Px(int v) => (int)Math.Round(v * s);

    public BarForm()
    {
        using (var g = Graphics.FromHwnd(IntPtr.Zero)) s = g.DpiX / 96f;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(20, 23, 31);
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

        // Layout (single line)
        int pad = Px(8), kw = Px(40), kh = Px(32), gap = Px(6), gripW = Px(14);
        int h = kh + pad * 2, x = pad;
        gripRect = new Rectangle(x, 0, gripW, h); x += gripW + gap;
        for (int i = 0; i < keyRects.Length; i++) { keyRects[i] = new Rectangle(x, pad, kw, kh); x += kw + gap; }
        x += Px(6);
        sliderRect = new Rectangle(x, 0, Px(70), h);
        trackX = x + Px(6); trackW = Px(70) - Px(12);
        x += Px(70) + Px(10);
        minRect = new Rectangle(x, (h - Px(22)) / 2, Px(22), Px(22)); x += Px(22) + pad;
        ClientSize = new Size(x, h);
        using (var gp = RoundedPath(new Rectangle(0, 0, Width, Height), Px(14))) Region = new Region(gp);

        LoadSettings();

        // Tray
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show / Hide", null, (_, _) => ToggleVisible());
        menu.Items.Add("Exit", null, (_, _) => { tray!.Visible = false; Application.Exit(); });
        tray = new NotifyIcon { Icon = MakeIcon(), Text = "KeyBar", ContextMenuStrip = menu, Visible = true };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ToggleVisible(); };

        shiftTimer.Tick += (_, _) =>
        {
            bool now = (GetAsyncKeyState(0x10) & 0x8000) != 0;
            if (now != shift) { shift = now; Invalidate(); }
        };
        shiftTimer.Start();

        topTimer.Tick += (_, _) => { if (Visible) SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x13); };
        topTimer.Start();
    }

    // ---- never take focus, so keys go to the window you are typing in ----
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000 /*NOACTIVATE*/ | 0x00000080 /*TOOLWINDOW*/;
            return cp;
        }
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0021) { m.Result = (IntPtr)3; return; } // WM_MOUSEACTIVATE -> MA_NOACTIVATE
        base.WndProc(ref m);
    }

    // ---- painting ----
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        var bg = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = RoundedPath(bg, Px(14)))
        using (var br = new LinearGradientBrush(bg, Color.FromArgb(32, 36, 49), Color.FromArgb(15, 17, 24), 90f))
        using (var pen = new Pen(Color.FromArgb(110, Accent), 1.2f))
        {
            g.FillPath(br, path);
            g.DrawPath(pen, path);
        }

        // grip dots
        using (var dot = new SolidBrush(Color.FromArgb(120, 150, 160, 180)))
        {
            int d = Px(3), cx = gripRect.X + gripRect.Width / 2 - d - Px(1), cy = Height / 2 - Px(8);
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 2; c++)
                    g.FillEllipse(dot, cx + c * (d + Px(2)), cy + r * (d + Px(3)), d, d);
        }

        using var big = new Font("Segoe UI Semibold", 13f, FontStyle.Regular, GraphicsUnit.Point);
        using var small = new Font("Segoe UI", 7.5f, FontStyle.Regular, GraphicsUnit.Point);
        using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        for (int i = 0; i < KeyList.Length; i++)
        {
            var k = KeyList[i];
            var r = keyRects[i];
            bool down = pressed == i;
            using (var path = RoundedPath(r, Px(8)))
            {
                Color top = down ? Accent : Color.FromArgb(62, 69, 90);
                Color bot = down ? Color.FromArgb(38, 150, 165) : Color.FromArgb(38, 43, 58);
                using var br = new LinearGradientBrush(r, top, bot, 90f);
                using var pen = new Pen(Color.FromArgb(down ? 200 : 70, 255, 255, 255), 1f);
                g.FillPath(br, path);
                g.DrawPath(pen, path);
            }

            bool hasAlt = k.Alt != k.Main;
            Color txt = down ? Color.FromArgb(15, 20, 28) : Color.White;
            if (!hasAlt)
            {
                using var b = new SolidBrush(txt);
                g.DrawString(k.Main, big, b, r, center);
            }
            else if (shift)
            {
                using var b = new SolidBrush(down ? txt : Accent);
                using var sb = new SolidBrush(Color.FromArgb(130, 190, 200, 215));
                g.DrawString(k.Alt, big, b, r, center);
                g.DrawString(k.Main, small, sb, new Rectangle(r.X + Px(3), r.Y + Px(1), Px(14), Px(14)), center);
            }
            else
            {
                using var b = new SolidBrush(txt);
                using var sb = new SolidBrush(Color.FromArgb(150, 190, 200, 215));
                g.DrawString(k.Main, big, b, r, center);
                g.DrawString(k.Alt, small, sb, new Rectangle(r.Right - Px(16), r.Y + Px(1), Px(14), Px(14)), center);
            }
        }

        // opacity slider
        {
            int cy = Height / 2, th = Px(4);
            double t = (Opacity - MinOpacity) / (1 - MinOpacity);
            int kx = trackX + (int)(t * trackW);
            using var trackBr = new SolidBrush(Color.FromArgb(70, 120, 130, 150));
            using var fillBr = new SolidBrush(Accent);
            using var knob = new SolidBrush(Color.White);
            g.FillRectangle(trackBr, trackX, cy - th / 2, trackW, th);
            g.FillRectangle(fillBr, trackX, cy - th / 2, kx - trackX, th);
            int kd = Px(12);
            g.FillEllipse(knob, kx - kd / 2, cy - kd / 2, kd, kd);
        }

        // minimize button
        {
            bool down = pressMin;
            using var path = RoundedPath(minRect, Px(6));
            using var br = new SolidBrush(down ? Color.FromArgb(120, Accent) : Color.FromArgb(60, 255, 255, 255));
            using var pen = new Pen(Color.White, 1.6f);
            g.FillPath(br, path);
            int y = minRect.Y + minRect.Height / 2 + Px(3);
            g.DrawLine(pen, minRect.X + Px(6), y, minRect.Right - Px(6), y);
        }
    }

    // ---- mouse / touch ----
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;

        for (int i = 0; i < keyRects.Length; i++)
            if (keyRects[i].Contains(e.Location)) { pressed = i; Invalidate(); return; }

        if (minRect.Contains(e.Location)) { pressMin = true; Invalidate(); return; }

        if (sliderRect.Contains(e.Location)) { sliding = true; SetSlider(e.X); return; }

        dragging = true;
        dragStart = Cursor.Position;
        formStart = Location;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragging)
        {
            var p = Cursor.Position;
            Location = new Point(formStart.X + p.X - dragStart.X, formStart.Y + p.Y - dragStart.Y);
        }
        else if (sliding) SetSlider(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (pressed >= 0)
        {
            if (keyRects[pressed].Contains(e.Location)) SendKey(KeyList[pressed].Vk);
            pressed = -1; Invalidate();
        }
        if (pressMin)
        {
            pressMin = false;
            if (minRect.Contains(e.Location)) Hide();
            Invalidate();
        }
        if (dragging || sliding) { dragging = false; sliding = false; SaveSettings(); }
    }

    void SetSlider(int mouseX)
    {
        double t = Math.Clamp((mouseX - trackX) / (double)trackW, 0, 1);
        Opacity = MinOpacity + t * (1 - MinOpacity);
        Invalidate();
    }

    void ToggleVisible()
    {
        if (Visible) Hide();
        else { Show(); SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x13); }
    }

    // ---- key injection (physical Shift is respected automatically by Windows) ----
    static void SendKey(ushort vk)
    {
        ushort scan = (ushort)MapVirtualKey(vk, 0);
        var inputs = new INPUT[2];
        inputs[0] = MakeKey(vk, scan, 0);
        inputs[1] = MakeKey(vk, scan, 0x0002); // KEYUP
        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    static INPUT MakeKey(ushort vk, ushort scan, uint flags) => new()
    {
        type = 1,
        U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } }
    };

    // ---- settings ----
    void LoadSettings()
    {
        var wa = Screen.PrimaryScreen!.WorkingArea;
        var loc = new Point(wa.X + (wa.Width - Width) / 2, wa.Bottom - Height - Px(24));
        double op = 0.85;
        try
        {
            if (File.Exists(SettingsFile))
            {
                var p = File.ReadAllText(SettingsFile).Trim().Split('|');
                var saved = new Point(int.Parse(p[0], CultureInfo.InvariantCulture), int.Parse(p[1], CultureInfo.InvariantCulture));
                op = Math.Clamp(double.Parse(p[2], CultureInfo.InvariantCulture), MinOpacity, 1.0);
                var rect = new Rectangle(saved, Size);
                if (Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(rect))) loc = saved;
            }
        }
        catch { }
        Location = loc;
        Opacity = op;
    }

    void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            File.WriteAllText(SettingsFile, string.Join("|",
                Location.X.ToString(CultureInfo.InvariantCulture),
                Location.Y.ToString(CultureInfo.InvariantCulture),
                Opacity.ToString("0.###", CultureInfo.InvariantCulture)));
        }
        catch { }
    }

    // ---- helpers ----
    static GraphicsPath RoundedPath(Rectangle r, int radius)
    {
        int d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static Icon MakeIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = RoundedPath(new Rectangle(1, 1, 29, 29), 7);
            using var br = new SolidBrush(Accent);
            g.FillPath(br, path);
            using var f = new Font("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("K", f, Brushes.Black, new Rectangle(0, 0, 32, 32), sf);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { tray?.Dispose(); shiftTimer.Dispose(); topTimer.Dispose(); }
        base.Dispose(disposing);
    }

    // ---- Win32 ----
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public InputUnion U; }
    [StructLayout(LayoutKind.Explicit)] struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
}
