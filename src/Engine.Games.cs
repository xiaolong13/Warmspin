using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Warmspin
{
    // ============================================================
    //  数据目录 / 配置
    // ============================================================
    public static class Store
    {
        public static readonly string Dir = ResolveDir();

        /// <summary>优先用 %LOCALAPPDATA%\Warmspin；不可写时退回 exe 旁的 data 目录（绿色版也能用）</summary>
        static string ResolveDir()
        {
            string roaming = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Warmspin");
            string portable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            foreach (string d in new[] { roaming, portable })
            {
                try
                {
                    Directory.CreateDirectory(d);
                    string probe = Path.Combine(d, ".w");
                    File.WriteAllText(probe, "1");
                    File.Delete(probe);
                    return d;
                }
                catch { }
            }
            return roaming;
        }

        public static string PathOf(string name) { return Path.Combine(Dir, name); }

        public static void Ensure()
        {
            try { if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir); } catch { }
        }

        /// <summary>读写简单的 key=value 行文件</summary>
        public static List<string[]> ReadPairs(string file)
        {
            var list = new List<string[]>();
            try
            {
                if (!File.Exists(file)) return list;
                foreach (string line in File.ReadAllLines(file, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    list.Add(new[] { line.Substring(0, i), line.Substring(i + 1) });
                }
            }
            catch { }
            return list;
        }

        public static List<string> ReadLines(string file)
        {
            try
            {
                if (!File.Exists(file)) return new List<string>();
                return File.ReadAllLines(file, Encoding.UTF8).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            }
            catch { return new List<string>(); }
        }

        public static void WriteLines(string file, IEnumerable<string> lines)
        {
            try
            {
                Ensure();
                File.WriteAllLines(file, lines, new UTF8Encoding(true));
            }
            catch { }
        }
    }

    // ============================================================
    //  回滚状态
    // ============================================================
    public class ChangeState
    {
        public string Timestamp = "";
        public int? SysMainStart;
        public int? Prefetcher;
        public int? DiskTimeout;
        public List<string> DefenderAdded = new List<string>();
        public List<string> IndexedPaths = new List<string>();

        public static ChangeState Load()
        {
            var s = new ChangeState();
            var pairs = Store.ReadPairs(Store.PathOf("state.txt"));
            foreach (var p in pairs)
            {
                switch (p[0])
                {
                    case "timestamp": s.Timestamp = p[1]; break;
                    case "sysmain": s.SysMainStart = ToInt(p[1]); break;
                    case "prefetcher": s.Prefetcher = ToInt(p[1]); break;
                    case "disktimeout": s.DiskTimeout = ToInt(p[1]); break;
                    case "defender": s.DefenderAdded.Add(p[1]); break;
                    case "indexed": s.IndexedPaths.Add(p[1]); break;
                }
            }
            return s;
        }

        static int? ToInt(string s)
        {
            int v;
            if (int.TryParse(s, out v)) return v;
            return null;
        }

        public void Save()
        {
            var lines = new List<string>();
            lines.Add("timestamp=" + (string.IsNullOrEmpty(Timestamp) ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") : Timestamp));
            if (SysMainStart.HasValue) lines.Add("sysmain=" + SysMainStart.Value);
            if (Prefetcher.HasValue) lines.Add("prefetcher=" + Prefetcher.Value);
            if (DiskTimeout.HasValue) lines.Add("disktimeout=" + DiskTimeout.Value);
            foreach (string d in DefenderAdded) lines.Add("defender=" + d);
            foreach (string d in IndexedPaths) lines.Add("indexed=" + d);
            Store.WriteLines(Store.PathOf("state.txt"), lines);
        }

        public bool IsEmpty
        {
            get
            {
                return !SysMainStart.HasValue && !Prefetcher.HasValue && !DiskTimeout.HasValue
                       && DefenderAdded.Count == 0 && IndexedPaths.Count == 0;
            }
        }
    }

    // ============================================================
    //  游戏发现
    // ============================================================
    public static class GameFinder
    {
        static readonly Regex Noise = new Regex(
            "微信|WeChat|QQ|钉钉|DingTalk|迅雷|Thunder|酷狗|KuGou|网易云|哔哩|Bilibili|抖音|Douyin|快手|豆包|KOOK|会议|WeMeet|游戏加加|GamePP|XIBERIA|OBS|VLC|PotPlayer|网盘|夸克|Quark|输入法|浏览器|Chrome|Edge|Firefox|驱动|鲁大师|图吧|ToDesk|向日葵|Oray|TeamViewer|AnyDesk|Wallpaper|7-Zip|WinRAR|KeePass|加速器|平台|助手|工具|Steamworks|Visual C\\+\\+|\\.NET|Runtime|SDK|Update|卸载|Uninstall|Speedtest|OpenRGB|Harness|萌站",
            RegexOptions.IgnoreCase);

        static bool IsSpecialPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return true;
            string p = path.TrimEnd('\\');
            if (Regex.IsMatch(p, @"^[A-Za-z]:$")) return true;
            if (Regex.IsMatch(p, @"\\(Desktop|Public|AppData|Windows)$", RegexOptions.IgnoreCase)) return true;
            if (Regex.IsMatch(p, @"Program Files( \(x86\))?$", RegexOptions.IgnoreCase)) return true;
            if (Regex.IsMatch(p, @"\\AppData\\", RegexOptions.IgnoreCase)) return true;
            if (Regex.IsMatch(p, @"\\Users\\[^\\]+$", RegexOptions.IgnoreCase)) return true;
            return false;
        }

        public static List<GameEntry> Find()
        {
            var found = new List<GameEntry>();
            var index = new Dictionary<string, GameEntry>(StringComparer.OrdinalIgnoreCase);

            Action<string, string, string, string> add = delegate(string name, string path, string exe, string source)
            {
                if (string.IsNullOrEmpty(path)) return;
                string p = path.TrimEnd('\\');
                if (p.Length < 4) return;
                GameEntry exist;
                if (index.TryGetValue(p, out exist))
                {
                    if (!string.IsNullOrEmpty(exe) && string.IsNullOrEmpty(exist.Exe)) exist.Exe = exe;
                    return;
                }
                var g = new GameEntry
                {
                    Name = string.IsNullOrEmpty(name) ? p : name,
                    Path = p,
                    Drive = char.ToUpper(p[0]),
                    Exe = exe,
                    Source = source
                };
                index[p] = g;
                found.Add(g);
            };

            // 1) Steam 库
            foreach (string root in SteamRoots())
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (string d in Directory.GetDirectories(root))
                    {
                        string nm = Path.GetFileName(d);
                        if (Noise.IsMatch(nm)) continue;
                        add(nm, d, null, "Steam");
                    }
                }
                catch { }
            }

            // 2) 桌面 / 开始菜单快捷方式
            try
            {
                dynamic sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                var dirs = new List<string>();
                dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
                dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
                dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    @"Microsoft\Windows\Start Menu\Programs"));
                dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    @"Microsoft\Windows\Start Menu\Programs"));

                int count = 0;
                foreach (string dir in dirs)
                {
                    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                    IEnumerable<string> links;
                    try { links = Directory.GetFiles(dir, "*.lnk", SearchOption.AllDirectories); }
                    catch { continue; }
                    foreach (string lnk in links)
                    {
                        if (count++ > 250) break;
                        string target = null;
                        try { dynamic sc = sh.CreateShortcut(lnk); target = (string)sc.TargetPath; }
                        catch { continue; }
                        if (string.IsNullOrEmpty(target)) continue;
                        if (!target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                        if (Regex.IsMatch(target, @"\\Windows\\|\\Program Files|\\AppData\\", RegexOptions.IgnoreCase)) continue;
                        if (Regex.IsMatch(target, "unins|updater\\.exe|update\\.exe|crashreport|dxsetup|vcredist", RegexOptions.IgnoreCase)) continue;

                        string exeName = Path.GetFileNameWithoutExtension(target);
                        string dirTwo = Path.GetDirectoryName(target);
                        if (string.IsNullOrEmpty(dirTwo)) continue;
                        // <游戏>\<游戏>\game.exe 这种布局，上跳一层才是根目录
                        string parent = Path.GetDirectoryName(dirTwo);
                        if (!string.IsNullOrEmpty(parent) &&
                            string.Equals(Path.GetFileName(dirTwo), exeName, StringComparison.OrdinalIgnoreCase))
                            dirTwo = parent;

                        if (IsSpecialPath(dirTwo)) continue;
                        string nm = Path.GetFileNameWithoutExtension(lnk);
                        if (Noise.IsMatch(nm)) continue;
                        add(nm, dirTwo, target, "快捷方式");
                    }
                }
            }
            catch { }

            // 3) 注册表安装项
            string[] uninstRoots = new string[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };
            foreach (string rootPath in uninstRoots)
            {
                try
                {
                    using (var root = Registry.LocalMachine.OpenSubKey(rootPath))
                    {
                        if (root == null) continue;
                        foreach (string sub in root.GetSubKeyNames())
                        {
                            try
                            {
                                using (var k = root.OpenSubKey(sub))
                                {
                                    if (k == null) continue;
                                    string dn = Convert.ToString(k.GetValue("DisplayName"));
                                    string loc = Convert.ToString(k.GetValue("InstallLocation"));
                                    if (string.IsNullOrEmpty(dn) || string.IsNullOrEmpty(loc)) continue;
                                    loc = loc.Trim().TrimEnd('\\');
                                    if (loc.Length < 4) continue;
                                    if (Noise.IsMatch(dn)) continue;
                                    if (IsSpecialPath(loc)) continue;
                                    if (!Directory.Exists(loc)) continue;

                                    string exe = null;
                                    long best = -1;
                                    try
                                    {
                                        foreach (string f in Directory.GetFiles(loc, "*.exe"))
                                        {
                                            string fn = Path.GetFileName(f);
                                            if (Regex.IsMatch(fn, "unins|setup|install|vcredist|dxsetup|crash|update", RegexOptions.IgnoreCase)) continue;
                                            long len = new FileInfo(f).Length;
                                            if (len > best) { best = len; exe = f; }
                                        }
                                    }
                                    catch { }
                                    add(dn, loc, exe, "安装项");
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }

            // 4) 手动添加
            foreach (string line in Store.ReadLines(Store.PathOf("custom.txt")))
            {
                string[] parts = line.Split('|');
                if (parts.Length < 2) continue;
                string name = parts[0];
                string path = parts[1];
                string exe = parts.Length > 2 ? parts[2] : null;
                if (!string.IsNullOrEmpty(exe) && !File.Exists(exe)) exe = null;
                if (!Directory.Exists(path)) continue;
                add(name, path, exe, "手动");
            }

            return found.OrderBy(g => g.Drive).ThenBy(g => g.Name, StringComparer.CurrentCulture).ToList();
        }

        public static List<string> SteamRoots()
        {
            var roots = new List<string>();
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    if (k != null)
                    {
                        string steam = Convert.ToString(k.GetValue("SteamPath"));
                        if (!string.IsNullOrEmpty(steam))
                        {
                            steam = steam.Replace('/', '\\');
                            string vdf = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
                            if (File.Exists(vdf))
                            {
                                string text = File.ReadAllText(vdf);
                                foreach (Match m in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\""))
                                {
                                    string p = m.Groups[1].Value.Replace(@"\\", @"\");
                                    roots.Add(Path.Combine(p, @"steamapps\common"));
                                }
                            }
                            roots.Add(Path.Combine(steam, @"steamapps\common"));
                        }
                    }
                }
            }
            catch { }

            foreach (string letter in new[] { "C", "D", "E", "F", "G", "H" })
            {
                if (!Directory.Exists(letter + @":\")) continue;
                foreach (string sub in new[] { @"SteamLibrary\steamapps\common", @"steam\steamapps\common",
                                               @"Steam\steamapps\common", "Epic Games", "GOG Games" })
                {
                    string p = Path.Combine(letter + @":\", sub);
                    if (Directory.Exists(p)) roots.Add(p);
                }
            }
            return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>从游戏目录推断 Steam AppID（读 appmanifest 的 installdir）</summary>
        public static string SteamAppId(string gamePath)
        {
            try
            {
                var dir = new DirectoryInfo(gamePath);
                string leaf = dir.Name;
                var cur = dir;
                for (int i = 0; i < 8 && cur != null; i++)
                {
                    if (!string.IsNullOrEmpty(cur.Name) && cur.Name.Equals("common", StringComparison.OrdinalIgnoreCase)
                        && cur.Parent != null && cur.Parent.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (string acf in Directory.GetFiles(cur.Parent.FullName, "appmanifest_*.acf"))
                        {
                            string txt = File.ReadAllText(acf);
                            Match m = Regex.Match(txt, "\"installdir\"\\s+\"([^\"]*)\"");
                            if (m.Success && string.Equals(m.Groups[1].Value, leaf, StringComparison.OrdinalIgnoreCase))
                                return Regex.Replace(Path.GetFileNameWithoutExtension(acf), "^appmanifest_", "");
                        }
                        break;
                    }
                    cur = cur.Parent;
                }
            }
            catch { }
            return null;
        }

        public static string FindExe(string gamePath)
        {
            try
            {
                var cands = new List<string>();
                cands.AddRange(Directory.GetFiles(gamePath, "*.exe"));
                foreach (string d in Directory.GetDirectories(gamePath))
                {
                    try { cands.AddRange(Directory.GetFiles(d, "*.exe")); }
                    catch { }
                }
                string best = null;
                long bestLen = -1;
                foreach (string f in cands)
                {
                    string n = Path.GetFileName(f);
                    if (Regex.IsMatch(n, "unins|unitycrashhandler|crashreport|vcredist|dxsetup|dotnet|setup\\.exe|launcher_helper", RegexOptions.IgnoreCase)) continue;
                    long len = 0;
                    try { len = new FileInfo(f).Length; } catch { }
                    if (len > bestLen) { bestLen = len; best = f; }
                }
                return best;
            }
            catch { return null; }
        }

        public static void AddCustom(string name, string path, string exe)
        {
            string line = name + "|" + path + "|" + (exe ?? "");
            var lines = Store.ReadLines(Store.PathOf("custom.txt"));
            foreach (string l in lines)
            {
                string[] p = l.Split('|');
                if (p.Length >= 2 && string.Equals(p[1].TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    return;
            }
            lines.Add(line);
            Store.WriteLines(Store.PathOf("custom.txt"), lines);
        }
    }
}
