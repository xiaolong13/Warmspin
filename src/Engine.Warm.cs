using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Warmspin
{
    // ============================================================
    //  内存预热
    // ============================================================
    public static class Warmer
    {
        class Pick
        {
            public string Path;
            public long Len;
            public int Prio;
        }

        static readonly string[] ArchiveExt = new string[]
        {
            ".pak", ".vpk", ".bundle", ".forge", ".wad", ".big", ".arc", ".pck",
            ".uasset", ".umap", ".bdt", ".bhd", ".mpk", ".pbo", ".esm", ".bsa",
            ".ba2", ".dat", ".bin", ".lvl", ".assets", ".ress", ".sav", ".blb"
        };

        static readonly string[] SkipExt = new string[]
        {
            ".log", ".tmp", ".dmp", ".etl", ".part", ".crdownload", ".md", ".txt", ".jpg", ".png"
        };

        static int Priority(string name)
        {
            string ext = Path.GetExtension(name).ToLowerInvariant();
            if (Array.IndexOf(ArchiveExt, ext) >= 0) return 0;
            if (ext == ".exe" || ext == ".dll" || ext == ".asi") return 1;
            if (Array.IndexOf(SkipExt, ext) >= 0) return 9;
            return 5;
        }

        /// <summary>把游戏目录里最可能被读的文件读进系统缓存。返回读取量统计。</summary>
        public static WarmResult Run(string dir, int budgetMB, Action<long, long, double> progress)
        {
            var result = new WarmResult();
            List<Pick> picks;
            try
            {
                picks = new List<Pick>();
                foreach (string f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    long len;
                    try { len = new FileInfo(f).Length; } catch { continue; }
                    if (len <= 0) continue;
                    int prio = Priority(f);
                    if (prio >= 9) continue;
                    picks.Add(new Pick { Path = f, Len = len, Prio = prio });
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                return result;
            }

            if (picks.Count == 0)
            {
                result.Error = "目录里没有可读的文件";
                return result;
            }

            picks = picks.OrderBy(p => p.Prio).ThenByDescending(p => p.Len).ToList();

            long budget = (long)Math.Max(64, budgetMB) * 1048576L;
            var selected = new List<Pick>();
            long selBytes = 0;
            foreach (var p in picks)
            {
                if (selBytes >= budget) break;
                long remain = budget - selBytes;
                if (p.Len > remain) continue;       // 单个文件比剩余预算大就跳过，避免预算失控
                selected.Add(p);
                selBytes += p.Len;
            }
            if (selected.Count == 0)
            {
                var smallest = picks.OrderBy(p => p.Len).First();
                selected.Add(smallest);
                selBytes = smallest.Len;
            }

            var buf = new byte[4 * 1024 * 1024];
            long done = 0;
            var sw = Stopwatch.StartNew();
            long lastReport = 0;

            foreach (var p in selected)
            {
                FileStream fs = null;
                try
                {
                    fs = new FileStream(p.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                        1024 * 1024, FileOptions.SequentialScan);
                }
                catch { continue; }

                try
                {
                    while (true)
                    {
                        int n = fs.Read(buf, 0, buf.Length);
                        if (n <= 0) break;
                        done += n;
                        long now = sw.ElapsedMilliseconds;
                        if (progress != null && (now - lastReport >= 120 || done >= selBytes))
                        {
                            lastReport = now;
                            double spd = sw.Elapsed.TotalSeconds > 0.2 ? (done / 1048576.0) / sw.Elapsed.TotalSeconds : 0;
                            progress(done, selBytes, spd);
                        }
                    }
                }
                catch { }
                finally { try { fs.Dispose(); } catch { } }
            }

            sw.Stop();
            result.BytesRead = done;
            result.Seconds = sw.Elapsed.TotalSeconds;
            result.FileCount = selected.Count;
            if (progress != null) progress(done, selBytes, result.Seconds > 0 ? (done / 1048576.0) / result.Seconds : 0);
            return result;
        }
    }

    // ============================================================
    //  启动游戏
    // ============================================================
    public static class Launcher
    {
        /// <summary>Steam 游戏走 steam://（直接跑 exe 会缺 Steam 环境），其它走 exe。</summary>
        public static string Launch(GameEntry g)
        {
            string appId = GameFinder.SteamAppId(g.Path);
            try
            {
                if (!string.IsNullOrEmpty(appId))
                {
                    Process.Start(new ProcessStartInfo("steam://rungameid/" + appId) { UseShellExecute = true });
                    return "已交给 Steam 启动（AppID " + appId + "）";
                }
                string exe = g.Exe;
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) exe = GameFinder.FindExe(g.Path);
                if (string.IsNullOrEmpty(exe)) return "没找到可执行文件";
                Process.Start(new ProcessStartInfo(exe)
                {
                    WorkingDirectory = Path.GetDirectoryName(exe),
                    UseShellExecute = true
                });
                return "已启动 " + Path.GetFileName(exe);
            }
            catch (Exception ex)
            {
                return "启动失败：" + ex.Message;
            }
        }
    }
}
