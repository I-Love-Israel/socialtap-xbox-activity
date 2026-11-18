using System.Runtime.InteropServices;

namespace SocialTapXboxActivity;

internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    /// <summary>Seconds since the last keyboard or mouse input on this PC (0 if unknown).</summary>
    public static int IdleSeconds()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info))
        {
            return 0;
        }

        uint idleMs = unchecked((uint)Environment.TickCount - info.dwTime);
        return (int)Math.Min(idleMs / 1000u, int.MaxValue);
    }
}
