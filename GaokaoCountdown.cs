using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

// 高考倒计时悬浮文字（Catime 风格）：无边框透明窗口，锁定后鼠标穿透，只做桌面水印。
// 单文件、无第三方依赖，用 .NET Framework 自带的 csc.exe 编译，见 README。
internal sealed class CountdownForm : Form
{
    // ===== 常量 =====
    private static readonly DateTime Target = new DateTime(2028, 6, 7, 9, 0, 0);

    private static readonly string ExeDir = AppDomain.CurrentDomain.BaseDirectory;
    private static readonly string FontsDir = Path.Combine(ExeDir, "fonts");

    // 默认字体和托盘图标编进 exe（csc /resource:），运行时不需要任何附带文件
    private const string FontResource = "Terminess.ttf";
    private const string IconResource = "app.ico";

    // 设置放 %APPDATA%：exe 在 Program Files 或移动文件夹后仍能保存
    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GaokaoCountdown");
    private static readonly string SettingsPath = Path.Combine(DataDir, "settings.txt");
    private static readonly string LegacySettingsPath = Path.Combine(ExeDir, "settings.txt");

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "GaokaoCountdown";

    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 0x4B31;
    private const uint ModAlt = 0x0001, ModControl = 0x0002;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;

    private static readonly Color TransparentKey = Color.FromArgb(1, 2, 3);

    private static readonly Color[] Palette =
    {
        Color.White,                       // 纯白
        Color.FromArgb(255, 210, 80),      // 暖黄
        Color.FromArgb(255, 110, 130),     // 粉红
        Color.FromArgb(90, 220, 160),      // 薄荷绿
        Color.FromArgb(110, 190, 255),     // 天蓝
        Color.Black,                       // 纯黑
    };

    private static readonly string[] PaletteNames = { "白色", "黄色", "粉色", "绿色", "蓝色", "黑色" };

    private static readonly float[] SizePresets = { 14f, 20f, 26f, 36f };
    private static readonly string[] SizePresetNames = { "小 14", "中 20", "大 26", "超大 36" };

    private static readonly int[] SpecialDays =
        { 666, 555, 444, 333, 222, 111, 100, 99, 88, 66, 50, 40, 30, 20, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 };

    private static readonly string[] Quotes =
    {
        "乾坤未定，你我皆是黑马",
        "所有的失去了，会以另一种方式归来",
        "你要搞清楚自己人生的剧本，你不是父母的续集",
        "同是寒窗苦读，怎能甘拜下风",
        "愿你合上笔盖的那一刻，有战士收刀入鞘的骄傲",
        "半山腰太挤了，我们山顶见",
        "所谓万丈深渊，下去也是前程万里",
        "星光不问赶路人，时光不负有心人",
        "我当然不会试图摘月，我要月亮奔我而来",
        "这笔墨写下的是天亮",
    };

    // ===== 状态 =====
    private readonly Timer timer = new Timer();
    private readonly ContextMenuStrip menu = new ContextMenuStrip();
    private readonly NotifyIcon tray = new NotifyIcon();
    private readonly Random rng = new Random();

    private static PrivateFontCollection defaultFonts;
    private static IntPtr fontBuffer;                  // AddMemoryFont 用的非托管内存，常驻不释放
    private static readonly Dictionary<string, PrivateFontCollection> fileFonts =
        new Dictionary<string, PrivateFontCollection>();

    private Font font;
    private string text = "";
    private bool examDay, specialDay;

    private bool editMode, dragging, sizeDrag, rainbow, hotkeyReady;
    private Point dragStart;

    private int colorIndex, showMode = 1, fontMode;
    private float fontSize = 26f;
    private string systemFontName = "Consolas";
    private string fontFile = "";

    private string quote;
    private DateTime quoteUntil;
    private bool hasPosition;

    public CountdownForm()
    {
        // 最先订阅：下面任何一句都可能提前创建窗口句柄（例如 TopMost 下的 CenterToScreen），
        // 晚订阅就永远等不到 HandleCreated，热键和锁定态都会失效。
        HandleCreated += (s, e) =>
        {
            RegisterEditHotkey();
            SetEditMode(false);                // 启动即锁定
        };

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;   // 尺寸全按文字实测，交给系统缩放会错位
        TopMost = true;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        Text = "高考倒计时";
        BackColor = TransparentKey;
        TransparencyKey = TransparentKey;
        Cursor = Cursors.SizeAll;
        KeyPreview = true;

        LoadSettings();
        if (!hasPosition) CenterToScreen();   // 首次运行摆屏幕中间

        BuildMenu();
        this.ContextMenuStrip = menu;          // 窗口与托盘共用一个菜单，勾选状态天然一致
        tray.Icon = LoadIcon();
        tray.Text = "高考倒计时";
        tray.ContextMenuStrip = menu;
        tray.Visible = true;
        tray.MouseClick += (s, e) =>
        {
            if (e.Button == MouseButtons.Left) Visible = !Visible;
        };

        BuildFont();

        timer.Interval = 200;
        timer.Tick += (s, e) => Tick();
        timer.Start();

        FormClosing += (s, e) =>
        {
            SaveSettings();
            if (hotkeyReady) UnregisterHotKey(Handle, HotkeyId);
            tray.Visible = false;
            tray.Dispose();
        };

        KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Escape && editMode) SetEditMode(false);
        };
    }

    // 不出现在 Alt+Tab 切换列表（WS_EX_TOOLWINDOW）；锁定态加 WS_EX_TRANSPARENT 让鼠标穿透。
    // 用 CreateParams 而不是 SetWindowLong：WinForms 自己也会改 EXSTYLE，直接写会被覆盖。
    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x80;
            if (!editMode) cp.ExStyle |= WS_EX_TRANSPARENT;
            return cp;
        }
    }

    // ===== 内容与绘制 =====
    private void Tick()
    {
        string next = ComposeText();
        bool changed = next != text;
        if (changed)
        {
            text = next;
            FitToText(next);
        }
        if (changed || rainbow) Invalidate();   // 彩虹是逐帧算色，必须持续重绘
    }

    private string ComposeText()
    {
        DateTime now = DateTime.Now;

        if (quote != null && now < quoteUntil) return quote;
        quote = null;

        TimeSpan remain = Target - now;
        if (remain.TotalSeconds < 0) remain = TimeSpan.Zero;

        examDay = now.Date == Target.Date && remain.TotalSeconds > 0;
        if (examDay) return "必胜！";

        specialDay = Array.IndexOf(SpecialDays, remain.Days) >= 0;
        string s = showMode == 0
            ? remain.Days + "天"
            : remain.Days + "天 " + remain.Hours.ToString("00") + ":" +
              remain.Minutes.ToString("00") + ":" + remain.Seconds.ToString("00");
        return specialDay ? s + "!" : s;
    }

    // 窗口尺寸按当前文字实测，文字变化时才调用，避免每秒抖动
    private void FitToText(string s)
    {
        if (font == null) return;
        SizeF size;
        using (Graphics g = CreateGraphics()) size = g.MeasureString(s, font);
        int w = (int)Math.Ceiling(size.Width) + 16;
        int h = (int)Math.Ceiling(size.Height) + 6;
        if (w != Width || h != Height) SetBounds(Left, Top, w, h);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        if (editMode)
        {
            // 编辑模式：实心底块（透明像素会被色键抠掉，半透明画不出效果），提示此刻可拖动/可右键
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(40, 44, 62)))
                g.FillRectangle(bg, ClientRectangle);
        }

        if (font == null || text.Length == 0) return;

        Color color;
        if (rainbow) color = HslToRgb((DateTime.Now.Ticks / 4000) % 360, 0.9f, 0.65f);
        else if (examDay) color = Color.FromArgb(255, 60, 60);
        else if (specialDay) color = Color.FromArgb(255, 80, 80);
        else color = Palette[colorIndex];

        using (SolidBrush b = new SolidBrush(color))
            g.DrawString(text, font, b, 6, 0);
    }

    private static Color HslToRgb(float h, float s, float l)
    {
        float c = (1 - Math.Abs(2 * l - 1)) * s;
        float x = c * (1 - Math.Abs((h / 60f) % 2 - 1));
        float m = l - c / 2;
        float r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; }
        else if (h < 120) { r = x; g = c; }
        else if (h < 180) { g = c; b = x; }
        else if (h < 240) { g = x; b = c; }
        else if (h < 300) { r = x; b = c; }
        else { r = c; b = x; }
        return Color.FromArgb(255,
            (int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
    }

    // ===== 编辑模式 / 热键 =====
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private void SetEditMode(bool on)
    {
        editMode = on;
        UpdateStyles();          // 重算 CreateParams：锁定态挂穿透位，编辑态摘掉
        if (on) Activate();
        RefreshChecks();
        Invalidate();
    }

    // 锁定后窗口收不到鼠标消息，所以必须有全局热键回到编辑模式
    private void RegisterEditHotkey()
    {
        hotkeyReady = RegisterHotKey(Handle, HotkeyId, ModControl | ModAlt, (uint)Keys.E);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
        {
            SetEditMode(!editMode);
            return;
        }
        base.WndProc(ref m);
    }

    // ===== 字体 =====
    private Font CreateFont(float size)
    {
        try
        {
            if (fontMode == 1) return new Font(systemFontName, size, FontStyle.Bold);

            if (fontMode == 2 && fontFile.Length > 0)
            {
                string path = Path.Combine(FontsDir, fontFile);
                if (File.Exists(path))
                    return new Font(GetFileFonts(path).Families[0], size, FontStyle.Regular);
            }

            PrivateFontCollection builtin = GetDefaultFonts();
            if (builtin != null)
                return new Font(builtin.Families[0], size, FontStyle.Regular);
        }
        catch { }
        return new Font("Consolas", size, FontStyle.Bold);   // 兜底
    }

    // 编进 exe 的资源：读成字节数组（字体要用非托管内存喂 AddMemoryFont）
    private static byte[] ReadResource(string name)
    {
        using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        {
            if (s == null) return null;
            using (MemoryStream ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }
    }

    private static PrivateFontCollection GetDefaultFonts()
    {
        if (defaultFonts == null)
        {
            byte[] data = ReadResource(FontResource);
            if (data == null) return null;

            defaultFonts = new PrivateFontCollection();
            // AddMemoryFont 要求这块内存一直有效，所以放在非托管堆且不释放（进程生命周期内就一份）
            fontBuffer = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, fontBuffer, data.Length);
            defaultFonts.AddMemoryFont(fontBuffer, data.Length);
        }
        return defaultFonts;
    }

    // 缓存持有：PrivateFontCollection 被 GC 回收后 GDI+ 会崩
    private static PrivateFontCollection GetFileFonts(string path)
    {
        string key = Path.GetFileName(path);
        PrivateFontCollection pfc;
        if (!fileFonts.TryGetValue(key, out pfc))
        {
            pfc = new PrivateFontCollection();
            pfc.AddFontFile(path);
            fileFonts[key] = pfc;
        }
        return pfc;
    }

    private void BuildFont()
    {
        Font next = CreateFont(fontSize);
        Font old = font;
        font = next;                 // 先换再释放，绘制线程不会拿到已释放对象
        if (old != null) old.Dispose();

        text = ComposeText();
        FitToText(text);
        Invalidate();
    }

    // ===== 鼠标 =====
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button == MouseButtons.Middle)
        {
            rainbow = !rainbow;      // 彩蛋：彩虹流光
            Invalidate();
            return;
        }
        if (e.Button == MouseButtons.Left)
        {
            dragging = true;
            dragStart = e.Location;
            sizeDrag = e.X > Width - 14;   // 右边缘 14px 拖动 = 调字号
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (dragging && sizeDrag)
        {
            fontSize = Math.Max(10f, Math.Min(72f, fontSize + (e.X - dragStart.X) / 8f));
            dragStart = e.Location;
            BuildFont();
            return;
        }
        if (dragging)
        {
            Point p = Location;
            p.Offset(e.X - dragStart.X, e.Y - dragStart.Y);
            Location = p;
            return;
        }
        Cursor = e.X > Width - 14 ? Cursors.SizeWE : Cursors.SizeAll;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!dragging) return;
        dragging = false;
        sizeDrag = false;
        SaveSettings();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        fontSize = Math.Max(10f, Math.Min(72f, fontSize + Math.Sign(e.Delta) * 2f));
        BuildFont();
        SaveSettings();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button != MouseButtons.Left) return;
        quote = Quotes[rng.Next(Quotes.Length)];   // 彩蛋：双击显示语录 4 秒
        quoteUntil = DateTime.Now.AddSeconds(4);
        text = ComposeText();
        FitToText(text);
        Invalidate();
    }

    // ===== 菜单 =====
    private void BuildMenu()
    {
        Add("编辑模式  Ctrl+Alt+E", "edit", () => SetEditMode(!editMode)).Font =
            new Font(menu.Font, FontStyle.Bold);
        menu.Items.Add(new ToolStripSeparator());

        ToolStripMenuItem target = new ToolStripMenuItem("目标：2028 高考 6月7日 9:00");
        target.Enabled = false;
        menu.Items.Add(target);

        ToolStripMenuItem colors = new ToolStripMenuItem("颜色");
        for (int i = 0; i < Palette.Length; i++)
        {
            int idx = i;
            Add(colors, PaletteNames[i], "color:" + i, () => { colorIndex = idx; SaveAndRefresh(); });
        }
        menu.Items.Add(colors);

        ToolStripMenuItem fonts = new ToolStripMenuItem("字体");
        fonts.DropDownOpening += (s, e) => FillFontMenu(fonts);   // 每次打开重扫 fonts 目录
        menu.Items.Add(fonts);
        FillFontMenu(fonts);

        ToolStripMenuItem sizes = new ToolStripMenuItem("字号");
        for (int i = 0; i < SizePresets.Length; i++)
        {
            int idx = i;
            Add(sizes, SizePresetNames[i], "size:" + i, () =>
            {
                fontSize = SizePresets[idx];
                BuildFont();
                SaveAndRefresh();
            });
        }
        menu.Items.Add(sizes);

        Add("只显示天数", "mode0", () => { showMode = 0; BuildFont(); SaveAndRefresh(); });
        Add("天数+时分秒", "mode1", () => { showMode = 1; BuildFont(); SaveAndRefresh(); });

        menu.Items.Add(new ToolStripSeparator());
        Add("窗口置顶", "top", () => { TopMost = !TopMost; SaveAndRefresh(); });
        Add("开机自启", "autorun", () => { SetAutoRun(!GetAutoRun()); RefreshChecks(); });

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("退出", null, (s, e) => Close()));

        RefreshChecks();
    }

    private ToolStripMenuItem Add(string label, string tag, Action click)
    {
        return Add(menu, label, tag, click);
    }

    private static ToolStripMenuItem Add(ToolStripMenuItem parent, string label, string tag, Action click)
    {
        ToolStripMenuItem item = new ToolStripMenuItem(label);
        item.Tag = tag;
        item.Click += (s, e) => click();
        parent.DropDownItems.Add(item);
        return item;
    }

    private static ToolStripMenuItem Add(ContextMenuStrip parent, string label, string tag, Action click)
    {
        ToolStripMenuItem item = new ToolStripMenuItem(label);
        item.Tag = tag;
        item.Click += (s, e) => click();
        parent.Items.Add(item);
        return item;
    }

    private void FillFontMenu(ToolStripMenuItem fonts)
    {
        fonts.DropDownItems.Clear();

        Add(fonts, "默认 (Catime Terminess)", "font0", () => { fontMode = 0; BuildFont(); SaveAndRefresh(); });
        Add(fonts, "选择系统字体...", "font1", PickSystemFont);

        try
        {
            if (!Directory.Exists(FontsDir))
            {
                AddHint(fonts, "（把 .ttf/.otf 放进程序目录的 fonts 文件夹）");
                return;
            }

            bool found = false;
            foreach (string ext in new[] { "*.ttf", "*.otf" })
            {
                foreach (string path in Directory.GetFiles(FontsDir, ext))
                {
                    string name = Path.GetFileName(path);
                    if (name.Equals("Terminess.ttf", StringComparison.OrdinalIgnoreCase)) continue;
                    found = true;
                    string captured = name;
                    Add(fonts, Shorten(name), "fontfile:" + name, () =>
                    {
                        fontFile = captured;
                        fontMode = 2;
                        BuildFont();
                        SaveAndRefresh();
                    });
                }
            }
            if (!found) AddHint(fonts, "（把 .ttf/.otf 放进程序目录的 fonts 文件夹）");
        }
        catch { }
    }

    private static void AddHint(ToolStripMenuItem parent, string label)
    {
        ToolStripMenuItem hint = new ToolStripMenuItem(label);
        hint.Enabled = false;
        parent.DropDownItems.Add(hint);
    }

    private static string Shorten(string name)
    {
        return name.Length > 40 ? name.Substring(0, 37) + "..." : name;
    }

    private void PickSystemFont()
    {
        using (FontDialog fd = new FontDialog())
        {
            fd.ShowColor = false;
            try { fd.Font = new Font(systemFontName, 12f); } catch { }
            if (fd.ShowDialog(this) != DialogResult.OK) return;
            systemFontName = fd.Font.Name;
            fontMode = 1;
            BuildFont();
            SaveAndRefresh();
        }
    }

    private void SaveAndRefresh()
    {
        SaveSettings();
        RefreshChecks();
        Invalidate();
    }

    private void RefreshChecks()
    {
        UpdateChecks(menu.Items);
    }

    private void UpdateChecks(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            ToolStripMenuItem mi = item as ToolStripMenuItem;
            if (mi == null) continue;
            string tag = mi.Tag as string;
            if (tag != null) ApplyCheck(mi, tag);
            if (mi.DropDownItems.Count > 0) UpdateChecks(mi.DropDownItems);
        }
    }

    private void ApplyCheck(ToolStripMenuItem mi, string tag)
    {
        switch (tag)
        {
            case "edit": mi.Checked = editMode; return;
            case "top": mi.Checked = TopMost; return;
            case "autorun": mi.Checked = GetAutoRun(); return;
            case "mode0": mi.Checked = showMode == 0; return;
            case "mode1": mi.Checked = showMode == 1; return;
            case "font0": mi.Checked = fontMode == 0; return;
            case "font1": mi.Checked = fontMode == 1; return;
        }

        if (tag.StartsWith("color:"))
            mi.Checked = tag == "color:" + colorIndex;
        else if (tag.StartsWith("size:"))
            mi.Checked = fontSize == SizePresets[int.Parse(tag.Substring(5), CultureInfo.InvariantCulture)];
        else if (tag.StartsWith("fontfile:"))
            mi.Checked = fontMode == 2 && tag.Substring(9) == fontFile;
    }

    private Icon LoadIcon()
    {
        byte[] ico = ReadResource(IconResource);
        if (ico != null)
        {
            try
            {
                using (MemoryStream ms = new MemoryStream(ico))
                    return new Icon(ms, SystemInformation.SmallIconSize);   // 托盘按小尺寸取帧，避免缩放糊
            }
            catch { }
        }
        try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
        catch { }
        return SystemIcons.Application;
    }

    // ===== 设置 =====
    // settings.txt 行序（旧版一致）：
    // 0 位置 X,Y  1 颜色  2 字号  3 显示模式  4 置顶  5 字体模式  6 系统字体名  7 fonts 内文件名
    private void LoadSettings()
    {
        // 只有旧位置确实有设置才迁移；否则保持“无设置”状态，等首次定位后再写
        bool legacy = !File.Exists(SettingsPath) && File.Exists(LegacySettingsPath);
        string path = legacy ? LegacySettingsPath : SettingsPath;

        try
        {
            if (File.Exists(path))
            {
                string[] lines = File.ReadAllLines(path);
                if (lines.Length >= 1) ApplyPosition(lines[0]);
                if (lines.Length >= 2) int.TryParse(lines[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out colorIndex);
                if (lines.Length >= 3) float.TryParse(lines[2], NumberStyles.Float, CultureInfo.InvariantCulture, out fontSize);
                if (lines.Length >= 4) int.TryParse(lines[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out showMode);
                if (lines.Length >= 5) TopMost = lines[4] == "1";
                if (lines.Length >= 6) int.TryParse(lines[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out fontMode);
                if (lines.Length >= 7 && lines[6].Length > 0) systemFontName = lines[6];
                if (lines.Length >= 8 && lines[7].Length > 0) fontFile = lines[7];
            }
        }
        catch { }

        // 范围校验放在 try 外：文件坏一半也不该带着非法值跑
        if (colorIndex < 0 || colorIndex >= Palette.Length) colorIndex = 0;
        if (fontMode < 0 || fontMode > 2) fontMode = 0;
        if (showMode < 0 || showMode > 1) showMode = 1;
        fontSize = Math.Max(10f, Math.Min(72f, fontSize));

        if (legacy) SaveSettings();   // 迁移到 %APPDATA%，旧文件保留不动
    }

    private void ApplyPosition(string line)
    {
        string[] parts = line.Split(',');
        if (parts.Length < 2) return;

        int x, y;
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out x)) return;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out y)) return;

        // 夹回屏幕内：换显示器 / 改分辨率后不会跑到看不见的地方
        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        Location = new Point(
            Math.Max(wa.Left, Math.Min(x, wa.Right - 80)),
            Math.Max(wa.Top, Math.Min(y, wa.Bottom - 40)));
        hasPosition = true;
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllLines(SettingsPath, new[]
            {
                Location.X.ToString(CultureInfo.InvariantCulture) + "," +
                    Location.Y.ToString(CultureInfo.InvariantCulture),
                colorIndex.ToString(CultureInfo.InvariantCulture),
                fontSize.ToString(CultureInfo.InvariantCulture),
                showMode.ToString(CultureInfo.InvariantCulture),
                TopMost ? "1" : "0",
                fontMode.ToString(CultureInfo.InvariantCulture),
                systemFontName,
                fontFile,
            });
        }
        catch { }
    }

    // ===== 开机自启 =====
    private static bool GetAutoRun()
    {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
            return key != null && key.GetValue(RunValueName) != null;
    }

    private static void SetAutoRun(bool enable)
    {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
        {
            if (enable) key.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\"");
            else key.DeleteValue(RunValueName, false);
        }
    }

    [STAThread]
    public static void Main()
    {
        Application.EnableVisualStyles();
        Application.Run(new CountdownForm());
    }
}
