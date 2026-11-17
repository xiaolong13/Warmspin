using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using Microsoft.Win32;

namespace Warmspin
{
    // ============================================================
    //  系统优化动作（每个方法返回一句给人看的日志）
    // ============================================================
    public static class Optimizer
    {
        static void SetServiceStart(string name, int value)
        {
            using (var k = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\" + name, true))
            {
                if (k != null) k.SetValue("Start", value, RegistryValueKind.DWord);
            }
        }

        static string RunHidden(string exe, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    string outp = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    p.WaitForExit(20000);
                    return outp;
                }
            }
            catch (Exception ex) { return "ERROR: " + ex.Message; }
        }

        static string PsQuote(string s)
        {
            return "'" + s.Replace("'", "''") + "'";
        }

        // ---------- SysMain ----------
        public static string EnableSysMain(ChangeState st)
        {
            try
            {
                int cur = Sys.ServiceStart("SysMain");
                if (cur == 4 && !st.SysMainStart.HasValue) st.SysMainStart = 4;

                SetServiceStart("SysMain", 2);
                try
                {
                    using (var sc = new ServiceController("SysMain"))
                    {
                        if (sc.Status != ServiceControllerStatus.Running)
                        {
                            sc.Start();
                            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                        }
                    }
                }
                catch (Exception ex) { return "SysMain 启动类型已改为自动，但服务启动失败：" + ex.Message; }
                return "SysMain 已启用（自动启动且正在运行）";
            }
            catch (Exception ex) { return "SysMain 处理失败：" + ex.Message; }
        }

        // ---------- Prefetch ----------
        public static string EnsurePrefetch(ChangeState st)
        {
            try
            {
                int cur = Sys.Prefetcher();
                if (cur == 0)
                {
                    using (var k = Registry.LocalMachine.OpenSubKey(
                        @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters", true))
                    {
                        if (k != null)
                        {
                            if (!st.Prefetcher.HasValue) st.Prefetcher = 0;
                            k.SetValue("EnablePrefetcher", 3, RegistryValueKind.DWord);
                        }
                    }
                    return "Prefetch 已恢复为 3";
                }
                return "Prefetch 本来就正常（值 " + cur + "）";
            }
            catch (Exception ex) { return "Prefetch 处理失败：" + ex.Message; }
        }

        // ---------- Defender 排除 ----------
        public static string ExcludeDefender(IEnumerable<string> dirs, ChangeState st)
        {
            var existing = new HashSet<string>(Sys.DefenderExclusions(), StringComparer.OrdinalIgnoreCase);
            int added = 0;
            foreach (string d in dirs)
            {
                if (string.IsNullOrEmpty(d)) continue;
                bool covered = false;
                foreach (string e in existing)
                {
                    string ee = e.TrimEnd('\\');
                    if (d.StartsWith(ee, StringComparison.OrdinalIgnoreCase)) { covered = true; break; }
                }
                if (covered) continue;

                string outp = RunHidden("powershell.exe",
                    "-NoProfile -NonInteractive -WindowStyle Hidden -Command \"Add-MpPreference -ExclusionPath " + PsQuote(d) + " -ErrorAction SilentlyContinue\"");
                if (outp != null && outp.StartsWith("ERROR")) continue;
                if (!st.DefenderAdded.Contains(d)) st.DefenderAdded.Add(d);
                added++;
            }
            return added > 0 ? ("Defender 已排除 " + added + " 个游戏目录") : "Defender 排除项已经覆盖了这些目录";
        }

        // ---------- 关闭内容索引 ----------
        public static string DisableIndexing(IEnumerable<string> dirs, ChangeState st, Action<int, int> progress)
        {
            int changed = 0;
            foreach (string dir in dirs)
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                if (!Sys.IsIndexed(dir)) continue;

                var files = new List<string>();
                try
                {
                    files.Add(dir);
                    files.AddRange(Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories));
                }
                catch { }

                int total = files.Count;
                int i = 0;
                foreach (string f in files)
                {
                    i++;
                    try
                    {
                        var a = File.GetAttributes(f);
                        if ((a & FileAttributes.NotContentIndexed) != FileAttributes.NotContentIndexed)
                        {
                            File.SetAttributes(f, a | FileAttributes.NotContentIndexed);
                            changed++;
                        }
                    }
                    catch { }
                    if (progress != null && i % 400 == 0) progress(i, total);
                }
                if (!st.IndexedPaths.Contains(dir)) st.IndexedPaths.Add(dir);
            }
            return "已把 " + changed + " 个文件/目录标记为不参与索引";
        }

        // ---------- 硬盘休眠 ----------
        public static string DisableDiskSleep(ChangeState st)
        {
            try
            {
                int cur = Sys.DiskTimeoutSeconds();
                if (cur <= 0) return "硬盘本来就是从不停转";
                if (!st.DiskTimeout.HasValue) st.DiskTimeout = cur;
                RunHidden("powercfg", "/change disk-timeout-ac 0");
                RunHidden("powercfg", "/change disk-timeout-dc 0");
                return "硬盘休眠已关闭（原来 " + (cur / 60) + " 分钟）";
            }
            catch (Exception ex) { return "硬盘休眠设置失败：" + ex.Message; }
        }

        // ---------- 碎片整理 ----------
        public static string Defrag(string drive, Action<string> status)
        {
            try
            {
                if (string.IsNullOrEmpty(drive)) return "找不到需要整理的盘";
                var psi = new ProcessStartInfo("defrag.exe", drive + " /O /U")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    var started = DateTime.Now;
                    while (!p.HasExited)
                    {
                        if (status != null)
                            status("碎片整理中… 已运行 " + Math.Round((DateTime.Now - started).TotalMinutes, 1) + " 分钟");
                        System.Threading.Thread.Sleep(500);
                    }
                    return p.ExitCode == 0 ? ("碎片整理完成（" + drive + "）") : ("碎片整理结束，退出码 " + p.ExitCode);
                }
            }
            catch (Exception ex) { return "碎片整理失败：" + ex.Message; }
        }

        // ---------- 还原 ----------
        public static List<string> Undo(ChangeState st)
        {
            var log = new List<string>();

            if (st.SysMainStart.HasValue)
            {
                try
                {
                    SetServiceStart("SysMain", st.SysMainStart.Value);
                    if (st.SysMainStart.Value == 4)
                    {
                        try
                        {
                            using (var sc = new ServiceController("SysMain"))
                                if (sc.Status == ServiceControllerStatus.Running) sc.Stop();
                        }
                        catch { }
                    }
                    log.Add("SysMain 启动类型已还原");
                }
                catch (Exception ex) { log.Add("SysMain 还原失败：" + ex.Message); }
            }

            if (st.Prefetcher.HasValue)
            {
                try
                {
                    using (var k = Registry.LocalMachine.OpenSubKey(
                        @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters", true))
                        if (k != null) k.SetValue("EnablePrefetcher", st.Prefetcher.Value, RegistryValueKind.DWord);
                    log.Add("Prefetch 已还原为 " + st.Prefetcher.Value);
                }
                catch (Exception ex) { log.Add("Prefetch 还原失败：" + ex.Message); }
            }

            foreach (string d in st.DefenderAdded)
            {
                RunHidden("powershell.exe",
                    "-NoProfile -NonInteractive -WindowStyle Hidden -Command \"Remove-MpPreference -ExclusionPath " + PsQuote(d) + " -ErrorAction SilentlyContinue\"");
                log.Add("Defender 排除已移除：" + d);
            }

            foreach (string d in st.IndexedPaths)
            {
                try
                {
                    if (!Directory.Exists(d)) continue;
                    var files = new List<string>();
                    files.Add(d);
                    files.AddRange(Directory.EnumerateFileSystemEntries(d, "*", SearchOption.AllDirectories));
                    foreach (string f in files)
                    {
                        try
                        {
                            var a = File.GetAttributes(f);
                            if ((a & FileAttributes.NotContentIndexed) == FileAttributes.NotContentIndexed)
                                File.SetAttributes(f, a & ~FileAttributes.NotContentIndexed);
                        }
                        catch { }
                    }
                    log.Add("索引标记已恢复：" + d);
                }
                catch (Exception ex) { log.Add("索引恢复失败：" + ex.Message); }
            }

            if (st.DiskTimeout.HasValue)
            {
                try
                {
                    RunHidden("powercfg", "/setacvalueindex SCHEME_CURRENT SUB_DISK DISKIDLE " + st.DiskTimeout.Value);
                    RunHidden("powercfg", "/setdcvalueindex SCHEME_CURRENT SUB_DISK DISKIDLE " + st.DiskTimeout.Value);
                    RunHidden("powercfg", "/setactive SCHEME_CURRENT");
                    log.Add("硬盘休眠已还原为 " + (st.DiskTimeout.Value / 60) + " 分钟");
                }
                catch (Exception ex) { log.Add("硬盘休眠还原失败：" + ex.Message); }
            }

            return log;
        }
    }
}
