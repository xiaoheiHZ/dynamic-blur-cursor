using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CursorBlur;

internal static class TouchpadProfile
{
    private const uint GetTouchpadParameters = 0x00AE;
    private const uint SetTouchpadParameters = 0x00AF;
    private const uint PersistAndNotify = 3;
    private const uint TouchpadPresent = 1;
    private const uint Tap = 1 << 2;
    private const uint TapAndDrag = 1 << 3;
    private const uint TwoFingerTap = 1 << 4;
    private const uint CornerRightClick = 1 << 5;
    private const uint HonorMouseAcceleration = 1 << 6;
    private const uint Pan = 1 << 7;
    private const uint Pinch = 1 << 8;
    private const uint NaturalScroll = 1 << 9;
    private const uint MacFlags = Tap | TapAndDrag | TwoFingerTap | Pan | Pinch | NaturalScroll;
    private const uint ProfileMask = MacFlags | CornerRightClick | HonorMouseAcceleration;
    private const string BackupKey = @"Software\CursorBlur";
    private const string BackupValue = "TouchpadFlagsBeforePreset";

    public static bool TryRead(out Parameters options)
    {
        options = new Parameters { Version = 1 };
        return SystemParametersInfo(GetTouchpadParameters, Size, ref options, 0) &&
            (options.DeviceFlags & TouchpadPresent) != 0;
    }

    public static bool Matches(Parameters options) => (options.UserFlags & ProfileMask) == MacFlags;

    public static bool HasBackup()
    {
        using var key = Registry.CurrentUser.OpenSubKey(BackupKey);
        return key?.GetValue(BackupValue) is int;
    }

    public static void Apply()
    {
        Parameters current = Read();
        if (Matches(current)) return;

        using var key = Registry.CurrentUser.CreateSubKey(BackupKey);
        if (key.GetValue(BackupValue) is not int)
            key.SetValue(BackupValue, (int)(current.UserFlags & ProfileMask), RegistryValueKind.DWord);
        current.UserFlags = (current.UserFlags & ~ProfileMask) | MacFlags;
        Write(current);
    }

    public static void Restore()
    {
        using var key = Registry.CurrentUser.OpenSubKey(BackupKey, writable: true);
        if (key?.GetValue(BackupValue) is not int original) return;

        Parameters current = Read();
        current.UserFlags = (current.UserFlags & ~ProfileMask) | ((uint)original & ProfileMask);
        Write(current);
        key.DeleteValue(BackupValue, throwOnMissingValue: false);
    }

    public static void SetSpeed(int speed)
    {
        Parameters current = Read();
        current.CursorSpeed = (uint)Math.Clamp(speed, 1, 20);
        Write(current);
    }

    private static Parameters Read() => TryRead(out var options)
        ? options : throw new InvalidOperationException("需要 Windows 11 24H2 或更新版本，以及精确式触控板。");

    private static void Write(Parameters options)
    {
        if (!SystemParametersInfo(SetTouchpadParameters, Size, ref options, PersistAndNotify))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static readonly uint Size = (uint)Marshal.SizeOf<Parameters>();

    // TOUCHPAD_PARAMETERS_V1 stores each set of Win32 BOOL bit fields in one DWORD.
    [StructLayout(LayoutKind.Sequential)]
    internal struct Parameters
    {
        public uint Version, MaxContacts, LegacyFeatures, DeviceFlags, UserFlags;
        public uint Sensitivity, CursorSpeed, FeedbackIntensity, ClickForce, RightClickWidth, RightClickHeight;
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint size, ref Parameters options, uint flags);
}
