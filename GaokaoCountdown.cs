using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

// 极简透明倒计时：Catime 风格，使用 Terminess Nerd Font（与 Catime 默认字体一致，SIL 开源协议）
public class CountdownForm : Form
{
    private static readonly DateTime Target = new DateTime(2028, 6, 7, 9, 0, 0);
    private static readonly Color TransparentKey = Color.FromArgb(1, 2, 3);

    private static readonly string ExeDir = AppDomain.CurrentDomain.BaseDirectory;
    private static readonly string FontFile = Path.Combine(ExeDir, "Terminess.ttf");
    private static readonly string FontFamilyName = "Terminess Nerd Font Propo Essence";

    private Timer timer;
    private ContextMenuStrip menu;
    private ContextMenuStrip trayMenu;
    private NotifyIcon trayIcon;

    private Font fontText;
    private static PrivateFontCollection fontCollection; // 静态缓存，避免被 GC 回收后字体失效
    private bool dragging = false;
    private Point dragStart;
    private DateTime dragStartTime;
    private bool sizeDragMode = false; // 右边缘拖动调字号

    // 极简配色（纯色，无描边无发光）
    private static readonly Color[] Colors = new Color[]
    {
        Color.White,                                  // 纯白
        Color.FromArgb(255, 210, 80),                 // 暖黄
        Color.FromArgb(255, 110, 130),                // 粉红
        Color.FromArgb(90, 220, 160),                 // 薄荷绿
        Color.FromArgb(110, 190, 255),                // 天蓝
        Color.Black,                                  // 纯黑
    };
    private static readonly string[] ColorNames = new string[]
    { "白色", "黄色", "粉色", "绿色", "蓝色", "黑色" };
    private int colorIndex = 0;

    private float fontSize = 26f;

    private static readonly string SettingsFile =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.txt");
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "GaokaoCountdown";

    public CountdownForm()
    {
        this.FormBorderStyle = FormBorderStyle.None;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.TopMost = true;
        this.Text = "高考倒计时";
        this.DoubleBuffered = true;
        this.ShowInTaskbar = false;
        this.BackColor = TransparentKey;
        this.TransparencyKey = TransparentKey;
        this.Cursor = Cursors.SizeAll;

        LoadSettings();
        BuildFont();

        this.HandleCreated += (s, e) =>
        {
            // 启动默认：锁定（穿透）模式
            editMode = true; // 预置 true 让 SetEditMode(false) 实际执行
            SetEditMode(false);
        };

        menu = BuildMenu();
        this.ContextMenuStrip = menu;

        trayMenu = BuildMenu();
        trayIcon = new NotifyIcon();
        trayIcon.Text = "高考倒计时";
        trayIcon.Icon = BuildTrayIcon();
        trayIcon.Visible = true;
        trayIcon.ContextMenuStrip = trayMenu;
        trayIcon.MouseClick += (s, e) =>
        {
            if (e.Button == MouseButtons.Left) this.Visible = !this.Visible;
        };

        timer = new Timer();
        timer.Interval = 250;
        timer.Tick += (s, e) => this.Invalidate();
        timer.Start();

        this.MouseDown += OnMouseDown;
        this.MouseMove += OnMouseMove;
        this.MouseUp += OnMouseUp;
        this.MouseWheel += OnMouseWheel;

        // 编辑模式下按 Esc 或点右键退出
        this.KeyPreview = true;
        this.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape && editMode) SetEditMode(false); };

        this.FormClosing += (s, e) =>
        {
            SaveSettings();
            trayIcon.Visible = false;
            trayIcon.Dispose();
        };
    }

    // 不出现在 Alt+Tab 切换列表（WS_EX_TOOLWINDOW）
    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW
            return cp;
        }
    }

    private void BuildFont()
    {
        Font newFont = LoadCustomFont(fontSize);
        Font old = fontText;
        fontText = newFont;   // 先赋值再释放旧字体，避免绘制时用到已释放对象
        if (old != null) old.Dispose();
        UpdateSize();
    }

    // ===== 编辑模式（仿 Catime）：开启后显示亚克力背景块、可拖动/缩放；关闭后穿透不可交互 =====
    private bool editMode = false;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_LAYERED = 0x80000;

    private void SetEditMode(bool on)
    {
        editMode = on;
        // 编辑模式下可交互（显示背景块可拖）；非编辑模式穿透锁定
        IntPtr h = this.Handle;
        int style = GetWindowLong(h, GWL_EXSTYLE);
        if (on)
            SetWindowLong(h, GWL_EXSTYLE, (style & ~WS_EX_TRANSPARENT) | WS_EX_LAYERED);
        else
            SetWindowLong(h, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_LAYERED);
        this.Invalidate();
        SyncChecks();
    }

    // ===== 自定义字体系统 =====
    // fontMode: 0=默认 Terminess（Catime 同款） 1=系统字体 2=fonts 文件夹里的字体文件
    private int fontMode = 0;
    private string systemFontName = "Consolas";
    private string customFontFile = "";          // fontMode=2 时的文件名（fonts 文件夹内）
    private static Dictionary<string, PrivateFontCollection> fileFontCache
        = new Dictionary<string, PrivateFontCollection>();

    private static readonly string FontsDir = Path.Combine(ExeDir, "fonts");

    private Font LoadCustomFont(float size)
    {
        try
        {
            if (fontMode == 1)
            {
                return new Font(systemFontName, size, FontStyle.Bold);
            }
            if (fontMode == 2 && !string.IsNullOrEmpty(customFontFile))
            {
                string path = Path.Combine(FontsDir, customFontFile);
                if (File.Exists(path))
                {
                    PrivateFontCollection pfc;
                    if (!fileFontCache.TryGetValue(customFontFile, out pfc))
                    {
                        pfc = new PrivateFontCollection();
                        pfc.AddFontFile(path);
                        fileFontCache[customFontFile] = pfc;  // 静态持有，防 GC 回收致 GDI+ 崩溃
                    }
                    return new Font(pfc.Families[0], size, FontStyle.Regular);
                }
            }
            // 默认：Terminess（Catime 同款）
            if (File.Exists(FontFile))
            {
                if (fontCollection == null)
                {
                    fontCollection = new PrivateFontCollection();
                    fontCollection.AddFontFile(FontFile);
                }
                return new Font(fontCollection.Families[0], size, FontStyle.Regular);
            }
        }
        catch { }
        return new Font("Consolas", size, FontStyle.Bold); // 兑底等宽字体
    }

    private void UpdateSize()
    {
        using (Graphics g = this.CreateGraphics())
        {
            string sample = "608天 99:99:99";
            SizeF s = g.MeasureString(sample, fontText);
            this.Width = (int)s.Width + 16;
            this.Height = (int)s.Height + 6;
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        ContextMenuStrip m = new ContextMenuStrip();

        // 编辑模式（仿 Catime）：置顶项
        ToolStripMenuItem editItem = new ToolStripMenuItem("编辑模式");
        editItem.Tag = "edit";
        editItem.Font = new Font(m.Font, FontStyle.Bold);
        editItem.Click += (s, e) => { SetEditMode(!editMode); };
        m.Items.Add(editItem);
        m.Items.Add(new ToolStripSeparator());

        // 倒计时目标子菜单
        ToolStripMenuItem targetMenu = new ToolStripMenuItem("目标日期");
        string[][] targets = new string[][]
        {
            new string[] { "2028 高考 (6月7日 9:00)", "2028-06-07 09:00:00" },
        };
        foreach (string[] t in targets)
        {
            ToolStripMenuItem it = new ToolStripMenuItem(t[0]);
            it.Checked = true;
            it.Enabled = false;
            targetMenu.DropDownItems.Add(it);
        }
        m.Items.Add(targetMenu);

        // 颜色子菜单
        ToolStripMenuItem colorMenu = new ToolStripMenuItem("颜色");
        for (int i = 0; i < Colors.Length; i++)
        {
            int idx = i;
            ToolStripMenuItem it = new ToolStripMenuItem(ColorNames[i]);
            it.Tag = "color" + i;
            it.Click += (s, e) => { colorIndex = idx; SyncChecks(); SaveSettings(); this.Invalidate(); };
            colorMenu.DropDownItems.Add(it);
        }
        m.Items.Add(colorMenu);

        // 字体子菜单
        ToolStripMenuItem fontMenu = new ToolStripMenuItem("字体");
        fontMenu.Tag = "fontmenu";
        BuildFontMenu(fontMenu);
        m.Items.Add(fontMenu);

        // 字号子菜单
        ToolStripMenuItem sizeMenu = new ToolStripMenuItem("字号");
        string[] sizeNames = new string[] { "小 14", "中 20", "大 26", "超大 36" };
        float[] sizeVals = new float[] { 14f, 20f, 26f, 36f };
        for (int i = 0; i < sizeVals.Length; i++)
        {
            int idx = i;
            ToolStripMenuItem it = new ToolStripMenuItem(sizeNames[i]);
            it.Tag = "fsize" + i;
            it.Click += (s, e) => { fontSize = sizeVals[idx]; BuildFont(); SyncChecks(); SaveSettings(); };
            sizeMenu.DropDownItems.Add(it);
        }
        m.Items.Add(sizeMenu);

        // 显示模式
        ToolStripMenuItem modeDays = new ToolStripMenuItem("只显示天数");
        modeDays.Tag = "modedays";
        modeDays.Click += (s, e) => { showMode = 0; SyncChecks(); SaveSettings(); };
        m.Items.Add(modeDays);

        ToolStripMenuItem modeFull = new ToolStripMenuItem("天数+时分秒");
        modeFull.Tag = "modefull";
        modeFull.Click += (s, e) => { showMode = 1; SyncChecks(); SaveSettings(); };
        m.Items.Add(modeFull);

        m.Items.Add(new ToolStripSeparator());

        ToolStripMenuItem topItem = new ToolStripMenuItem("窗口置顶");
        topItem.Tag = "top";
        topItem.Click += (s, e) => { this.TopMost = !this.TopMost; SyncChecks(); SaveSettings(); };
        m.Items.Add(topItem);

        ToolStripMenuItem autoRunItem = new ToolStripMenuItem("开机自启");
        autoRunItem.Tag = "autorun";
        autoRunItem.Click += (s, e) => { SetAutoRun(!GetAutoRun()); SyncChecks(); };
        m.Items.Add(autoRunItem);

        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(new ToolStripMenuItem("退出", null, (s, e) => this.Close()));

        SyncChecksFor(m);
        return m;
    }

    private int showMode = 1; // 0=只天数 1=天数+时间

    // ===== 彩蛋系统 =====
    private bool rainbowMode = false;          // 彩虹模式：中键点击开关
    private string quoteText = null;           // 双击显示的语录
    private DateTime quoteUntil = DateTime.MinValue;
    private Random rng = new Random();

    private static readonly string[] Quotes = new string[]
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

    // 剩余天数命中时自动触发彩蛋（特殊颜色 + 感叹号）
    private static readonly int[] SpecialDays = new int[]
    { 666, 555, 444, 333, 222, 111, 100, 99, 88, 66, 50, 40, 30, 20, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 };

    private static Color HslToRgb(float h, float s, float l)
    {
        // h: 0-360
        float c = (1 - Math.Abs(2 * l - 1)) * s;
        float x = c * (1 - Math.Abs((h / 60f) % 2 - 1));
        float m = l - c / 2;
        float r = 0, g = 0, b = 0;
        if (h < 60)      { r = c; g = x; }
        else if (h < 120){ r = x; g = c; }
        else if (h < 180){ g = c; b = x; }
        else if (h < 240){ g = x; b = c; }
        else if (h < 300){ r = x; b = c; }
        else             { r = c; b = x; }
        return Color.FromArgb(255,
            (int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
    }

    private void ResizeForText(string text)
    {
        using (Graphics g = this.CreateGraphics())
        {
            SizeF s = g.MeasureString(text, fontText);
            this.Width = (int)s.Width + 16;
            this.Height = (int)s.Height + 6;
        }
    }

    // 构建字体子菜单：默认 / 浏览系统字体 / fonts 文件夹内所有 .ttf/.otf
    private void BuildFontMenu(ToolStripMenuItem fontMenu)
    {
        fontMenu.DropDownItems.Clear();

        ToolStripMenuItem defItem = new ToolStripMenuItem("默认 (Catime Terminess)");
        defItem.Tag = "fontmode0";
        defItem.Click += (s, e) => { fontMode = 0; BuildFont(); SyncChecks(); SaveSettings(); };
        fontMenu.DropDownItems.Add(defItem);

        ToolStripMenuItem sysItem = new ToolStripMenuItem("选择系统字体...");
        sysItem.Tag = "fontmode1";
        sysItem.Click += (s, e) =>
        {
            using (FontDialog fd = new FontDialog())
            {
                fd.FixedPitchOnly = false;
                fd.ShowColor = false;
                try { fd.Font = new Font(systemFontName, 12f); } catch { }
                if (fd.ShowDialog(this) == DialogResult.OK)
                {
                    systemFontName = fd.Font.Name;
                    fontMode = 1;
                    BuildFont();
                    SyncChecks();
                    SaveSettings();
                }
            }
        };
        fontMenu.DropDownItems.Add(sysItem);

        // fonts 文件夹里的自定义字体文件
        try
        {
            if (Directory.Exists(FontsDir))
            {
                string[] exts = new string[] { "*.ttf", "*.otf" };
                bool found = false;
                foreach (string ext in exts)
                {
                    foreach (string f in Directory.GetFiles(FontsDir, ext))
                    {
                        string name = Path.GetFileName(f);
                        if (name == "Terminess.ttf") continue;
                        found = true;
                        string captured = name;
                        ToolStripMenuItem it = new ToolStripMenuItem(name);
                        it.Tag = "fontfile:" + name;
                        it.Click += (s2, e2) =>
                        {
                            customFontFile = captured;
                            fontMode = 2;
                            fileFontCache.Remove(captured); // 重新加载一次，避免旧缓存异常
                            BuildFont();
                            SyncChecks();
                            SaveSettings();
                        };
                        fontMenu.DropDownItems.Add(it);
                    }
                }
                if (found)
                {
                    fontMenu.DropDownItems.Add(new ToolStripSeparator());
                    ToolStripMenuItem hint = new ToolStripMenuItem("（往 fonts 文件夹加字体文件后重开菜单即可刷新）");
                    hint.Enabled = false;
                    fontMenu.DropDownItems.Add(hint);
                }
            }
            else
            {
                ToolStripMenuItem hint = new ToolStripMenuItem("（把 .ttf/.otf 放到程序目录的 fonts 文件夹）");
                hint.Enabled = false;
                fontMenu.DropDownItems.Add(hint);
            }
        }
        catch { }
    }

    private void SyncChecks()
    {
        SyncChecksFor(menu);
        SyncChecksFor(trayMenu);
    }

    private void SyncChecksFor(ContextMenuStrip m)
    {
        if (m == null) return;
        foreach (ToolStripItem it in m.Items)
        {
            ToolStripMenuItem mi = it as ToolStripMenuItem;
            if (mi == null || mi.Tag == null) continue;
            string tag = mi.Tag.ToString();
            if (tag == "top") mi.Checked = this.TopMost;
            if (tag == "edit") mi.Checked = editMode;
            if (tag == "autorun") mi.Checked = GetAutoRun();
            if (tag == "modedays") mi.Checked = showMode == 0;
            if (tag == "modefull") mi.Checked = showMode == 1;
            if (tag == "fontmode0") mi.Checked = fontMode == 0;
            if (tag == "fontmode1") mi.Checked = fontMode == 1;
            if (tag.StartsWith("fontfile:"))
            {
                string fn = tag.Substring(9);
                mi.Checked = fontMode == 2 && fn == customFontFile;
                if (fontMode == 2)
                {
                    // 文件名可能很长，展示时截短
                    if (fn.Length > 40) mi.Text = fn.Substring(0, 37) + "...";
                }
            }
            if (tag.StartsWith("color")) mi.Checked = (tag == "color" + colorIndex);
            if (tag.StartsWith("fsize"))
            {
                float cur = fontSize;
                int idx = int.Parse(tag.Substring(5));
                float[] vals = new float[] { 14f, 20f, 26f, 36f };
                mi.Checked = vals[idx] == cur;
            }
        }
    }

    private Icon BuildTrayIcon()
    {
        try
        {
            string icoPath = Path.Combine(ExeDir, "app.ico");
            if (File.Exists(icoPath))
            {
                return new Icon(icoPath, SystemInformation.SmallIconSize); // 托盘用小尺寸，避免缩放模糊
            }
        }
        catch { }
        // 兑底：手画 D 字图标
        using (Bitmap bmp = new Bitmap(16, 16))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(30, 32, 50));
                using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 210, 80)))
                using (Font f = new Font("Segoe UI", 9f, FontStyle.Bold))
                {
                    g.DrawString("D", f, b, 3, 1);
                }
            }
            IntPtr hIcon = bmp.GetHicon();
            return Icon.FromHandle(hIcon);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        // 编辑模式：轻微半透明背景块，提示“当前锁定不可交互”
        if (editMode)
        {
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(70, 30, 34, 52)))
            {
                g.FillRectangle(bg, 0, 0, this.Width, this.Height);
            }
        }

        TimeSpan remain = Target - DateTime.Now;
        if (remain.TotalSeconds < 0) remain = TimeSpan.Zero;

        // 彩蛋：双击语录临时替换内容
        if (quoteText != null && DateTime.Now < quoteUntil)
        {
            Font f = fontText;
            if (f == null) return;
            using (SolidBrush b = new SolidBrush(rainbowMode
                ? HslToRgb((DateTime.Now.Ticks / 4000) % 360, 0.9f, 0.65f)
                : Colors[colorIndex]))
            {
                g.DrawString(quoteText, f, b, 6, 0);
            }
            return;
        }
        if (quoteText != null && DateTime.Now >= quoteUntil)
        {
            quoteText = null;
            UpdateSize(); // 恢复正常尺寸
        }

        string text;
        bool isSpecial = Array.IndexOf(SpecialDays, remain.Days) >= 0;
        bool isExamDay = (DateTime.Now.Date == Target.Date);
        if (isExamDay && remain.TotalSeconds > 0)
        {
            // 彩蛋：考试当天，变成“必胜！”
            Font f3 = fontText;
            if (f3 == null) return;
            using (SolidBrush b = new SolidBrush(rainbowMode
                ? HslToRgb((DateTime.Now.Ticks / 4000) % 360, 0.9f, 0.65f)
                : Color.FromArgb(255, 60, 60)))
            {
                g.DrawString("必胜！", f3, b, 6, 0);
            }
            return;
        }
        if (showMode == 0)
            text = remain.Days + "天";
        else
            text = remain.Days + "天 " + remain.Hours.ToString("00") + ":" +
                   remain.Minutes.ToString("00") + ":" + remain.Seconds.ToString("00");
        if (isSpecial) text += "!";

        Font f2 = fontText;
        if (f2 == null) return;
        try
        {
            Color drawColor;
            if (rainbowMode)
                drawColor = HslToRgb((DateTime.Now.Ticks / 4000) % 360, 0.9f, 0.65f);
            else if (isSpecial)
                drawColor = Color.FromArgb(255, 80, 80); // 特殊日子自动变红
            else
                drawColor = Colors[colorIndex];
            using (SolidBrush b = new SolidBrush(drawColor))
            {
                g.DrawString(text, f2, b, 6, 0);
            }
        }
        catch (ArgumentException)
        {
            // 字体对象异常时重建，下一帧恢复
            BuildFont();
        }
    }

    // 鼠标：左键拖动；按住 Alt+滚轮 或 右边缘拖动 调字号
    private void OnMouseDown(object sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Middle)
        {
            // 彩蛋：中键开关彩虹流光模式
            rainbowMode = !rainbowMode;
            this.Invalidate();
            return;
        }
        if (e.Button == MouseButtons.Left)
        {
            dragging = true;
            dragStart = e.Location;
            dragStartTime = DateTime.Now;
            sizeDragMode = e.X > this.Width - 14;
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left)
        {
            // 彩蛋：双击随机励志语录，显示4秒
            quoteText = Quotes[rng.Next(Quotes.Length)];
            quoteUntil = DateTime.Now.AddSeconds(4);
            ResizeForText(quoteText);
            this.Invalidate();
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (dragging && sizeDragMode)
        {
            float delta = (e.X - dragStart.X) / 8f;
            fontSize = Math.Max(10f, Math.Min(72f, fontSize + delta));
            dragStart = e.Location;
            BuildFont();
            return;
        }
        if (dragging)
        {
            Point p = this.Location;
            p.Offset(e.X - dragStart.X, e.Y - dragStart.Y);
            this.Location = p;
        }
        else
        {
            this.Cursor = (e.X > this.Width - 14) ? Cursors.SizeWE : Cursors.SizeAll;
        }
    }

    private void OnMouseUp(object sender, MouseEventArgs e)
    {
        if (dragging)
        {
            dragging = false;
            sizeDragMode = false;
            SaveSettings();
        }
    }

    private void OnMouseWheel(object sender, MouseEventArgs e)
    {
        fontSize = Math.Max(10f, Math.Min(72f, fontSize + Math.Sign(e.Delta) * 2f));
        BuildFont();
        SaveSettings();
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return;
            string[] lines = File.ReadAllLines(SettingsFile);
            if (lines.Length >= 1)
            {
                string[] p = lines[0].Split(',');
                int x = int.Parse(p[0]), y = int.Parse(p[1]);
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                x = Math.Max(wa.Left, Math.Min(x, wa.Right - 80));
                y = Math.Max(wa.Top, Math.Min(y, wa.Bottom - 40));
                this.Location = new Point(x, y);
            }
            if (lines.Length >= 2) int.TryParse(lines[1], out colorIndex);
            if (lines.Length >= 3) float.TryParse(lines[2], out fontSize);
            if (lines.Length >= 4) int.TryParse(lines[3], out showMode);
            if (lines.Length >= 5) this.TopMost = lines[4] == "1";
            if (lines.Length >= 6) int.TryParse(lines[5], out fontMode);
            if (lines.Length >= 7 && lines[6].Length > 0) systemFontName = lines[6];
            if (lines.Length >= 8 && lines[7].Length > 0) customFontFile = lines[7];
            if (colorIndex < 0 || colorIndex >= Colors.Length) colorIndex = 0;
            if (fontMode < 0 || fontMode > 2) fontMode = 0;
            fontSize = Math.Max(10f, Math.Min(72f, fontSize));
        }
        catch { }
    }

    private void SaveSettings()
    {
        try
        {
            File.WriteAllLines(SettingsFile, new string[]
            {
                this.Location.X + "," + this.Location.Y,
                colorIndex.ToString(),
                fontSize.ToString(),
                showMode.ToString(),
                this.TopMost ? "1" : "0",
                fontMode.ToString(),
                systemFontName,
                customFontFile
            });
        }
        catch { }
    }

    private bool GetAutoRun()
    {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
        {
            return key != null && key.GetValue(RunValueName) != null;
        }
    }

    private void SetAutoRun(bool enable)
    {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
        {
            if (enable)
                key.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\"");
            else
                key.DeleteValue(RunValueName, false);
        }
    }

    [STAThread]
    public static void Main()
    {
        Application.EnableVisualStyles();
        Application.Run(new CountdownForm());
    }
}
