using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CursorBlur;

internal sealed class PointerTilt : IDisposable
{
    private readonly Dictionary<int, Skin> skins = new();
    private readonly Dictionary<(int,int), IntPtr> frames = new();
    private EventWaitHandle? guardDone;
    private EventWaitHandle? guardReady;
    private Process? guard;
    private bool attempted, changed;
    private int lastAngle;
    private long lastUpdate;
    private float angle;
    private sealed record Skin(Bitmap Image, Point Hotspot, IntPtr Original, string RegistryName);
    private static readonly (int Id, string Name)[] CursorIds = { (32512,"Arrow"), (32649,"Hand") };
    public void Update(int deltaX, int maximum, long now)
    {
        if(maximum<=0) { Restore(); return; }
        float target=Math.Clamp(deltaX/20f,-1f,1f)*maximum;
        angle+=(target-angle)*.32f;
        int rounded=(int)MathF.Round(angle/3f)*3;
        if(Math.Abs(rounded)<3) { Restore(); return; }
        if(rounded==lastAngle || now-lastUpdate<45) return;
        if(!attempted && !Start()) return;
        if(guard is null || guard.HasExited) { Restore(); return; }
        foreach(var entry in skins)
        {
            var key=(entry.Key,rounded);
            if(!frames.TryGetValue(key,out var frame)) { frame=MakeCursor(entry.Value.Image,entry.Value.Hotspot,rounded); if(frame==IntPtr.Zero)continue; frames[key]=frame; }
            IntPtr copy=CopyIcon(frame);
            if(copy!=IntPtr.Zero) { if(SetSystemCursor(copy,entry.Key)) changed=true; else DestroyIcon(copy); }
        }
        lastAngle=rounded; lastUpdate=now;
    }
    private bool Start()
    {
        attempted=true;
        try {
            foreach(var (id,name) in CursorIds)
            {
                IntPtr shared=LoadCursor(IntPtr.Zero,(IntPtr)id); IntPtr original=CopyIcon(shared);
                if(original==IntPtr.Zero || !GetIconInfo(original,out var info)) { if(original!=IntPtr.Zero)DestroyIcon(original); continue; }
                Point hotspot=new((int)info.xHotspot,(int)info.yHotspot);
                if(info.hbmMask!=IntPtr.Zero)DeleteObject(info.hbmMask); if(info.hbmColor!=IntPtr.Zero)DeleteObject(info.hbmColor);
                try { using var icon=Icon.FromHandle(original); var image=icon.ToBitmap(); skins[id]=new Skin(image,hotspot,original,name); }
                catch { DestroyIcon(original); throw; }
            }
            if(skins.Count==0)return false;
            int pid=Environment.ProcessId;
            guardReady=new EventWaitHandle(false,EventResetMode.ManualReset,ReadyName(pid));
            guardDone=new EventWaitHandle(false,EventResetMode.ManualReset,DoneName(pid));
            guard=Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--cursor-guard " + pid) { UseShellExecute=false, CreateNoWindow=true });
            if(guard is null || !guardReady.WaitOne(5000) || guard.HasExited) { StopGuard(); return false; }
            return true;
        } catch { StopGuard(); return false; }
    }
    private static string ReadyName(int pid)=>"Local\\CursorBlurTiltReady-"+pid;
    private static string DoneName(int pid)=>"Local\\CursorBlurTiltDone-"+pid;
    public static void GuardMain(int pid)
    {
        try {
            using var ready=EventWaitHandle.OpenExisting(ReadyName(pid)); using var done=EventWaitHandle.OpenExisting(DoneName(pid)); using var parent=Process.GetProcessById(pid);
            ready.Set();
            while(!done.WaitOne(500)) { if(parent.HasExited) { SystemParametersInfo(0x0057,0,IntPtr.Zero,0); return; } }
        } catch (ArgumentException) { SystemParametersInfo(0x0057,0,IntPtr.Zero,0); }
          catch (WaitHandleCannotBeOpenedException) { }
    }
    private static IntPtr MakeCursor(Bitmap original,Point hotspot,float degrees)
    {
        int side=Math.Clamp((int)Math.Ceiling(Math.Max(original.Width,original.Height)*3.0),48,512), center=side/2;
        using var bmp=new Bitmap(side,side,PixelFormat.Format32bppPArgb);
        using(var g=Graphics.FromImage(bmp)) { g.SmoothingMode=SmoothingMode.AntiAlias; g.InterpolationMode=InterpolationMode.HighQualityBicubic; g.TranslateTransform(center,center); g.RotateTransform(degrees); g.DrawImage(original,new Rectangle(-hotspot.X,-hotspot.Y,original.Width,original.Height)); }
        IntPtr color=bmp.GetHbitmap(Color.FromArgb(0)); IntPtr mask=CreateBitmap(side,side,1,1,IntPtr.Zero);
        if(mask==IntPtr.Zero) { DeleteObject(color); return IntPtr.Zero; }
        try { var info=new ICONINFO { fIcon=false,xHotspot=(uint)center,yHotspot=(uint)center,hbmMask=mask,hbmColor=color }; return CreateIconIndirect(ref info); }
        finally { DeleteObject(color); DeleteObject(mask); }
    }
    public void Restore()
    {
        if(changed)
        {
            using var key=Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors");
            bool restored=true;
            foreach(var entry in skins)
            {
                string? file=key?.GetValue(entry.Value.RegistryName) as string;
                IntPtr original=string.IsNullOrEmpty(file)?IntPtr.Zero:LoadCursorFromFile(Environment.ExpandEnvironmentVariables(file));
                if(original==IntPtr.Zero)original=CopyIcon(entry.Value.Original);
                if(original==IntPtr.Zero) restored=false;
                else if(!SetSystemCursor(original,entry.Key)) { DestroyIcon(original); restored=false; }
            }
            if(!restored && !SystemParametersInfo(0x0057,0,IntPtr.Zero,0)) throw new InvalidOperationException("Unable to restore system cursor");
            changed=false;
        }
        lastAngle=0;
    }
    private void StopGuard() { try { guardDone?.Set(); } catch (ObjectDisposedException) { } guard?.Dispose(); guard=null; guardReady?.Dispose(); guardReady=null; guardDone?.Dispose(); guardDone=null; }
    public void Dispose()
    {
        Restore(); StopGuard();
        foreach(var frame in frames.Values)DestroyIcon(frame); frames.Clear();
        foreach(var skin in skins.Values){skin.Image.Dispose(); DestroyIcon(skin.Original);} skins.Clear();
    }
    public static void SelfTest()
    {
        using var b=new Bitmap(24,32,PixelFormat.Format32bppPArgb); using(var g=Graphics.FromImage(b)) g.FillPolygon(Brushes.White,new[]{new Point(1,1),new Point(1,27),new Point(18,17)});
        IntPtr h=MakeCursor(b,new Point(1,1),18); if(h==IntPtr.Zero)throw new Exception("Unable to create tilted cursor");
        if(!GetIconInfo(h,out var info))throw new Exception("Unable to inspect tilted cursor");
        if(info.fIcon||info.xHotspot==0||info.yHotspot==0)throw new Exception("Invalid tilted cursor hotspot");
        if(info.hbmMask!=IntPtr.Zero)DeleteObject(info.hbmMask); if(info.hbmColor!=IntPtr.Zero)DeleteObject(info.hbmColor); DestroyIcon(h);
        using var tilt=new PointerTilt(); if(!tilt.Start())throw new Exception("Cursor recovery guard failed to start");
    }
    public static void HoldTest()
    {
        using var tilt=new PointerTilt(); tilt.Update(30,18,100); if(!tilt.changed)throw new Exception("Cursor replacement failed");
        Thread.Sleep(30000);
    }
    public static void SystemTest()
    {
        using var tilt=new PointerTilt(); tilt.Update(30,18,100);
        if(!tilt.changed)throw new Exception("SetSystemCursor did not replace a cursor");
        tilt.Restore();
    }
    [StructLayout(LayoutKind.Sequential)] private struct ICONINFO { [MarshalAs(UnmanagedType.Bool)] public bool fIcon; public uint xHotspot,yHotspot; public IntPtr hbmMask,hbmColor; }
    [DllImport("user32.dll")] private static extern IntPtr LoadCursor(IntPtr instance,IntPtr name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr LoadCursorFromFile(string file);
    [DllImport("user32.dll")] private static extern IntPtr CopyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr icon,out ICONINFO info);
    [DllImport("user32.dll")] private static extern IntPtr CreateIconIndirect(ref ICONINFO info);
    [DllImport("user32.dll")] private static extern bool SetSystemCursor(IntPtr cursor,int id);
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action,uint parameter,IntPtr value,uint flags);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateBitmap(int width,int height,uint planes,uint bits,IntPtr data);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
}
