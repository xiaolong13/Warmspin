using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Warmspin
{
    /// <summary>一套配色。切换主题时把这些画刷换进窗口资源，界面用 DynamicResource 引用，自动刷新。</summary>
    public class Theme
    {
        public string Key;
        public string Name;
        public string Hint;
        public Color Accent;
        public Color Accent2;
        public Color Bg;
        public Color Surface;
        public Color SurfaceAlt;
        public Color Border;
        public Color Text;
        public Color TextDim;
        public Color TextMute;
        public Color Ok;
        public Color Warn;
        public Color Bad;
        public bool Dark;
    }

    public static class Themes
    {
        static Color C(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }

        public static readonly Theme Red = new Theme
        {
            Key = "red",
            Name = "赤红",
            Hint = "浅色",
            Accent = C("#E4393C"),
            Accent2 = C("#FF6B6B"),
            Bg = C("#FFFFFF"),
            Surface = C("#FFFFFF"),
            SurfaceAlt = C("#F7F8FA"),
            Border = C("#E9EBEF"),
            Text = C("#16181D"),
            TextDim = C("#5B6472"),
            TextMute = C("#98A1B0"),
            Ok = C("#12B76A"),
            Warn = C("#F79009"),
            Bad = C("#F04438"),
            Dark = false
        };

        public static readonly Theme Blue = new Theme
        {
            Key = "blue",
            Name = "靛蓝",
            Hint = "冷静",
            Accent = C("#2563EB"),
            Accent2 = C("#60A5FA"),
            Bg = C("#FFFFFF"),
            Surface = C("#FFFFFF"),
            SurfaceAlt = C("#F7F8FA"),
            Border = C("#E9EBEF"),
            Text = C("#16181D"),
            TextDim = C("#5B6472"),
            TextMute = C("#98A1B0"),
            Ok = C("#12B76A"),
            Warn = C("#F79009"),
            Bad = C("#F04438"),
            Dark = false
        };

        public static readonly Theme Green = new Theme
        {
            Key = "green",
            Name = "青竹",
            Hint = "清爽",
            Accent = C("#0E9F6E"),
            Accent2 = C("#34D399"),
            Bg = C("#FFFFFF"),
            Surface = C("#FFFFFF"),
            SurfaceAlt = C("#F7F8FA"),
            Border = C("#E9EBEF"),
            Text = C("#16181D"),
            TextDim = C("#5B6472"),
            TextMute = C("#98A1B0"),
            Ok = C("#12B76A"),
            Warn = C("#F79009"),
            Bad = C("#F04438"),
            Dark = false
        };

        public static readonly Theme Night = new Theme
        {
            Key = "redblack",
            Name = "红黑",
            Hint = "默认",
            Accent = C("#FF5A5F"),
            Accent2 = C("#FF8A8F"),
            Bg = C("#0E1014"),
            Surface = C("#161A21"),
            SurfaceAlt = C("#10141A"),
            Border = C("#252C37"),
            Text = C("#ECEEF3"),
            TextDim = C("#9AA3B2"),
            TextMute = C("#5C6675"),
            Ok = C("#34D399"),
            Warn = C("#FBBF24"),
            Bad = C("#FF5C6C"),
            Dark = true
        };

        public static List<Theme> All()
        {
            return new List<Theme> { Night, Red, Blue, Green };
        }

        public static Theme ByKey(string key)
        {
            foreach (var t in All())
                if (string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase)) return t;
            return Night;
        }

        static SolidColorBrush B(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        /// <summary>把主题写进窗口资源。XAML 里全部用 DynamicResource，所以会立即生效。</summary>
        public static void Apply(Window w, Theme t)
        {
            if (w == null || t == null) return;

            var grad = new LinearGradientBrush(t.Accent2, t.Accent, 45.0);
            grad.Freeze();

            w.Resources["B.Accent"] = B(t.Accent);
            w.Resources["B.Accent2"] = B(t.Accent2);
            w.Resources["B.AccentGrad"] = grad;
            w.Resources["B.Bg"] = B(t.Bg);
            w.Resources["B.Surface"] = B(t.Surface);
            w.Resources["B.SurfaceAlt"] = B(t.SurfaceAlt);
            w.Resources["B.Border"] = B(t.Border);
            w.Resources["B.Text"] = B(t.Text);
            w.Resources["B.TextDim"] = B(t.TextDim);
            w.Resources["B.TextMute"] = B(t.TextMute);
            w.Resources["B.Ok"] = B(t.Ok);
            w.Resources["B.Warn"] = B(t.Warn);
            w.Resources["B.Bad"] = B(t.Bad);

            // 右上角那个"主题"按钮的小圆点跟着变
            var dot = w.FindName("ThemeDot") as System.Windows.Controls.Border;
            if (dot != null) dot.Background = B(t.Accent);

            var shadow = w.FindName("Root") as System.Windows.Controls.Border;
            if (shadow != null && shadow.Effect is System.Windows.Media.Effects.DropShadowEffect)
            {
                var e = (System.Windows.Media.Effects.DropShadowEffect)shadow.Effect;
                e.Opacity = t.Dark ? 0.55 : 0.14;
            }
        }
    }
}
