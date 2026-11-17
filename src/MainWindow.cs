using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Warmspin
{
    public class MainWindow
    {
        readonly Window _w;
        readonly Dictionary<string, FrameworkElement> _ui = new Dictionary<string, FrameworkElement>();
        Brush _accent, _accent2, _ok, _warn, _bad, _text, _dim, _mute, _surface, _surfaceAlt;
        List<GameEntry> _games = new List<GameEntry>();
        List<DiskInfo> _hdd = new List<DiskInfo>();
        int _diskSel = 0;
        string[] _gameDirs = new string[0];
        readonly List<string> _log = new List<string>();
        Theme _theme = Themes.Night;

        public MainWindow(Window w)
        {
            _w = w;
            CacheElements();

            // 必须先应用主题再缓存画刷，否则代码创建的控件会拿着默认（浅色）画刷，
            // 切到深色主题后出现"深底深字"看不见的问题
            string themeArg = null;
            foreach (string a in Environment.GetCommandLineArgs())
                if (a.StartsWith("--theme=", StringComparison.OrdinalIgnoreCase)) themeArg = a.Substring(8);
            _theme = Themes.ByKey(themeArg ?? Settings.Get("theme", "redblack"));
            Themes.Apply(_w, _theme);
            CacheBrushes();

            WireEvents();
            LoadSettingsToUi();

            string startPage = "Overview";
            foreach (string a in Environment.GetCommandLineArgs())
                if (a.StartsWith("--page=", StringComparison.OrdinalIgnoreCase))
                    startPage = a.Substring(7);
            ShowPage(startPage);

            _w.Loaded += delegate
            {
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
                    new Action(delegate
                    {
                        try { InitialLoad(); }
                        catch (Exception ex) { Log("初始化出错：" + ex.Message); }
                    }));
            };
        }

        // ============================================================
        //  初始化
        // ============================================================
        void CacheElements()
        {
            string[] names = new string[]
            {
                "TitleBar", "BtnTheme", "ThemeDot", "BtnMin", "BtnClose", "ThemePopup",
                "BtnThemeRed", "BtnThemeBlue", "BtnThemeGreen", "BtnThemeNight",
                "TabOverview", "TabWarm", "TabTune", "TabAbout",
                "PageOverview", "PageWarm", "PageTune", "PageAbout",
                "HeroKicker", "DiskName", "DiskSpec", "DiskBarFill", "DiskBarText",
                "CpuName", "CpuSpec", "GpuName", "GpuSpec", "MemName", "MemSpec",
                "PageSub", "StatusPanel", "BtnRescan", "OvHint", "BtnGoWarm", "BtnGoTune",
                "GamesPanel", "Seg1", "Seg2", "Seg3", "Seg4", "BudgetHint", "BtnAddGame", "DiskPanel", "DiskCountText",
                "WarmStatus", "WarmPct", "WarmBar",
                "TunePanel", "LogText", "TuneStatus", "BtnUndo", "BtnRun",
                "AboutVersion", "AboutAuthor", "UpdateStatus", "BtnCheckUpdate",
                "DeskHint", "BtnDesktop", "BtnUndo2"
            };
            foreach (string n in names)
            {
                var el = _w.FindName(n) as FrameworkElement;
                if (el != null) _ui[n] = el;
            }
        }

        void CacheBrushes()
        {
            _accent = GetBrush("B.Accent");
            _accent2 = GetBrush("B.Accent2");
            _ok = GetBrush("B.Ok");
            _warn = GetBrush("B.Warn");
            _bad = GetBrush("B.Bad");
            _text = GetBrush("B.Text");
            _dim = GetBrush("B.TextDim");
            _mute = GetBrush("B.TextMute");
            _surface = GetBrush("B.Surface");
            _surfaceAlt = GetBrush("B.SurfaceAlt");
        }

        Brush GetBrush(string key)
        {
            var b = _w.Resources[key] as Brush;
            return b ?? Brushes.Gray;
        }

        FrameworkElement El(string name)
        {
            FrameworkElement el;
            _ui.TryGetValue(name, out el);
            return el;
        }

        TextBlock Text(string name)
        {
            return El(name) as TextBlock;
        }

        void Text(string name, string value)
        {
            var t = El(name) as TextBlock;
            if (t != null) t.Text = value;
        }

        void WireEvents()
        {
            var titleBar = El("TitleBar") as Grid;
            if (titleBar != null)
                titleBar.MouseLeftButtonDown += delegate { try { _w.DragMove(); } catch { } };

            ((Button)El("BtnMin")).Click += delegate { _w.WindowState = WindowState.Minimized; };
            ((Button)El("BtnClose")).Click += delegate { _w.Close(); };
            ((Button)El("BtnTheme")).Click += delegate
            {
                var popup = El("ThemePopup") as System.Windows.Controls.Primitives.Popup;
                if (popup != null) popup.IsOpen = !popup.IsOpen;
            };

            foreach (string key in new[] { "BtnThemeRed", "BtnThemeBlue", "BtnThemeGreen", "BtnThemeNight" })
            {
                var b = El(key) as Button;
                if (b == null) continue;
                b.Click += delegate(object sender, RoutedEventArgs e)
                {
                    var btn = sender as Button;
                    if (btn != null) Safe(delegate { SwitchTheme(Convert.ToString(btn.Tag)); });
                };
            }

            ((RadioButton)El("TabOverview")).Checked += delegate { ShowPage("Overview"); };
            ((RadioButton)El("TabWarm")).Checked += delegate { ShowPage("Warm"); };
            ((RadioButton)El("TabTune")).Checked += delegate { ShowPage("Tune"); };
            ((RadioButton)El("TabAbout")).Checked += delegate { ShowPage("About"); };

            ((Button)El("BtnGoWarm")).Click += delegate { ((RadioButton)El("TabWarm")).IsChecked = true; };
            ((Button)El("BtnGoTune")).Click += delegate { ((RadioButton)El("TabTune")).IsChecked = true; };
            ((Button)El("BtnRescan")).Click += delegate { Safe(RefreshOverview); };

            foreach (string s in new[] { "Seg1", "Seg2", "Seg3", "Seg4" })
                ((RadioButton)El(s)).Checked += delegate { UpdateBudgetHint(); };

            ((Button)El("BtnAddGame")).Click += delegate { Safe(AddGame); };
            ((Button)El("BtnRun")).Click += delegate { Safe(RunTune); };
            ((Button)El("BtnUndo")).Click += delegate { Safe(RunUndo); };
            ((Button)El("BtnUndo2")).Click += delegate { Safe(RunUndo); };
            ((Button)El("BtnDesktop")).Click += delegate { Safe(InstallDesktop); };
            ((Button)El("BtnCheckUpdate")).Click += delegate { CheckUpdate(true); };
        }

        void LoadSettingsToUi()
        {
            int budget = 2048;
            int.TryParse(Settings.Get("budget", "2048"), out budget);
            if (budget == 1024) ((RadioButton)El("Seg1")).IsChecked = true;
            else if (budget == 3072) ((RadioButton)El("Seg3")).IsChecked = true;
            else if (budget == 4096) ((RadioButton)El("Seg4")).IsChecked = true;
            else ((RadioButton)El("Seg2")).IsChecked = true;
            UpdateBudgetHint();
        }

        void InitialLoad()
        {
            Text("AboutVersion", "版本 " + Updater.CurrentVersion);
            Text("AboutAuthor", "作者 " + AppInfo.Author);
            Updater.CleanupOld();

            Log("正在检测机械硬盘…");
            Safe(BuildDiskChips);
            Log("正在扫描游戏…");
            Safe(RefreshGames);
            Safe(RefreshOverview);
            Safe(BuildTuneList);
            Log("就绪");

            if (Updater.IsConfigured) CheckUpdate(false);
        }

        void Safe(Action a)
        {
            try { a(); }
            catch (Exception ex)
            {
                Log("出错：" + ex.Message);
                Store.Ensure();
                try
                {
                    File.AppendAllText(Store.PathOf("crash.log"),
                        "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + ex + "\r\n", System.Text.Encoding.UTF8);
                }
                catch { }
            }
        }

        void ShowPage(string name)
        {
            string[][] map = new string[][]
            {
                new[] { "Overview", "TabOverview" }, new[] { "Warm", "TabWarm" },
                new[] { "Tune", "TabTune" }, new[] { "About", "TabAbout" }
            };
            foreach (string[] kv in map)
            {
                bool hit = string.Equals(kv[0], name, StringComparison.OrdinalIgnoreCase);
                Program.Log("ShowPage " + name + " -> " + kv[0] + " hit=" + hit + " el=" + (El("Page" + kv[0]) != null));
                var page = El("Page" + kv[0]) as FrameworkElement;
                if (page != null) page.Visibility = hit ? Visibility.Visible : Visibility.Collapsed;
                if (hit)
                {
                    var tab = El(kv[1]) as RadioButton;
                    if (tab != null && tab.IsChecked != true) tab.IsChecked = true;
                }
            }
        }

        // ============================================================
        //  主题
        // ============================================================
        void SwitchTheme(string key)
        {
            _theme = Themes.ByKey(key);
            Themes.Apply(_w, _theme);
            CacheBrushes();
            Settings.Set("theme", _theme.Key);

            var popup = El("ThemePopup") as System.Windows.Controls.Primitives.Popup;
            if (popup != null) popup.IsOpen = false;

            // 代码里创建的控件持有旧画刷，重建一遍
            Safe(RefreshOverview);
            Safe(RefreshGames);
            Safe(BuildTuneList);
            Log("主题已切换：" + _theme.Name);
        }

        // ============================================================
        //  通用渲染
        // ============================================================
        void Pump()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
                new DispatcherOperationCallback(delegate(object f) { ((DispatcherFrame)f).Continue = false; return null; }), frame);
            Dispatcher.PushFrame(frame);
        }

        void Log(string s)
        {
            _log.Add("· " + s);
            if (_log.Count > 500) _log.RemoveAt(0);
            var t = Text("LogText");
            if (t != null) t.Text = string.Join("\r\n", _log);
            Pump();
        }

        TextBlock MakeText(string text, double size, Brush brush, bool bold)
        {
            var t = new TextBlock();
            t.Text = text;
            t.FontSize = size;
            t.Foreground = brush;
            if (bold) t.FontWeight = FontWeights.SemiBold;
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            return t;
        }

        static ScrollViewer FindParentScroll(DependencyObject d)
        {
            while (d != null)
            {
                var sv = d as ScrollViewer;
                if (sv != null) return sv;
                d = System.Windows.Media.VisualTreeHelper.GetParent(d);
            }
            return null;
        }

        void AddStatusRow(CheckItem item)
        {
            Brush dot = _mute;
            if (item.Level == "Ok") dot = _ok;
            else if (item.Level == "Warn") dot = _warn;
            else if (item.Level == "Bad") dot = _bad;

            var row = new Border();
            row.CornerRadius = new CornerRadius(10);
            row.Padding = new Thickness(10, 9, 10, 9);
            row.Background = Brushes.Transparent;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var ell = new System.Windows.Shapes.Ellipse();
            ell.Width = 8;
            ell.Height = 8;
            ell.Fill = dot;
            ell.VerticalAlignment = VerticalAlignment.Center;
            ell.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetColumn(ell, 0);
            grid.Children.Add(ell);

            var sp = new StackPanel();
            sp.Margin = new Thickness(2, 0, 0, 0);
            Grid.SetColumn(sp, 1);
            sp.Children.Add(MakeText(item.Name, 12.5, _text, false));
            var d = MakeText(item.Detail, 11, _mute, false);
            d.TextWrapping = TextWrapping.Wrap;
            d.Margin = new Thickness(0, 3, 0, 0);
            sp.Children.Add(d);
            grid.Children.Add(sp);

            row.Child = grid;
            ((StackPanel)El("StatusPanel")).Children.Add(row);
        }

        // ============================================================
        //  概览
        // ============================================================
        void RefreshOverview()
        {
            var mem = Sys.Memory();
            Text("CpuName", Sys.CpuName());
            Text("CpuSpec", Sys.CpuSpec());

            string gpuSpec;
            Text("GpuName", Sys.GpuName(out gpuSpec));
            Text("GpuSpec", gpuSpec);

            Text("MemName", mem.TotalGB + " GB");
            Text("MemSpec", "可用 " + mem.AvailGB + " GB · 占用 " + mem.LoadPercent + "%");

            var disks = Sys.Disks();
            var hdd = disks.FirstOrDefault(d => d.Media == "机械");
            if (hdd != null)
            {
                Text("HeroKicker", "游戏盘 · 机械硬盘");
                Text("DiskName", hdd.Model);
                Text("DiskSpec", hdd.SizeGB > 0 ? ("容量 " + Math.Round(hdd.SizeGB) + " GB") : "");
            }
            else
            {
                Text("HeroKicker", "游戏盘");
                Text("DiskName", "未检测到机械盘");
                Text("DiskSpec", "游戏可能已经在固态盘上");
            }

            var vols = Sys.Volumes();
            var first = _games.FirstOrDefault();
            VolumeInfo target = null;
            if (first != null) target = vols.FirstOrDefault(v => v.Letter == first.Drive);
            if (target == null) target = vols.FirstOrDefault(v => v.Media == "机械");
            if (target != null)
            {
                double used = 1.0 - (double)target.FreeGB / Math.Max(1.0, (double)target.SizeGB);
                var bar = El("DiskBarFill");
                if (bar != null) bar.Width = Math.Max(6, 420 * used);
                Text("DiskBarText", target.Letter + ":  剩 " + Math.Round(target.FreeGB) + " GB / " +
                                    Math.Round(target.SizeGB) + " GB   ·  可用 " + target.FreePercent + "%");
            }

            var panel = (StackPanel)El("StatusPanel");
            panel.Children.Clear();
            var checks = Sys.Checks();
            int bad = 0, warn = 0;
            foreach (var c in checks)
            {
                AddStatusRow(c);
                if (c.Level == "Bad") bad++;
                if (c.Level == "Warn") warn++;
            }

            var sv = FindParentScroll(panel);
            if (sv != null) sv.ScrollToTop();

            if (bad == 0 && warn == 0)
            {
                Text("PageSub", "系统设置没有明显拖后腿的地方");
                Text("OvHint", "直接去预热启动游戏吧。");
            }
            else
            {
                Text("PageSub", "发现 " + bad + " 项问题、" + warn + " 项可优化");
                Text("OvHint", "先做一次系统优化，再预热启动游戏。");
            }
        }

        // ============================================================
        //  预热
        // ============================================================
        void BuildDiskChips()
        {
            var panel = (StackPanel)El("DiskPanel");
            if (panel == null) return;
            panel.Children.Clear();

            _hdd = Sys.Disks().Where(d => d.Media == "机械").ToList();
            if (_diskSel >= _hdd.Count) _diskSel = 0;
            if (_hdd.Count == 0)
            {
                var none = MakeText("未检测到机械盘", 12, _mute, false);
                none.VerticalAlignment = VerticalAlignment.Center;
                panel.Children.Add(none);
                Text("DiskCountText", "");
                return;
            }

            for (int i = 0; i < _hdd.Count; i++)
            {
                var d = _hdd[i];
                int captured = i;
                var b = new Button();
                b.Style = _w.Resources[i == _diskSel ? "BtnPrimary" : "BtnGhost"] as Style;
                b.Padding = new Thickness(14, 7, 14, 7);
                b.FontSize = 11.5;
                b.Content = d.Model + "  ·  " + Math.Round(d.SizeGB) + " GB";
                b.Margin = new Thickness(0, 0, 8, 0);
                b.Cursor = System.Windows.Input.Cursors.Hand;
                b.Click += delegate
                {
                    _diskSel = captured;
                    Safe(BuildDiskChips);
                    Safe(RefreshGames);
                    Safe(RefreshOverview);
                };
                panel.Children.Add(b);
            }
            Text("DiskCountText", _hdd.Count > 1 ? ("检测到 " + _hdd.Count + " 块机械盘，点上面切换") : "");
        }

        /// <summary>当前选中机械盘上的所有分区盘符</summary>
        HashSet<char> SelectedDiskLetters()
        {
            var set = new HashSet<char>();
            if (_hdd.Count == 0) return set;
            int idx = _hdd[_diskSel].Index;
            foreach (var v in Sys.Volumes())
                if (v.DiskIndex == idx) set.Add(v.Letter);
            return set;
        }

        void RefreshGames()
        {
            var panel = (StackPanel)El("GamesPanel");
            panel.Children.Clear();

            var letters = SelectedDiskLetters();
            _games = new List<GameEntry>();
            var dirs = new List<string>();
            foreach (var g in GameFinder.Find())
            {
                if (letters.Count > 0)
                {
                    if (!letters.Contains(g.Drive)) continue;
                }
                else if (g.Drive == 'C') continue;
                _games.Add(g);
                dirs.Add(g.Path);
            }
            _gameDirs = dirs.ToArray();

            foreach (var g in _games) AddGameRow(g);

            if (_games.Count == 0)
            {
                var t = MakeText("这块机械盘上还没找到游戏。点右上角「+ 添加游戏」手动指定目录，或换一块盘看看。", 12.5, _mute, false);
                t.Margin = new Thickness(18, 26, 18, 0);
                t.TextWrapping = TextWrapping.Wrap;
                panel.Children.Add(t);
            }
        }

        void AddGameRow(GameEntry g)
        {
            var row = new Border();
            row.CornerRadius = new CornerRadius(12);
            row.Padding = new Thickness(0, 12, 16, 12);
            row.Margin = new Thickness(6, 3, 6, 3);
            row.Background = Brushes.Transparent;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 左侧强调色竖条
            var bar = new Border();
            bar.Width = 4;
            bar.CornerRadius = new CornerRadius(2);
            bar.Background = _accent;
            bar.VerticalAlignment = VerticalAlignment.Stretch;
            bar.Margin = new Thickness(0, 2, 0, 2);
            bar.Opacity = 0.85;
            Grid.SetColumn(bar, 0);
            grid.Children.Add(bar);

            var sp = new StackPanel();
            Grid.SetColumn(sp, 2);
            sp.Children.Add(MakeText(g.Name, 13.5, _text, true));

            string media = "机械";
            var vol = Sys.Volumes().FirstOrDefault(v => v.Letter == g.Drive);
            if (vol != null) media = vol.Media;
            string appId = GameFinder.SteamAppId(g.Path);
            string tag = !string.IsNullOrEmpty(appId) ? ("Steam " + appId) : g.Source;
            var m = MakeText(g.Drive + ":  ·  " + media + "  ·  " + tag, 11, _mute, false);
            m.Margin = new Thickness(0, 4, 0, 0);
            sp.Children.Add(m);
            grid.Children.Add(sp);

            var btn = new Button();
            btn.Content = "预热启动";
            btn.Style = _w.Resources["BtnPrimary"] as Style;
            btn.Padding = new Thickness(18, 8, 18, 8);
            btn.FontSize = 12.5;
            btn.Tag = g;
            btn.VerticalAlignment = VerticalAlignment.Center;
            btn.Cursor = System.Windows.Input.Cursors.Hand;
            Grid.SetColumn(btn, 3);
            btn.Click += delegate(object sender, RoutedEventArgs e)
            {
                var b = sender as Button;
                var game = b != null ? b.Tag as GameEntry : null;
                if (game != null) Safe(delegate { StartWarm(game); });
            };
            grid.Children.Add(btn);

            row.Child = grid;
            row.MouseEnter += delegate { row.Background = _surfaceAlt; };
            row.MouseLeave += delegate { row.Background = Brushes.Transparent; };

            ((StackPanel)El("GamesPanel")).Children.Add(row);
        }

        int BudgetMB()
        {
            if (((RadioButton)El("Seg1")).IsChecked == true) return 1024;
            if (((RadioButton)El("Seg3")).IsChecked == true) return 3072;
            if (((RadioButton)El("Seg4")).IsChecked == true) return 4096;
            return 2048;
        }

        void UpdateBudgetHint()
        {
            Text("BudgetHint", "约 " + Math.Round(BudgetMB() / 95.0) + " 秒");
            Settings.Set("budget", BudgetMB().ToString());
        }

        void StartWarm(GameEntry g)
        {
            var bar = (ProgressBar)El("WarmBar");
            bar.Value = 0;
            Text("WarmPct", "");
            Text("WarmStatus", "正在扫描 " + g.Name + " …");
            Pump();

            var result = Warmer.Run(g.Path, BudgetMB(), delegate(long done, long sel, double spd)
            {
                if (sel > 0) bar.Value = Math.Min(100.0, done * 100.0 / sel);
                Text("WarmPct", (done / 1048576) + " / " + (sel / 1048576) + " MB   " + ((int)spd) + " MB/s");
                Text("WarmStatus", "正在读入内存：" + g.Name);
                Pump();
            });

            bar.Value = 100;
            Text("WarmPct", "");

            if (!string.IsNullOrEmpty(result.Error))
            {
                Text("WarmStatus", "预热失败：" + result.Error);
                return;
            }
            Text("WarmStatus", "已读入 " + Fmt.Size(result.BytesRead) + "（" +
                               Fmt.Duration(result.Seconds) + "），" + Launcher.Launch(g));
        }

        void AddGame()
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog();
            dlg.Description = "选择游戏安装目录（含主程序或启动器的那个文件夹）";
            dlg.ShowNewFolderButton = false;
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            string path = dlg.SelectedPath;
            string name = new DirectoryInfo(path).Name;
            GameFinder.AddCustom(name, path, GameFinder.FindExe(path));
            RefreshGames();
            Text("WarmStatus", "已添加：" + name);
        }

        // ============================================================
        //  优化
        // ============================================================
        void BuildTuneList()
        {
            var panel = (StackPanel)El("TunePanel");
            panel.Children.Clear();
            AddTune("sysmain", "启用 SysMain 预读取服务", "机械盘上系统会学习读文件顺序并提前预取", true);
            AddTune("prefetch", "开启 Prefetch 预读", "让系统记录启动时读了哪些文件", true);
            AddTune("defender", "Defender 排除游戏目录", "几万个小文件逐个过扫描，开销很可观", true);
            AddTune("index", "关闭游戏目录内容索引", "游戏文件不需要被搜索索引", true);
            AddTune("disksleep", "关闭硬盘自动休眠", "盘停转后读图要等它唤醒", true);
            AddTune("defrag", "碎片整理（耗时长，建议睡前跑）", "文件被拆散时，读一个文件要来回寻道", false);
        }

        void AddTune(string key, string title, string note, bool isChecked)
        {
            var cb = new CheckBox();
            cb.Style = _w.Resources["Chk"] as Style;
            cb.IsChecked = isChecked;
            cb.Tag = key;
            cb.Margin = new Thickness(0, 0, 0, 2);

            var sp = new StackPanel();
            sp.Children.Add(MakeText(title, 12.5, _text, false));
            var n = MakeText(note, 11, _mute, false);
            n.Margin = new Thickness(0, 3, 0, 0);
            n.TextWrapping = TextWrapping.Wrap;
            sp.Children.Add(n);
            cb.Content = sp;

            ((StackPanel)El("TunePanel")).Children.Add(cb);
        }

        List<string> CheckedKeys()
        {
            var keys = new List<string>();
            foreach (var child in ((StackPanel)El("TunePanel")).Children)
            {
                var cb = child as CheckBox;
                if (cb != null && cb.IsChecked == true) keys.Add(Convert.ToString(cb.Tag));
            }
            return keys;
        }

        void RunTune()
        {
            var keys = CheckedKeys();
            if (keys.Count == 0) { Log("没有勾选任何项目"); return; }

            var btnRun = (Button)El("BtnRun");
            var btnUndo = (Button)El("BtnUndo");
            btnRun.IsEnabled = false;
            btnUndo.IsEnabled = false;

            _log.Clear();
            Text("LogText", "");
            Log("开始优化，共 " + keys.Count + " 项");

            var state = ChangeState.Load();
            var dirs = _gameDirs.ToList();

            if (keys.Contains("sysmain")) { Log(Optimizer.EnableSysMain(state)); Pump(); }
            if (keys.Contains("prefetch")) { Log(Optimizer.EnsurePrefetch(state)); Pump(); }
            if (keys.Contains("defender")) { Log(Optimizer.ExcludeDefender(dirs, state)); Pump(); }
            if (keys.Contains("index"))
            {
                Log(Optimizer.DisableIndexing(dirs, state, delegate(int i, int total)
                {
                    Text("TuneStatus", "设置索引属性 " + i + " / " + total);
                    Pump();
                }));
            }
            if (keys.Contains("disksleep")) { Log(Optimizer.DisableDiskSleep(state)); Pump(); }

            state.Save();

            if (keys.Contains("defrag"))
            {
                string drive = dirs.Count > 0 ? char.ToUpper(dirs[0][0]) + ":" : null;
                if (drive == null) Log("找不到机械盘上的游戏，跳过整理");
                else
                {
                    Log("开始整理 " + drive + "，可以随时关闭窗口中止");
                    Log(Optimizer.Defrag(drive, delegate(string s) { Text("TuneStatus", s); Pump(); }));
                }
            }

            Log("全部完成");
            btnRun.IsEnabled = true;
            btnUndo.IsEnabled = true;
            Safe(RefreshOverview);
        }

        void RunUndo()
        {
            var st = ChangeState.Load();
            if (st.IsEmpty) { Log("没有找到优化记录"); return; }

            var btnRun = (Button)El("BtnRun");
            var btnUndo = (Button)El("BtnUndo");
            btnRun.IsEnabled = false;
            btnUndo.IsEnabled = false;

            foreach (string l in Optimizer.Undo(st)) Log(l);
            try { if (File.Exists(Store.PathOf("state.txt"))) File.Delete(Store.PathOf("state.txt")); }
            catch { }

            btnRun.IsEnabled = true;
            btnUndo.IsEnabled = true;
            Safe(RefreshOverview);
        }

        // ============================================================
        //  关于 / 更新
        // ============================================================
        void InstallDesktop()
        {
            try
            {
                string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string lnk = Path.Combine(desktop, "Warmspin.lnk");

                dynamic sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                dynamic sc = sh.CreateShortcut(lnk);
                sc.TargetPath = exe;
                sc.WorkingDirectory = Path.GetDirectoryName(exe);
                sc.IconLocation = exe + ",0";
                sc.Description = "Warmspin · 机械盘游戏加速";
                sc.WindowStyle = 1;
                sc.Save();

                Text("DeskHint", "桌面已有 Warmspin 快捷方式");
            }
            catch (Exception ex)
            {
                Text("DeskHint", "生成失败：" + ex.Message);
            }
        }

        void CheckUpdate(bool interactive)
        {
            var btn = (Button)El("BtnCheckUpdate");
            if (btn != null) btn.IsEnabled = false;
            Text("UpdateStatus", "正在检查更新…");

            ThreadPool.QueueUserWorkItem(delegate
            {
                var info = Updater.Check();
                _w.Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (btn != null) btn.IsEnabled = true;
                    OnUpdateChecked(info, interactive);
                }));
            });
        }

        void OnUpdateChecked(UpdateInfo info, bool interactive)
        {
            if (!info.Configured)
            {
                Text("UpdateStatus", "未配置更新源（发布后在 src/Updater.cs 里填写 ManifestUrl）");
                return;
            }
            if (!info.Ok)
            {
                Text("UpdateStatus", info.Error);
                return;
            }
            if (!info.HasUpdate)
            {
                string ver = string.IsNullOrEmpty(info.Version) ? Updater.CurrentVersion : info.Version;
                if (info.Error == "no-manifest")
                    Text("UpdateStatus", "已是最新版本（当前 " + ver + "，作者还没有发布更新清单）");
                else
                    Text("UpdateStatus", "已是最新版本（当前 " + ver + "）");
                return;
            }

            if (interactive)
            {
                var r = MessageBox.Show("发现新版本 " + info.Version + "，现在下载并更新？\r\n\r\n" + info.Notes,
                    "Warmspin", MessageBoxButton.OKCancel, MessageBoxImage.Information);
                if (r != MessageBoxResult.OK) return;
            }

            Text("UpdateStatus", "发现新版本 " + info.Version + "，正在下载…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string file = Updater.Download(info, delegate(int pct, long bytes)
                    {
                        _w.Dispatcher.BeginInvoke(new Action(delegate
                        {
                            Text("UpdateStatus", "正在下载 " + pct + "%  ·  " + Fmt.Size(bytes));
                        }));
                    });

                    _w.Dispatcher.BeginInvoke(new Action(delegate
                    {
                        Text("UpdateStatus", "下载完成，正在重启以应用更新…");
                        try { Updater.ApplyAndRestart(file); }
                        catch (Exception ex) { Text("UpdateStatus", "更新失败：" + ex.Message); }
                    }));
                }
                catch (Exception ex)
                {
                    _w.Dispatcher.BeginInvoke(new Action(delegate
                    {
                        Text("UpdateStatus", "更新失败：" + ex.Message);
                    }));
                }
            });
        }
    }

    /// <summary>程序信息</summary>
    public static class AppInfo
    {
        public const string Name = "Warmspin";
        public const string Author = "Kite";
        public const string Repo = "https://github.com/kite/warmspin";
        public const string Tagline = "HDD Game Accelerator";
    }

    /// <summary>极简设置存取</summary>
    public static class Settings
    {
        static Dictionary<string, string> _cache;

        static Dictionary<string, string> Load()
        {
            if (_cache != null) return _cache;
            _cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Store.ReadPairs(Store.PathOf("settings.txt")))
                _cache[p[0]] = p[1];
            return _cache;
        }

        public static string Get(string key, string fallback)
        {
            string v;
            return Load().TryGetValue(key, out v) ? v : fallback;
        }

        public static void Set(string key, string value)
        {
            var d = Load();
            d[key] = value;
            var lines = new List<string>();
            foreach (var kv in d) lines.Add(kv.Key + "=" + kv.Value);
            Store.WriteLines(Store.PathOf("settings.txt"), lines);
        }
    }
}
