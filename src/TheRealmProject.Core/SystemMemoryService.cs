using System.Runtime.InteropServices;

namespace TheRealmProject.Core;

public static class SystemMemoryService
{
    public static int GetRecommendedMaximumRamMb()
    {
        var totalMb = GetTotalPhysicalMemoryBytes() / 1024 / 1024;
        if (totalMb <= 0)
            return 8192;

        var maxMb = (int)(totalMb * 0.75);
        return Math.Max(1024, RoundDown(maxMb, 512));
    }

    private static long GetTotalPhysicalMemoryBytes()
    {
        var status = new MemoryStatusEx();
        status.dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>();
        return GlobalMemoryStatusEx(ref status) ? (long)status.ullTotalPhys : 0;
    }

    private static int RoundDown(int value, int step) => value / step * step;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
}
