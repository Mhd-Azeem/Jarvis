using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Jarvis.Windows;

internal static class SystemMonitor
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    private static TimeSpan previousCpu;
    private static DateTime previousTime = DateTime.UtcNow;

    internal static string Snapshot()
    {
        using var process = Process.GetCurrentProcess();
        var now = DateTime.UtcNow;
        var cpuTime = process.TotalProcessorTime;
        var elapsed = (now - previousTime).TotalMilliseconds;
        var cpu = elapsed > 0 ? (cpuTime - previousCpu).TotalMilliseconds / elapsed / Environment.ProcessorCount * 100 : 0;
        previousCpu = cpuTime;
        previousTime = now;

        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        var ram = GlobalMemoryStatusEx(ref memory) ? $"System RAM {memory.MemoryLoad}%" : "RAM unavailable";
        var drive = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && d.DriveType == DriveType.Fixed);
        var storage = drive is null ? "Storage unavailable" : $"{drive.Name} free {drive.AvailableFreeSpace / 1_073_741_824d:0.0} GB";
        var online = NetworkInterface.GetAllNetworkInterfaces().Any(n =>
            n.OperationalStatus == OperationalStatus.Up &&
            n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
            n.NetworkInterfaceType != NetworkInterfaceType.Tunnel);
        return $"Jarvis CPU {Math.Clamp(cpu, 0, 100):0.0}%   •   {ram}   •   {storage}   •   Network {(online ? "connected" : "offline")}";
    }
}
