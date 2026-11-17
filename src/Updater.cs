using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Warmspin
{
    public class UpdateInfo
    {
        public bool Ok;
        public bool HasUpdate;
        public string Version = "";
        public string Url = "";
        public string Notes = "";
        public string Sha256 = "";
        public string Error = "";
        public bool Configured;
    }

    /// <summary>
    /// 自动更新：读取远端 update.json，对比版本；下载后"改名旧 exe → 拷入新 exe → 重启"，
    /// 不需要额外的更新器进程，也不受中文路径影响。
    /// </summary>
    public static class Updater
    {
        public const string CurrentVersion = "2.0.0";

        /// <summary>
        /// 更新清单地址。留空 / 保持 example 时视为"未配置更新源"。
        /// 清单格式：{ "version": "2.1.0", "url": "https://.../Warmspin.exe", "sha256": "...", "notes": "..." }
        /// </summary>
        public const string ManifestUrl = "https://raw.githubusercontent.com/kite/warmspin/main/update.json";

        public static bool IsConfigured
        {
            get
            {
                return !string.IsNullOrWhiteSpace(ManifestUrl)
                       && ManifestUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                       && ManifestUrl.IndexOf("github.com/kite/warmspin", StringComparison.OrdinalIgnoreCase) < 0;
            }
        }

        static void EnableTls()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch { }
        }

        public static UpdateInfo Check()
        {
            var info = new UpdateInfo();
            info.Configured = IsConfigured;
            if (!IsConfigured)
            {
                info.Error = "未配置更新源（在 src/Updater.cs 里填写 ManifestUrl）";
                return info;
            }
            EnableTls();
            try
            {
                using (var wc = new WebClient())
                {
                    wc.Encoding = Encoding.UTF8;
                    wc.Headers.Add("User-Agent", "Warmspin/" + CurrentVersion);
                    string json = wc.DownloadString(ManifestUrl);

                    info.Version = Field(json, "version");
                    info.Url = Field(json, "url");
                    info.Sha256 = Field(json, "sha256");
                    info.Notes = Field(json, "notes");

                    if (string.IsNullOrEmpty(info.Version) || string.IsNullOrEmpty(info.Url))
                    {
                        info.Error = "更新清单格式不正确";
                        return info;
                    }
                    info.Ok = true;
                    info.HasUpdate = Compare(info.Version, CurrentVersion) > 0;
                }
            }
            catch (WebException wex)
            {
                var resp = wex.Response as HttpWebResponse;
                if (resp != null && resp.StatusCode == HttpStatusCode.NotFound)
                {
                    // 还没有发布更新清单：对用户而言就是"没有新版本"
                    info.Ok = true;
                    info.HasUpdate = false;
                    info.Version = CurrentVersion;
                    info.Error = "no-manifest";
                    return info;
                }
                info.Error = "检查失败：" + wex.Message;
            }
            catch (Exception ex)
            {
                info.Error = "检查失败：" + ex.Message;
            }
            return info;
        }

        static string Field(string json, string name)
        {
            Match m = Regex.Match(json, "\"" + name + "\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : "";
        }

        static int Compare(string a, string b)
        {
            string[] pa = a.Split('.');
            string[] pb = b.Split('.');
            int n = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < n; i++)
            {
                int va = 0, vb = 0;
                if (i < pa.Length) int.TryParse(Regex.Replace(pa[i], "[^0-9]", ""), out va);
                if (i < pb.Length) int.TryParse(Regex.Replace(pb[i], "[^0-9]", ""), out vb);
                if (va != vb) return va > vb ? 1 : -1;
            }
            return 0;
        }

        /// <summary>下载新版本到临时目录，返回文件路径。</summary>
        public static string Download(UpdateInfo info, Action<int, long> progress)
        {
            EnableTls();
            string dir = Path.Combine(Path.GetTempPath(), "WarmspinUpdate");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "Warmspin-new.exe");
            if (File.Exists(file)) File.Delete(file);

            using (var wc = new WebClient())
            {
                wc.Headers.Add("User-Agent", "Warmspin/" + CurrentVersion);
                if (progress != null)
                {
                    wc.DownloadProgressChanged += delegate(object s, DownloadProgressChangedEventArgs e)
                    {
                        progress(e.ProgressPercentage, e.BytesReceived);
                    };
                }
                wc.DownloadFile(info.Url, file);
            }

            if (!string.IsNullOrEmpty(info.Sha256))
            {
                string actual = Sha256Of(file);
                if (!string.Equals(actual, info.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(file); } catch { }
                    throw new Exception("校验失败：下载的文件与清单哈希不一致");
                }
            }
            return file;
        }

        public static string Sha256Of(string path)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var fs = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(fs);
                var sb = new StringBuilder();
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>替换正在运行的 exe 并重启。旧文件改名为 .old，下次启动时清理。</summary>
        public static void ApplyAndRestart(string newExePath)
        {
            string exe = Assembly.GetExecutingAssembly().Location;
            string bak = exe + ".old";

            try { if (File.Exists(bak)) File.Delete(bak); } catch { }

            File.Move(exe, bak);
            try
            {
                File.Copy(newExePath, exe, true);
            }
            catch
            {
                try { File.Move(bak, exe); } catch { }
                throw;
            }

            try { File.Delete(newExePath); } catch { }
            System.Diagnostics.Process.Start(exe);
            if (System.Windows.Application.Current != null)
                System.Windows.Application.Current.Shutdown();
        }

        /// <summary>启动时清理上次更新留下的 .old</summary>
        public static void CleanupOld()
        {
            try
            {
                string exe = Assembly.GetExecutingAssembly().Location;
                string bak = exe + ".old";
                if (File.Exists(bak)) File.Delete(bak);
            }
            catch { }
        }
    }
}
