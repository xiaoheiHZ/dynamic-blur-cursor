using System.Diagnostics;
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
    private bool drawn;
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
        if (current is not { } caret || previous is not { } last ||
            elapsed > 100 || caret.Owner != last.Owner || caret.Client != last.Client ||
            caret.Bounds.Height != last.Bounds.Height)
        {
            ResetTrail();
            previous = current;
            return;
        }

        previous = current;
        if (!spring.Advance(last.Bounds, caret.Bounds, elapsed / 1000f, style - 1))
        {
            ClearOverlay();
            return;
        }

        Rectangle bounds = Rectangle.Intersect(spring.Bounds(), caret.Client);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            ClearOverlay();
            return;
        }
        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
            spring.Draw(graphics, bounds, Color.FromArgb(settings.ColorArgb), settings.Opacity / 100f);
        BlurOverlay.SetBitmap(Handle, bitmap, bounds.X, bounds.Y);
        drawn = true;
    }

    private void ResetTrail()
    {
        previous = null;
        spring.Reset();
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
                if (overlay.previous is null)
                    throw new InvalidOperationException("Could not read the text box caret");
                SetCaretPos(105, 32);
                var moved = overlay.tracker.Read(form.Handle, thread);
                overlay.DrawFrame(moved, 16);
                if (!overlay.drawn)
                    throw new InvalidOperationException($"Text caret movement did not draw preset {preset}");
                for (int frame = 0; frame < 24; frame++) overlay.DrawFrame(moved, 16);
                if (overlay.drawn)
                    throw new InvalidOperationException($"Text caret preset {preset} did not settle");
            }
            SetCaretPos(12, 10);
            overlay.DrawFrame(overlay.tracker.Read(form.Handle, thread), 16);
            overlay.EnabledEffect = false;
            if (overlay.drawn)
                throw new InvalidOperationException("Pausing did not clear the caret overlay");
        }
        finally { DestroyCaret(); }
    }

    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool CreateCaret(IntPtr window, IntPtr bitmap, int width, int height);
    [DllImport("user32.dll")] private static extern bool SetCaretPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool DestroyCaret();
}
