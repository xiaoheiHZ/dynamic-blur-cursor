using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CursorBlur;

internal sealed class CaretOverlay : Form
{
    private readonly Settings settings;
    private readonly CaretTracker tracker = new();
    private readonly SpringTrail spring = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 15 };
    private CaretPosition? previous;
    private long lastTick;
    private int style;
    private bool drawn, animating;
    private bool enabledEffect = true;

    public CaretOverlay(Settings settings)
    {
        this.settings = settings;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(0, 0, 1, 1);
        TopMost = true;
        timer.Tick += (_, _) => Tick();
        timer.Start();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x00080000 | 0x00000020 | 0x08000000 | 0x00000080;
            return parameters;
        }
    }

    public bool EnabledEffect
    {
        set
        {
            enabledEffect = value;
            tracker.SetEnabled(value && settings.CaretStyle > 0);
            ResetTrail();
        }
    }

    private void Tick()
    {
        if (style != settings.CaretStyle)
        {
            style = settings.CaretStyle;
            tracker.SetEnabled(enabledEffect && style > 0);
            ResetTrail();
        }
        if (!enabledEffect || style == 0) return;

        long now = clock.ElapsedMilliseconds;
        long elapsed = now - lastTick;
        lastTick = now;
        DrawFrame(tracker.Read(), elapsed);
    }

    private void DrawFrame(CaretPosition? current, long elapsed)
    {
        if (current is not { } caret)
        {
            ResetTrail();
            return;
        }

        if (current == previous && drawn && !animating) return;
        Rectangle core = BoldBounds(caret.Bounds);
        if (previous is { } last && elapsed <= 100 &&
            caret.Owner == last.Owner && caret.Client == last.Client && caret.Bounds.Height == last.Bounds.Height)
        {
            animating = spring.Advance(last.Bounds, caret.Bounds, elapsed / 1000f, style - 1);
        }
        else
        {
            spring.Reset();
            animating = false;
        }
        previous = current;

        Rectangle glow = Rectangle.Inflate(core, 9, 8);
        Rectangle bounds = Rectangle.Intersect(animating ? Rectangle.Union(spring.Bounds(), glow) : glow, caret.Client);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            ClearOverlay();
            return;
        }
        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TranslateTransform(-bounds.X, -bounds.Y);
            if (animating) spring.Draw(graphics);
            DrawGoldCaret(graphics, core);
        }
        BlurOverlay.SetBitmap(Handle, bitmap, bounds.X, bounds.Y);
        drawn = true;
    }

    private static Rectangle BoldBounds(Rectangle caret)
    {
        int width = Math.Max(caret.Width + 2, Math.Clamp(caret.Height / 5, 4, 12));
        return new Rectangle(caret.Left - (width - caret.Width) / 2, caret.Top, width, caret.Height);
    }

    private static void DrawGoldCaret(Graphics graphics, Rectangle core)
    {
        using var outer = new SolidBrush(Color.FromArgb(20, SpringTrail.Gold));
        using var middle = new SolidBrush(Color.FromArgb(45, SpringTrail.Gold));
        using var inner = new SolidBrush(Color.FromArgb(93, SpringTrail.Gold));
        graphics.FillEllipse(outer, Rectangle.Inflate(core, 8, 8));
        graphics.FillEllipse(middle, Rectangle.Inflate(core, 5, 5));
        graphics.FillEllipse(inner, Rectangle.Inflate(core, 3, 2));
        using var highlight = new LinearGradientBrush(core, Color.FromArgb(255, 224, 130), SpringTrail.Gold, LinearGradientMode.Horizontal);
        graphics.FillRectangle(highlight, core);
    }

    private void ResetTrail()
    {
        previous = null;
        spring.Reset();
        animating = false;
        ClearOverlay();
    }

    private void ClearOverlay()
    {
        if (!drawn) return;
        using var bitmap = new Bitmap(1, 1, PixelFormat.Format32bppPArgb);
        BlurOverlay.SetBitmap(Handle, bitmap, Left, Top);
        drawn = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose();
            tracker.Dispose();
        }
        base.Dispose(disposing);
    }

    public static void SelfTest()
    {
        using var form = new Form { Text = "CursorBlur self-test", TopMost = true };
        using var box = new TextBox { Multiline = true, Dock = DockStyle.Fill, Text = "Springy text caret test\r\nNext line" };
        form.Controls.Add(box);
        form.Show();
        form.Activate();
        box.Focus();
        Application.DoEvents();
        using var overlay = new CaretOverlay(new Settings());
        overlay.timer.Stop();
        overlay.Show();
        uint thread = GetCurrentThreadId();
        if (!CreateCaret(box.Handle, IntPtr.Zero, 2, 18))
            throw new InvalidOperationException("Could not create a native caret for the test");
        try
        {
            for (int preset = 1; preset <= 3; preset++)
            {
                overlay.style = preset;
                overlay.ResetTrail();
                SetCaretPos(12, 10);
                overlay.DrawFrame(overlay.tracker.Read(form.Handle, thread), 16);
                if (!overlay.drawn || overlay.animating)
                    throw new InvalidOperationException("Could not draw a stationary caret");
                SetCaretPos(105, 32);
                var moved = overlay.tracker.Read(form.Handle, thread);
                overlay.DrawFrame(moved, 16);
                if (!overlay.drawn || !overlay.animating)
                    throw new InvalidOperationException($"Text caret movement did not draw preset {preset}");
                for (int frame = 0; frame < 24; frame++) overlay.DrawFrame(moved, 16);
                if (!overlay.drawn || overlay.animating)
                    throw new InvalidOperationException($"Text caret preset {preset} did not settle");
            }
            SetCaretPos(12, 10);
            overlay.DrawFrame(overlay.tracker.Read(form.Handle, thread), 16);
            overlay.EnabledEffect = false;
            if (overlay.drawn)
                throw new InvalidOperationException("Pausing did not clear the caret overlay");

            using var bitmap = new Bitmap(40, 40, PixelFormat.Format32bppPArgb);
            Rectangle core = BoldBounds(new Rectangle(20, 11, 2, 18));
            using (var graphics = Graphics.FromImage(bitmap)) DrawGoldCaret(graphics, core);
            Color gold = bitmap.GetPixel(core.Left, core.Top + 9);
            if (core.Width < 4 || gold.A != 255 || gold.R <= gold.G || gold.G <= gold.B ||
                bitmap.GetPixel(core.Left - 3, core.Top + 9).A == 0)
                throw new InvalidOperationException("Caret did not render a thick gold core and glow");
        }
        finally { DestroyCaret(); }
    }

    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool CreateCaret(IntPtr window, IntPtr bitmap, int width, int height);
    [DllImport("user32.dll")] private static extern bool SetCaretPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool DestroyCaret();
}
