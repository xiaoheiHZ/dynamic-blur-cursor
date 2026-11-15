using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CursorBlur;

internal static class Program
{
    [STAThread] private static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--cursor-guard" && int.TryParse(args[1], out int parentId)) { PointerTilt.GuardMain(parentId); return; }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Contains("--self-test")) { PointerTilt.SelfTest(); BlurOverlay.SelfTest(); CaretOverlay.SelfTest(); return; }
        if (args.Contains("--tilt-test")) { PointerTilt.SystemTest(); return; }
        if (args.Contains("--tilt-hold-test")) { PointerTilt.HoldTest(); return; }
        using var instance = new Mutex(true, "Local\\CursorBlur_Desktop_SingleInstance", out bool first);
        if (!first) return;
        Application.Run(new BlurContext(args.Contains("--settings")));
    }
}

internal sealed class Settings
{
    public int Style { get; set; } = 0;
    public int CaretStyle { get; set; } = 1;
    public int Opacity { get; set; } = 60;
    public int Lifetime { get; set; } = 230;
    public int Size { get; set; } = 100;
    public int Spacing { get; set; } = 22;
    public int TiltDegrees { get; set; } = 18;
    public int ColorArgb { get; set; } = Color.FromArgb(130, 205, 255).ToArgb();
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CursorBlur", "settings.json");
    public static Settings Load()
    {
        try {
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new();
            s.Style = Math.Clamp(s.Style, 0, 2); s.Opacity = Math.Clamp(s.Opacity, 10, 100);
            s.CaretStyle = Math.Clamp(s.CaretStyle, 0, 3);
            s.Lifetime = Math.Clamp(s.Lifetime, 100, 600); s.Size = Math.Clamp(s.Size, 50, 180); s.Spacing = Math.Clamp(s.Spacing, 10, 50); s.TiltDegrees = Math.Clamp(s.TiltDegrees, 0, 45);
            return s;
        } catch { return new(); }
    }
    public void Save() { try { Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!); File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
}

internal sealed class BlurContext : ApplicationContext
{
    private readonly Settings settings = Settings.Load();
    private readonly BlurOverlay overlay;
    private readonly CaretOverlay caret;
    private readonly NotifyIcon tray;
    private readonly ToolStripMenuItem toggle;
    private SettingsForm? settingsForm;
    private readonly Icon appIcon;
    public BlurContext(bool openSettings = false)
    {
        appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        overlay = new BlurOverlay(settings);
        caret = new CaretOverlay(settings);
        toggle = new ToolStripMenuItem("暂停特效", null, (_, _) => Toggle());
        var menu = new ContextMenuStrip();
        menu.Items.Add("个性化设置…", null, (_, _) => ShowSettings());
        menu.Items.Add(toggle);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitThread());
        tray = new NotifyIcon { Icon = appIcon, Text = "动态模糊光标", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => ShowSettings();
        overlay.Show();
        caret.Show();
        if (openSettings) ShowSettings();
    }
    private void ShowSettings()
    {
        if (settingsForm is { IsDisposed: false }) { settingsForm.Activate(); return; }
        settingsForm = new SettingsForm(settings, appIcon);
        settingsForm.Show();
    }
    private void Toggle() { overlay.EnabledEffect = !overlay.EnabledEffect; caret.EnabledEffect = overlay.EnabledEffect; toggle.Text = overlay.EnabledEffect ? "暂停特效" : "启用特效"; tray.Text = overlay.EnabledEffect ? "动态模糊光标" : "动态模糊光标（已暂停）"; }
    protected override void ExitThreadCore() { settingsForm?.Close(); tray.Visible = false; tray.Dispose(); caret.Dispose(); overlay.Close(); overlay.Dispose(); appIcon.Dispose(); base.ExitThreadCore(); }
}

internal static class Startup
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CursorBlur";
    public static bool Enabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        string? value = key?.GetValue(ValueName) as string;
        return string.Equals(value, "\"" + Application.ExecutablePath + "\"", StringComparison.OrdinalIgnoreCase);
    }
    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, true) ?? throw new InvalidOperationException("无法打开启动项");
        if (enabled) key.SetValue(ValueName, "\"" + Application.ExecutablePath + "\"", RegistryValueKind.String);
        else key.DeleteValue(ValueName, false);
    }
}
internal sealed class SettingsForm : Form
{
    private readonly Settings settings;
    private readonly ComboBox style = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox caretStyle = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, AccessibleName = "文本光标动画" };
    private readonly TrackBar opacity = Slider(10, 100, 10), lifetime = Slider(100, 600, 100), size = Slider(50, 180, 20), spacing = Slider(10, 50, 10), tilt = Slider(0, 45, 5);
    private readonly Button color = new() { Text = "选择柔光颜色", AutoSize = true, FlatStyle = FlatStyle.Flat };
    private readonly CheckBox startup = new() { Text = "登录 Windows 后自动启动", AutoSize = true, Dock = DockStyle.Fill };
    private bool loading = true;
    public SettingsForm(Settings settings, Icon icon)
    {
        this.settings = settings; Text = "动态模糊光标 · 个性化"; Icon = icon;
        Font = new Font("Microsoft YaHei UI", 9f); BackColor = Color.FromArgb(247, 249, 255); ForeColor = Color.FromArgb(32, 39, 62);
        ClientSize = new Size(520, 616); MinimumSize = new Size(500, 641); StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 18, 24, 14), ColumnCount = 2, RowCount = 12, AutoSize = false };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        for (int i = 0; i < 5; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 57));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        Controls.Add(layout);
        var title = new Label { Text = "让光标留下漂亮的轨迹", Font = new Font(Font.FontFamily, 14f, FontStyle.Bold), AutoSize = true, Dock = DockStyle.Fill };
        layout.Controls.Add(title, 0, 0); layout.SetColumnSpan(title, 2);
        style.Items.AddRange(new object[] { "跟随指针 + 柔光", "跟随指针", "柔光线条" });
        AddRow(layout, 1, "鼠标拖影样式", style);
        caretStyle.Items.AddRange(new object[] { "关闭", "Springy · Neovide", "Springy · Subtle", "Springy · Zed" });
        AddRow(layout, 2, "文本光标动画", caretStyle);
        AddSlider(layout, 3, "拖影不透明度", opacity, "%");
        AddSlider(layout, 4, "消散时间", lifetime, " 毫秒");
        AddSlider(layout, 5, "指针拖影大小", size, "%");
        AddSlider(layout, 6, "拖影间距", spacing, " px");
        AddSlider(layout, 7, "指针倾斜幅度", tilt, "°（0 为关闭）");
        AddRow(layout, 8, "柔光颜色", color);
        layout.Controls.Add(startup, 0, 9); layout.SetColumnSpan(startup, 2);
        var note = new Label { Text = "更改立即生效，设置会自动保存。", AutoSize = true, ForeColor = Color.FromArgb(100, 110, 132), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        layout.Controls.Add(note, 0, 10); layout.SetColumnSpan(note, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var close = new Button { Text = "完成", AutoSize = true, FlatStyle = FlatStyle.System }; close.Click += (_, _) => Close();
        var reset = new Button { Text = "恢复默认", AutoSize = true, FlatStyle = FlatStyle.System }; reset.Click += (_, _) => { var d = new Settings(); settings.Style=d.Style; settings.CaretStyle=d.CaretStyle; settings.Opacity=d.Opacity; settings.Lifetime=d.Lifetime; settings.Size=d.Size; settings.Spacing=d.Spacing; settings.TiltDegrees=d.TiltDegrees; settings.ColorArgb=d.ColorArgb; LoadControls(); Save(); };
        buttons.Controls.Add(close); buttons.Controls.Add(reset); layout.Controls.Add(buttons, 0, 11); layout.SetColumnSpan(buttons, 2);
        style.SelectedIndexChanged += (_, _) => Save();
        caretStyle.SelectedIndexChanged += (_, _) => Save();
        foreach (var t in new[] { opacity, lifetime, size, spacing, tilt }) t.ValueChanged += (_, _) => Save();
        color.Click += (_, _) => { using var picker = new ColorDialog { Color = Color.FromArgb(settings.ColorArgb), FullOpen = true }; if (picker.ShowDialog(this) == DialogResult.OK) { settings.ColorArgb = picker.Color.ToArgb(); UpdateColor(); Save(); } };
        startup.CheckedChanged += (_, _) => {
            if (loading) return;
            try { Startup.Set(startup.Checked); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException or InvalidOperationException) {
                loading = true; startup.Checked = !startup.Checked; loading = false;
                MessageBox.Show(this, "无法更改开机自启动：" + ex.Message, "动态模糊光标", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        LoadControls();
    }
    private static TrackBar Slider(int min, int max, int tick) => new() { Minimum = min, Maximum = max, TickFrequency = tick, Dock = DockStyle.Fill, AutoSize = false, Height = 34 };
    private static void AddRow(TableLayoutPanel table, int row, string caption, Control value) { table.Controls.Add(new Label { Text = caption, AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row); table.Controls.Add(value, 1, row); }
    private static void AddSlider(TableLayoutPanel table, int row, string caption, TrackBar slider, string suffix)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        var val = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight };
        slider.ValueChanged += (_, _) => val.Text = slider.Value + suffix;
        panel.Controls.Add(slider, 0, 0); panel.Controls.Add(val, 1, 0); AddRow(table, row, caption, panel);
    }
    private void LoadControls() { loading = true; style.SelectedIndex = settings.Style; caretStyle.SelectedIndex = settings.CaretStyle; opacity.Value = settings.Opacity; lifetime.Value = settings.Lifetime; size.Value = settings.Size; spacing.Value = settings.Spacing; tilt.Value = settings.TiltDegrees; startup.Checked = Startup.Enabled(); UpdateColor(); loading = false; }
    private void UpdateColor() { color.BackColor = Color.FromArgb(settings.ColorArgb); var c = color.BackColor; color.ForeColor = c.GetBrightness() < .45 ? Color.White : Color.Black; }
    private void Save() { if (loading) return; settings.Style = style.SelectedIndex; settings.CaretStyle = caretStyle.SelectedIndex; settings.Opacity = opacity.Value; settings.Lifetime = lifetime.Value; settings.Size = size.Value; settings.Spacing = spacing.Value; settings.TiltDegrees = tilt.Value; settings.Save(); }
}

internal sealed class BlurOverlay : Form
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 15 };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly List<Sample> samples = new();
    private readonly Settings settings;
    private readonly PointerTilt pointerTilt = new();
    private Point last;
    private bool wasDrawn, enabledEffect = true;
    private Bitmap? cursorImage;
    private IntPtr cursorHandle;
    private Point cursorHotspot;
    private long lastCapture;
    private readonly record struct Sample(Point Position, long Time);
    public bool EnabledEffect { get => enabledEffect; set { enabledEffect=value; samples.Clear(); ClearOverlay(); pointerTilt.Restore(); last=Cursor.Position; } }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams { get { var p=base.CreateParams; p.ExStyle |= 0x00080000 | 0x00000020 | 0x08000000 | 0x00000080; return p; } }
    public BlurOverlay(Settings settings)
    {
        this.settings=settings; FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false; StartPosition=FormStartPosition.Manual;
        Bounds=new Rectangle(0,0,1,1); TopMost=true; last=Cursor.Position;
        timer.Tick += (_,_) => Tick(); timer.Start();
    }
    private void Tick()
    {
        if (!enabledEffect) return;
        long now=clock.ElapsedMilliseconds;
        var ci=new CURSORINFO { cbSize=Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref ci) || (ci.flags & 1) == 0) { samples.Clear(); ClearOverlay(); pointerTilt.Restore(); last=Cursor.Position; return; }
        Point current=Cursor.Position;
        pointerTilt.Update(current.X-last.X, settings.TiltDegrees, now);
        if (current!=last) { if(samples.Count==0) samples.Add(new Sample(last,now-15)); samples.Add(new Sample(current,now)); last=current; }
        samples.RemoveAll(s=>now-s.Time>settings.Lifetime);
        if(samples.Count<2) { ClearOverlay(); return; }
        if(settings.Style!=2 && (cursorHandle!=ci.hCursor || now-lastCapture>300)) CaptureCursor(ci.hCursor, now);
        int margin=cursorImage is null?30:Math.Min(270,(int)(Math.Max(cursorImage.Width,cursorImage.Height)*settings.Size/100f)+30);
        int left=samples.Min(s=>s.Position.X)-margin, top=samples.Min(s=>s.Position.Y)-margin;
        int right=samples.Max(s=>s.Position.X)+margin, bottom=samples.Max(s=>s.Position.Y)+margin;
        int width=right-left,height=bottom-top;
        if(width<=0||height<=0||(long)width*height>16000000) { samples.Clear(); ClearOverlay(); return; }
        using var bmp=new Bitmap(width,height,PixelFormat.Format32bppPArgb);
        using(var g=Graphics.FromImage(bmp)) DrawTrail(g,samples,now,left,top,settings,cursorImage,cursorHotspot,current);
        SetBitmap(bmp,left,top); wasDrawn=true;
    }
    private void CaptureCursor(IntPtr handle, long now)
    {
        lastCapture=now; cursorHandle=handle;
        Bitmap? image=null; Point hotspot=Point.Empty;
        IntPtr copy=CopyIcon(handle);
        if(copy!=IntPtr.Zero)
        {
            try {
                if(GetIconInfo(copy,out var info)) { hotspot=new Point((int)info.xHotspot,(int)info.yHotspot); if(info.hbmMask!=IntPtr.Zero)DeleteObject(info.hbmMask); if(info.hbmColor!=IntPtr.Zero)DeleteObject(info.hbmColor); }
                using var icon=Icon.FromHandle(copy); image=icon.ToBitmap();
            } catch (ArgumentException) { image?.Dispose(); image=null; }
            finally { DestroyIcon(copy); }
        }
        cursorImage?.Dispose(); cursorImage=image; cursorHotspot=hotspot;
    }
    private static void DrawTrail(Graphics g,List<Sample> points,long now,int left,int top,Settings settings,Bitmap? cursor,Point hotspot,Point current)
    {
        g.SmoothingMode=SmoothingMode.AntiAlias; g.InterpolationMode=InterpolationMode.HighQualityBicubic; g.CompositingMode=CompositingMode.SourceOver;
        float strength=settings.Opacity/100f;
        if(settings.Style!=1 || cursor is null)
        {
            Color tint=Color.FromArgb(settings.ColorArgb);
            for(int i=1;i<points.Count;i++)
            {
                var a=points[i-1]; var b=points[i]; float dx=b.Position.X-a.Position.X,dy=b.Position.Y-a.Position.Y;
                float distance=MathF.Sqrt(dx*dx+dy*dy); if(distance<2)continue;
                float fade=Math.Clamp(1f-(now-(a.Time+b.Time)/2f)/settings.Lifetime,0f,1f);
                float op=fade*fade*Math.Clamp(distance/16f,.2f,1f)*strength;
                var p1=new PointF(a.Position.X-left,a.Position.Y-top); var p2=new PointF(b.Position.X-left,b.Position.Y-top);
                Stroke(g,p1,p2,22,(int)(25*op),tint); Stroke(g,p1,p2,13,(int)(45*op),tint); Stroke(g,p1,p2,5,(int)(75*op),tint);
            }
        }
        if(settings.Style==2 || cursor is null) return;
        float scale=settings.Size/100f; int drawn=0;
        float totalDistance=0;
        for(int i=1;i<points.Count;i++) { float dx=points[i].Position.X-points[i-1].Position.X,dy=points[i].Position.Y-points[i-1].Position.Y; totalDistance+=MathF.Sqrt(dx*dx+dy*dy); }
        float step=Math.Max(settings.Spacing,totalDistance/31f), next=0, travelled=0;
        using var attributes=new ImageAttributes();
        for(int i=1;i<points.Count && drawn<32;i++)
        {
            var a=points[i-1]; var b=points[i];
            float dx=b.Position.X-a.Position.X,dy=b.Position.Y-a.Position.Y, distance=MathF.Sqrt(dx*dx+dy*dy);
            if(distance<.1f)continue;
            while(next<=travelled+distance && drawn<32)
            {
                float f=Math.Clamp((next-travelled)/distance,0f,1f); next+=step;
                float x=a.Position.X+dx*f,y=a.Position.Y+dy*f;
                if(MathF.Abs(x-current.X)+MathF.Abs(y-current.Y)<18) continue;
                float age=now-(a.Time+(b.Time-a.Time)*f); float fade=Math.Clamp(1f-age/settings.Lifetime,0f,1f);
                float alpha=Math.Clamp(strength*fade*fade*.72f,0f,.72f); if(alpha<.015f)continue;
                var matrix=new ColorMatrix { Matrix33=alpha }; attributes.SetColorMatrix(matrix);
                int w=Math.Max(1,(int)(cursor.Width*scale)),h=Math.Max(1,(int)(cursor.Height*scale));
                var dest=new Rectangle((int)(x-left-hotspot.X*scale),(int)(y-top-hotspot.Y*scale),w,h);
                g.DrawImage(cursor,dest,0,0,cursor.Width,cursor.Height,GraphicsUnit.Pixel,attributes); drawn++;
            }
            travelled+=distance;
        }
    }
    private static void Stroke(Graphics g,PointF a,PointF b,float width,int alpha,Color color)
    { if(alpha<1)return; using var pen=new Pen(Color.FromArgb(Math.Clamp(alpha,0,255),color),width){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round}; g.DrawLine(pen,a,b); }
    private void ClearOverlay() { if(!wasDrawn||!IsHandleCreated)return; using var blank=new Bitmap(1,1,PixelFormat.Format32bppPArgb); SetBitmap(blank,last.X,last.Y); wasDrawn=false; }
    protected override void Dispose(bool disposing) { if(disposing) { timer.Dispose(); pointerTilt.Dispose(); cursorImage?.Dispose(); } base.Dispose(disposing); }
    private void SetBitmap(Bitmap bitmap,int x,int y) => SetBitmap(Handle, bitmap, x, y);
    internal static void SetBitmap(IntPtr window, Bitmap bitmap,int x,int y)
    {
        IntPtr screen=GetDC(IntPtr.Zero),memory=CreateCompatibleDC(screen),hBitmap=bitmap.GetHbitmap(Color.FromArgb(0)),old=SelectObject(memory,hBitmap);
        try { var size=new SIZE(bitmap.Width,bitmap.Height); var destination=new POINT(x,y); var source=new POINT(0,0); var blend=new BLENDFUNCTION{SourceConstantAlpha=255,AlphaFormat=1};
            if(!UpdateLayeredWindow(window,screen,ref destination,ref size,memory,ref source,0,ref blend,2)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        } finally { SelectObject(memory,old); DeleteObject(hBitmap); DeleteDC(memory); ReleaseDC(IntPtr.Zero,screen); }
    }
    public static void SelfTest()
    {
        var ci=new CURSORINFO { cbSize=Marshal.SizeOf<CURSORINFO>() };
        if(!GetCursorInfo(ref ci) || ci.hCursor==IntPtr.Zero) throw new Exception("Cannot get current cursor");
        using var overlay=new BlurOverlay(new Settings()); overlay.CaptureCursor(ci.hCursor,0);
        if(overlay.cursorImage is null) throw new Exception("Cannot capture current cursor image");
        bool hasAlpha=false; for(int y=0;y<overlay.cursorImage.Height;y++) for(int x=0;x<overlay.cursorImage.Width;x++) if(overlay.cursorImage.GetPixel(x,y).A>0)hasAlpha=true;
        if(!hasAlpha)throw new Exception("Captured cursor image is empty");
        using var bmp=new Bitmap(300,150,PixelFormat.Format32bppPArgb);
        var samples=new List<Sample>{new(new Point(40,70),0),new(new Point(180,70),25)};
        using(var g=Graphics.FromImage(bmp))DrawTrail(g,samples,30,0,0,new Settings(),overlay.cursorImage,overlay.cursorHotspot,new Point(180,70));
        bool drawn=false; for(int y=0;y<bmp.Height;y++) for(int x=0;x<bmp.Width;x++) if(bmp.GetPixel(x,y).A>0)drawn=true;
        if(!drawn)throw new Exception("Trail drawing empty");
        using var ghosts=new Bitmap(300,150,PixelFormat.Format32bppPArgb);
        using(var g=Graphics.FromImage(ghosts))DrawTrail(g,samples,30,0,0,new Settings{Style=1},overlay.cursorImage,overlay.cursorHotspot,new Point(180,70));
        bool ghostDrawn=false; for(int y=0;y<ghosts.Height;y++) for(int x=0;x<ghosts.Width;x++) if(ghosts.GetPixel(x,y).A>0)ghostDrawn=true;
        if(!ghostDrawn)throw new Exception("Cursor-shaped trail drawing empty");
        using var form=new SettingsForm(new Settings(),SystemIcons.Application); form.CreateControl();
    }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X,Y; public POINT(int x,int y){X=x;Y=y;} }
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx,cy; public SIZE(int x,int y){cx=x;cy=y;} }
    [StructLayout(LayoutKind.Sequential,Pack=1)] private struct BLENDFUNCTION { public byte BlendOp,BlendFlags,SourceConstantAlpha,AlphaFormat; }
    [StructLayout(LayoutKind.Sequential)] private struct CURSORINFO { public int cbSize,flags; public IntPtr hCursor; public POINT ptScreenPos; }
    [StructLayout(LayoutKind.Sequential)] private struct ICONINFO { [MarshalAs(UnmanagedType.Bool)] public bool fIcon; public uint xHotspot,yHotspot; public IntPtr hbmMask,hbmColor; }
    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CURSORINFO ci);
    [DllImport("user32.dll")] private static extern IntPtr CopyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr icon,out ICONINFO info);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd,IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool UpdateLayeredWindow(IntPtr hwnd,IntPtr dest,ref POINT point,ref SIZE size,IntPtr src,ref POINT srcPoint,int key,ref BLENDFUNCTION blend,int flags);
}
