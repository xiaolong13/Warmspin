using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Warmspin
{
    public static class Program
    {
        static string LogPath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HDDGameBooster");
                try
                {
                    Directory.CreateDirectory(dir);
                    string probe = Path.Combine(dir, ".w");
                    File.WriteAllText(probe, "1");
                    File.Delete(probe);
                    return Path.Combine(dir, "win.log");
                }
                catch { }
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "win.log");
            }
        }

        public static void Log(string s)
        {
            try
            {
                string p = LogPath;
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                File.AppendAllText(p, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s + "\r\n", Encoding.UTF8);
            }
            catch { }
        }

        [STAThread]
        public static void Main()
        {
            try
            {
                string p = LogPath;
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                if (File.Exists(p)) File.Delete(p);
            }
            catch { }
            Log("Main start");

            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;

            Window win = null;
            try
            {
                string xaml;
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Ui.xaml"))
                {
                    if (s == null) throw new Exception("找不到内嵌的界面资源");
                    using (var r = new StreamReader(s, Encoding.UTF8))
                        xaml = r.ReadToEnd();
                }
                win = (Window)XamlReader.Parse(xaml);
                Log("XAML ok");
            }
            catch (Exception ex)
            {
                Log("XAML failed: " + ex);
                MessageBox.Show("界面加载失败：\r\n" + ex.Message, "Warmspin");
                return;
            }

            app.DispatcherUnhandledException += delegate(object s, DispatcherUnhandledExceptionEventArgs e)
            {
                Log("unhandled: " + e.Exception);
                MessageBox.Show("出错了：" + e.Exception.Message + "\r\n\r\n详情：" + LogPath, "Warmspin");
                e.Handled = true;
            };

            try
            {
                new MainWindow(win);
                Log("MainWindow built");
            }
            catch (Exception ex)
            {
                Log("MainWindow failed: " + ex);
                MessageBox.Show("启动失败：\r\n" + ex.Message, "Warmspin");
                return;
            }

            try
            {
                app.MainWindow = win;
                // 显式定位：CenterScreen 在部分环境（无边框 + 透明窗口）下不生效，
                // 窗口会停在 (-32000,-32000) 的隐藏状态
                var wa = SystemParameters.WorkArea;
                win.WindowStartupLocation = WindowStartupLocation.Manual;
                win.Width = 1180;
                win.Height = 760;
                win.Left = Math.Max(0, wa.Left + (wa.Width - 1180) / 2);
                win.Top = Math.Max(0, wa.Top + (wa.Height - 760) / 2);
                Log("before Show: Left=" + win.Left + " Top=" + win.Top + " W=" + win.Width + " H=" + win.Height);
                win.Show();
                win.Activate();
                Log("after Show: Actual=" + win.ActualWidth + "x" + win.ActualHeight +
                    " Left=" + win.Left + " Top=" + win.Top +
                    " Visible=" + win.IsVisible + " State=" + win.WindowState);


            }
            catch (Exception ex)
            {
                Log("Show failed: " + ex);
                MessageBox.Show("显示窗口失败：\r\n" + ex.Message, "Warmspin");
                return;
            }

            // --shot=路径：把窗口渲染成 PNG 后退出（截图用，避免外部抓图工具干扰）
            string shotPath = null;
            foreach (string a in Environment.GetCommandLineArgs())
                if (a.StartsWith("--shot=", StringComparison.OrdinalIgnoreCase)) shotPath = a.Substring(7);

            if (!string.IsNullOrEmpty(shotPath))
            {
                var shotTimer = new DispatcherTimer();
                shotTimer.Interval = TimeSpan.FromMilliseconds(1600);
                shotTimer.Tick += delegate(object ts, EventArgs te)
                {
                    ((DispatcherTimer)ts).Stop();
                    try
                    {
                        var rtb = new RenderTargetBitmap(
                            (int)Math.Ceiling(win.ActualWidth), (int)Math.Ceiling(win.ActualHeight),
                            96, 96, PixelFormats.Pbgra32);
                        rtb.Render(win);
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(rtb));
                        using (var fs = File.Create(shotPath)) encoder.Save(fs);
                        Log("shot saved");
                    }
                    catch (Exception ex) { Log("shot failed: " + ex.Message); }
                    Application.Current.Shutdown();
                };
                shotTimer.Start();
            }

            app.Run();            app.Run();
            Log("Main end");
        }
    }
}
