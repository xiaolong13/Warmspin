using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using Microsoft.Win32;

namespace Warmspin
{
    // ============================================================
    //  基础类型
    // ============================================================
    public class DiskInfo
    {
        public int Index;
        public string Model;
        public string Media;      // 机械 / 固态 / 未知
        public string Bus;
        public double SizeGB;
    }

    public class VolumeInfo
    {
        public char Letter;
        public string FileSystem;
        public double SizeGB;
        public double FreeGB;
        public int DiskIndex = -1;
        public string Media = "未知";
        public int FreePercent { get { return SizeGB > 0 ? (int)Math.Round(FreeGB / SizeGB * 100) : 0; } }
    }

    public class GameEntry
    {
        public string Name;
        public string Path;
        public char Drive;
        public string Exe;        // 可空：Steam 游戏走 steam://
        public string Source;     // Steam / 快捷方式 / 安装项 / 手动
    }

    public class MemInfo
    {
        public double TotalGB;
        public double AvailGB;
        public int LoadPercent;
    }

    public class CheckItem
    {
        public string Level;   // Ok / Warn / Bad / Info
        public string Name;
        public string Detail;
    }

    public class WarmResult
    {
        public long BytesRead;
        public double Seconds;
        public int FileCount;
        public string Error;
    }

    public static class Fmt
    {
        public static string Size(long bytes)
        {
            if (bytes >= 1073741824L) return (bytes / 1073741824.0).ToString("N2") + " GB";
            if (bytes >= 1048576L) return (bytes / 1048576.0).ToString("N1") + " MB";
            if (bytes >= 1024L) return (bytes / 1024.0).ToString("N0") + " KB";
            return bytes + " B";
        }

        public static string Duration(double seconds)
        {
            if (seconds < 60) return ((int)Math.Round(seconds)) + " 秒";
            return ((int)(seconds / 60)) + " 分 " + ((int)(seconds % 60)) + " 秒";
        }
    }

    // ============================================================
    //  系统信息
    // ============================================================
    public static class Sys
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
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

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        public static bool IsAdmin
        {
            get
            {
                try
                {
                    var id = WindowsIdentity.GetCurrent();
                    var p = new WindowsPrincipal(id);
                    return p.IsInRole(WindowsBuiltInRole.Administrator);
                }
                catch { return false; }
            }
        }

        public static MemInfo Memory()
        {
            var m = new MEMORYSTATUSEX();
            m.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (!GlobalMemoryStatusEx(ref m)) return new MemInfo();
            return new MemInfo
            {
                TotalGB = Math.Round(m.ullTotalPhys / 1073741824.0, 1),
                AvailGB = Math.Round(m.ullAvailPhys / 1073741824.0, 1),
                LoadPercent = (int)m.dwMemoryLoad
            };
        }

        public static string CpuName()
        {
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor"))
                foreach (ManagementObject o in s.Get())
                {
                    string n = Convert.ToString(o["Name"]);
                    if (!string.IsNullOrEmpty(n))
                    {
                        n = n.Replace("(R)", "").Replace("(TM)", "").Replace(" CPU", "");
                        return n.Trim();
                    }
                }
            }
            catch { }
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                    if (k != null) return Convert.ToString(k.GetValue("ProcessorNameString")).Trim();
            }
            catch { }
            return "—";
        }

        public static string CpuSpec()
        {
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor"))
                foreach (ManagementObject o in s.Get())
                    return Convert.ToString(o["NumberOfCores"]) + " 核 · " +
                           Convert.ToString(o["NumberOfLogicalProcessors"]) + " 线程";
            }
            catch { }
            return "—";
        }

        public static string GpuName(out string spec)
        {
            spec = "—";
            string best = null;
            long bestRam = -1;
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController"))
                foreach (ManagementObject o in s.Get())
                {
                    string n = Convert.ToString(o["Name"]);
                    if (string.IsNullOrEmpty(n)) continue;
                    if (System.Text.RegularExpressions.Regex.IsMatch(n,
                        "Basic|Remote|Mirror|Virtual|Idd|Oray|Parsec|DisplayLink|DameWare", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        continue;
                    long ram = 0;
                    try { ram = Convert.ToInt64(o["AdapterRAM"]); } catch { }
                    if (ram > bestRam) { bestRam = ram; best = n; }
                }
            }
            catch { }
            if (best == null) return "—";
            if (bestRam > 0) spec = Math.Round(bestRam / 1073741824.0, 0) + " GB 显存";
            return best;
        }

        public static List<DiskInfo> Disks()
        {
            var list = new List<DiskInfo>();
            try
            {
                // root\Microsoft\Windows\Storage 里有 MediaType（3=HDD 4=SSD）
                var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
                scope.Connect();
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT DeviceId, FriendlyName, MediaType, BusType, Size FROM MSFT_PhysicalDisk")))
                foreach (ManagementObject o in s.Get())
                {
                    var d = new DiskInfo();
                    d.Index = Convert.ToInt32(o["DeviceId"]);
                    d.Model = Convert.ToString(o["FriendlyName"]).Trim();
                    int mt = 0;
                    try { mt = Convert.ToInt32(o["MediaType"]); } catch { }
                    d.Media = mt == 4 ? "固态" : (mt == 3 ? "机械" : "未知");
                    int bt = 0;
                    try { bt = Convert.ToInt32(o["BusType"]); } catch { }
                    d.Bus = BusName(bt);
                    try { d.SizeGB = Math.Round(Convert.ToInt64(o["Size"]) / 1073741824.0, 1); } catch { }
                    list.Add(d);
                }
                return list;
            }
            catch { }

            // 退路：注册表里读型号，靠名字猜类型
            try
            {
                var baseKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\SCSI");
                if (baseKey != null)
                {
                    foreach (string devName in baseKey.GetSubKeyNames())
                    {
                        using (var dev = baseKey.OpenSubKey(devName))
                        {
                            if (dev == null) continue;
                            foreach (string instName in dev.GetSubKeyNames())
                            {
                                using (var inst = dev.OpenSubKey(instName))
                                {
                                    if (inst == null) continue;
                                    string name = Convert.ToString(inst.GetValue("FriendlyName"));
                                    if (string.IsNullOrEmpty(name)) continue;
                                    var d = new DiskInfo();
                                    d.Index = list.Count;
                                    d.Model = name;
                                    if (System.Text.RegularExpressions.Regex.IsMatch(name, "SSD|NVME|Solid State", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) d.Media = "固态";
                                    else if (System.Text.RegularExpressions.Regex.IsMatch(name, @"^ST\d|^WD\d|BARRACUDA|TOSHIBA DT|SEAGATE", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) d.Media = "机械";
                                    else d.Media = "未知";
                                    list.Add(d);
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return list;
        }

        private static string BusName(int bt)
        {
            switch (bt)
            {
                case 3: return "ATA";
                case 4: return "ATAPI";
                case 7: return "USB";
                case 8: return "RAID";
                case 11: return "SATA";
                case 17: return "NVMe";
                case 6: return "SCSI";
                default: return "";
            }
        }

        public static List<VolumeInfo> Volumes()
        {
            var vols = new List<VolumeInfo>();
            var map = new Dictionary<char, int>();
            try
            {
                var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
                scope.Connect();
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT DriveLetter, DiskNumber FROM MSFT_Partition")))
                foreach (ManagementObject o in s.Get())
                {
                    var dl = o["DriveLetter"];
                    if (dl == null) continue;
                    char c;
                    try { c = Convert.ToChar(dl); } catch { continue; }
                    if (c < 'A') continue;
                    try { map[c] = Convert.ToInt32(o["DiskNumber"]); } catch { }
                }
            }
            catch { }

            var diskMedia = new Dictionary<int, string>();
            foreach (var d in Disks()) diskMedia[d.Index] = d.Media;

            try
            {
                foreach (var di in DriveInfo.GetDrives())
                {
                    if (!di.IsReady) continue;
                    if (di.DriveType != DriveType.Fixed && di.DriveType != DriveType.Removable) continue;
                    var v = new VolumeInfo();
                    v.Letter = di.Name[0];
                    try { v.FileSystem = di.DriveFormat; } catch { }
                    v.SizeGB = Math.Round(di.TotalSize / 1073741824.0, 1);
                    v.FreeGB = Math.Round(di.TotalFreeSpace / 1073741824.0, 1);
                    int dn;
                    if (map.TryGetValue(v.Letter, out dn))
                    {
                        v.DiskIndex = dn;
                        string m;
                        if (diskMedia.TryGetValue(dn, out m)) v.Media = m;
                    }
                    vols.Add(v);
                }
            }
            catch { }
            return vols;
        }

        // ---------- 各项系统设置 ----------
        public static int ServiceStart(string name)
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name))
                {
                    if (k == null) return -1;
                    object v = k.GetValue("Start");
                    return v == null ? -1 : Convert.ToInt32(v);
                }
            }
            catch { return -1; }
        }

        public static bool ServiceRunning(string name)
        {
            try
            {
                using (var sc = new ServiceController(name))
                    return sc.Status == ServiceControllerStatus.Running;
            }
            catch { return false; }
        }

        public static int Prefetcher()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters"))
                {
                    if (k == null) return -1;
                    object v = k.GetValue("EnablePrefetcher");
                    return v == null ? -1 : Convert.ToInt32(v);
                }
            }
            catch { return -1; }
        }

        public static int DiskTimeoutSeconds()
        {
            try
            {
                var psi = new ProcessStartInfo("powercfg", "/query SCHEME_CURRENT SUB_DISK DISKIDLE")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi))
                {
                    string outp = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    p.WaitForExit(4000);
                    var ms = System.Text.RegularExpressions.Regex.Matches(outp, "0x([0-9A-Fa-f]{8})");
                    if (ms.Count >= 1) return Convert.ToInt32(ms[0].Groups[1].Value, 16);
                }
            }
            catch { }
            return -1;
        }

        public static List<string> DefenderExclusions()
        {
            var list = new List<string>();
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows Defender\Exclusions\Paths"))
                {
                    if (k != null)
                        foreach (string n in k.GetValueNames())
                            list.Add(n);
                }
            }
            catch { }
            return list;
        }

        public static bool IsIndexed(string path)
        {
            try
            {
                var a = File.GetAttributes(path);
                return (a & FileAttributes.NotContentIndexed) != FileAttributes.NotContentIndexed;
            }
            catch { return false; }
        }

        public static List<CheckItem> Checks()
        {
            var list = new List<CheckItem>();

            int sm = ServiceStart("SysMain");
            if (sm == 4) list.Add(new CheckItem { Level = "Bad", Name = "SysMain 预读取服务", Detail = "已禁用，机械盘上这是净损失" });
            else if (!ServiceRunning("SysMain")) list.Add(new CheckItem { Level = "Warn", Name = "SysMain 预读取服务", Detail = "已停止" });
            else list.Add(new CheckItem { Level = "Ok", Name = "SysMain 预读取服务", Detail = "运行中" });

            int pf = Prefetcher();
            if (pf == 0) list.Add(new CheckItem { Level = "Warn", Name = "Prefetch 预读", Detail = "已关闭" });
            else list.Add(new CheckItem { Level = "Ok", Name = "Prefetch 预读", Detail = "值 " + pf });

            int to = DiskTimeoutSeconds();
            if (to > 0) list.Add(new CheckItem { Level = "Warn", Name = "硬盘休眠", Detail = (to / 60) + " 分钟后停转" });
            else list.Add(new CheckItem { Level = "Ok", Name = "硬盘休眠", Detail = "从不停转" });

            var mem = Memory();
            if (mem.TotalGB > 0)
                list.Add(new CheckItem
                {
                    Level = mem.TotalGB < 8 ? "Warn" : "Ok",
                    Name = "物理内存",
                    Detail = mem.TotalGB + " GB，可用 " + mem.AvailGB + " GB"
                });

            var ex = DefenderExclusions();
            if (ex.Count == 0) list.Add(new CheckItem { Level = "Warn", Name = "Defender 排除项", Detail = "无，游戏文件每个都要过一遍扫描" });
            else list.Add(new CheckItem { Level = "Info", Name = "Defender 排除项", Detail = ex.Count + " 条" });

            return list;
        }
    }
}
