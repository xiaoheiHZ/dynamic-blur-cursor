using System.Runtime.InteropServices;
using Accessibility;

namespace CursorBlur;

internal readonly record struct CaretPosition(IntPtr Owner, Rectangle Bounds, Rectangle Client);

internal sealed class CaretTracker : IDisposable
{
    private const int CaretObject = -8;
    private const uint Show = 0x8002;
    private const uint Hide = 0x8003;
    private const uint LocationChange = 0x800B;
    private const uint Destroy = 0x8001;
    private const uint Focus = 0x8005;
    private const uint MenuOrMoveFlags = 0x1E;
    private readonly WinEventCallback callback;
    private IntPtr hook, foreground, focus;
    private int generation;
    private CaretEvent? pending;
    private Task<AccessLocation>? lookup;
    private AccessLocation? accessible;

    private readonly record struct CaretEvent(IntPtr Window, int Child, int Generation);
    private readonly record struct AccessLocation(CaretEvent Event, Rectangle Bounds);

    public CaretTracker() => callback = OnWinEvent;

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            if (hook == IntPtr.Zero)
                hook = SetWinEventHook(Destroy, LocationChange, IntPtr.Zero, callback, 0, 0, 0);
        }
        else
        {
            if (hook != IntPtr.Zero) UnhookWinEvent(hook);
            hook = IntPtr.Zero;
            InvalidateAccessible();
        }
    }

    private void InvalidateAccessible()
    {
        generation++;
        pending = null;
        accessible = null;
    }

    private void OnWinEvent(IntPtr _, uint kind, IntPtr window, int objectId, int child, uint thread, uint time)
    {
        if (kind != Focus && objectId != CaretObject) return;
        if (window == IntPtr.Zero || GetAncestor(window, 2) != GetForegroundWindow()) return;
        if (kind == Focus)
        {
            InvalidateAccessible();
            return;
        }
        if (kind is Hide or Destroy)
        {
            InvalidateAccessible();
            return;
        }
        if (kind is Show or LocationChange)
            pending = new CaretEvent(window, child, generation);
    }

    public CaretPosition? Read() => Read(GetForegroundWindow(), 0);

    internal CaretPosition? Read(IntPtr window, uint thread)
    {
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        if (window == IntPtr.Zero || !GetGUIThreadInfo(thread, ref info) || (info.Flags & MenuOrMoveFlags) != 0)
        {
            foreground = focus = IntPtr.Zero;
            InvalidateAccessible();
            return null;
        }
        if (foreground != window || focus != info.Focus)
        {
            foreground = window;
            focus = info.Focus;
            InvalidateAccessible();
        }

        if (info.Caret != IntPtr.Zero && GetAncestor(info.Caret, 2) == window &&
            TryScreenRect(info.Caret, info.Rect, out var native))
        {
            var caret = BuildPosition(info.Caret, native);
            if (thread == 0 && GetForegroundWindow() != window) return null;
            if (caret is not null) return caret;
        }
        // An accessibility provider lives in another process and can take time to respond.
        if (lookup is { IsCompleted: true })
        {
            AccessLocation result = lookup.GetAwaiter().GetResult();
            lookup = null;
            if (result.Event.Generation == generation) accessible = result;
        }
        if (lookup is null && pending is { } request)
        {
            pending = null;
            lookup = Task.Run(() => ReadAccessible(request));
        }
        return accessible is { } fallback && GetAncestor(fallback.Event.Window, 2) == window
            ? BuildPosition(fallback.Event.Window, fallback.Bounds)
            : null;
    }

    private static AccessLocation ReadAccessible(CaretEvent request)
    {
        try
        {
            if (AccessibleObjectFromEvent(request.Window, CaretObject, request.Child,
                    out var caret, out object child) == 0 && caret is not null)
            {
                try
                {
                    caret.accLocation(out int x, out int y, out int width, out int height, child);
                    return new AccessLocation(request, new Rectangle(x, y, width, height));
                }
                finally { Marshal.ReleaseComObject(caret); }
            }
        }
        catch (COMException) { }
        return new AccessLocation(request, Rectangle.Empty);
    }

    private static CaretPosition? BuildPosition(IntPtr owner, Rectangle bounds)
    {
        if (bounds.Height is < 4 or > 240 || bounds.Width is < 0 or > 240 ||
            !GetClientRect(owner, out var localClient) || !TryScreenRect(owner, localClient, out var client))
            return null;

        bounds.Width = Math.Max(2, bounds.Width);
        if (!client.IntersectsWith(bounds)) return null;
        return new CaretPosition(owner, bounds, client);
    }

    private static bool TryScreenRect(IntPtr window, NativeRect native, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        var first = new NativePoint(native.Left, native.Top);
        var last = new NativePoint(native.Right, native.Bottom);
        IntPtr context = SetThreadDpiAwarenessContext(GetWindowDpiAwarenessContext(window));
        if (context == IntPtr.Zero) return false;
        try
        {
            if (!ClientToScreen(window, ref first) || !ClientToScreen(window, ref last)) return false;
        }
        finally { SetThreadDpiAwarenessContext(context); }
        if (!LogicalToPhysicalPointForPerMonitorDPI(window, ref first) ||
            !LogicalToPhysicalPointForPerMonitorDPI(window, ref last)) return false;
        bounds = Rectangle.FromLTRB(Math.Min(first.X, last.X), Math.Min(first.Y, last.Y),
            Math.Max(first.X, last.X), Math.Max(first.Y, last.Y));
        return true;
    }

    public void Dispose() => SetEnabled(false);

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint Size, Flags;
        public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public NativeRect Rect;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X, Y;
        public NativePoint(int x, int y) { X = x; Y = y; }
    }

    private delegate void WinEventCallback(IntPtr hook, uint kind, IntPtr window, int objectId, int child, uint thread, uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, WinEventCallback callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out NativeRect rectangle);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern bool LogicalToPhysicalPointForPerMonitorDPI(IntPtr window, ref NativePoint point);
    [DllImport("oleacc.dll")] private static extern int AccessibleObjectFromEvent(IntPtr window, int objectId, int childId,
        [MarshalAs(UnmanagedType.Interface)] out IAccessible? accessible,
        [MarshalAs(UnmanagedType.Struct)] out object child);
}
