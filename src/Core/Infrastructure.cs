using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace CodexBlockLib.Core
{
    /// <summary>本地目录约定：配置、日志、缩略图缓存都在 %APPDATA%\Autodesk\CodexBlockLib 下。</summary>
    public static class AppPaths
    {
        public static string Root
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "CodexBlockLib"); }
        }

        public static string LogFile { get { return Path.Combine(Root, "codex-blocklib.log"); } }
        public static string LibraryFile { get { return Path.Combine(Root, "library.xml"); } }
        public static string ThumbRoot { get { return Path.Combine(Root, "thumbs"); } }
        public static string SelfTestFile { get { return Path.Combine(Root, "selftest.txt"); } }

        public static void Ensure()
        {
            try
            {
                Directory.CreateDirectory(Root);
                Directory.CreateDirectory(ThumbRoot);
            }
            catch
            {
            }
        }
    }

    /// <summary>极简日志，写入固定文件，便于插件在无人值守时排查问题。</summary>
    public static class Log
    {
        private static readonly object Sync = new object();
        private const long MaxBytes = 4L * 1024L * 1024L;

        public static string FilePath { get { return AppPaths.LogFile; } }

        public static void Write(string message) { Line("INFO ", message); }
        public static void Warn(string message) { Line("WARN ", message); }

        public static void Error(string context, Exception ex)
        {
            if (ex == null) { Line("ERROR", context); return; }
            Line("ERROR", context + " :: " + ex.GetType().Name + ": " + ex.Message + Environment.NewLine + ex.StackTrace);
        }

        private static void Line(string level, string message)
        {
            lock (Sync)
            {
                try
                {
                    AppPaths.Ensure();
                    Rotate();
                    using (var stream = new FileStream(AppPaths.LogFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(true)))
                    {
                        writer.WriteLine("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + "] " + level + " " + message);
                    }
                }
                catch
                {
                }
            }
        }

        private static void Rotate()
        {
            try
            {
                var info = new FileInfo(AppPaths.LogFile);
                if (!info.Exists || info.Length <= MaxBytes) return;
                string old = AppPaths.LogFile + ".1";
                if (File.Exists(old)) File.Delete(old);
                File.Move(AppPaths.LogFile, old);
            }
            catch
            {
            }
        }

        public static string Tail(int maxLines)
        {
            try
            {
                if (!File.Exists(AppPaths.LogFile)) return string.Empty;
                var lines = File.ReadAllLines(AppPaths.LogFile, Encoding.UTF8);
                int start = Math.Max(0, lines.Length - Math.Max(1, maxLines));
                var sb = new StringBuilder();
                for (int i = start; i < lines.Length; i++) sb.AppendLine(lines[i]);
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return "(读取日志失败: " + ex.Message + ")";
            }
        }
    }

    /// <summary>图形单位换算：源图纸与当前图纸单位不一致时自动换算插入比例。</summary>
    public static class UnitConvert
    {
        public static double MetersPerUnit(UnitsValue unit)
        {
            switch (unit)
            {
                case UnitsValue.Inches: return 0.0254;
                case UnitsValue.Feet: return 0.3048;
                case UnitsValue.Miles: return 1609.344;
                case UnitsValue.Millimeters: return 0.001;
                case UnitsValue.Centimeters: return 0.01;
                case UnitsValue.Meters: return 1.0;
                case UnitsValue.Kilometers: return 1000.0;
                case UnitsValue.MicroInches: return 2.54e-8;
                case UnitsValue.Mils: return 2.54e-5;
                case UnitsValue.Yards: return 0.9144;
                case UnitsValue.Angstroms: return 1e-10;
                case UnitsValue.Nanometers: return 1e-9;
                case UnitsValue.Microns: return 1e-6;
                case UnitsValue.Decimeters: return 0.1;
                case UnitsValue.Dekameters: return 10.0;
                case UnitsValue.Hectometers: return 100.0;
                case UnitsValue.Gigameters: return 1e9;
                case UnitsValue.Astronomical: return 1.495978707e11;
                case UnitsValue.LightYears: return 9.4607304725808e15;
                case UnitsValue.Parsecs: return 3.0856775814913673e16;
                case UnitsValue.USSurveyFeet: return 1200.0 / 3937.0;
                case UnitsValue.USSurveyInch: return 100.0 / 3937.0;
                case UnitsValue.USSurveyYard: return 3600.0 / 3937.0;
                case UnitsValue.USSurveyMile: return 6336000.0 / 3937.0;
                default: return 0.0;
            }
        }

        /// <summary>源图纸 1 个单位换算成目标图纸单位数。</summary>
        public static double ScaleFactor(Database source, Database target)
        {
            try
            {
                double metersFrom = MetersPerUnit(source.Insunits);
                double metersTo = MetersPerUnit(target.Insunits);
                if (metersFrom <= 0.0 || metersTo <= 0.0) return 1.0;
                return metersFrom / metersTo;
            }
            catch
            {
                return 1.0;
            }
        }

        public static string UnitName(UnitsValue unit)
        {
            switch (unit)
            {
                case UnitsValue.Inches: return "英寸";
                case UnitsValue.Feet: return "英尺";
                case UnitsValue.Miles: return "英里";
                case UnitsValue.Millimeters: return "毫米";
                case UnitsValue.Centimeters: return "厘米";
                case UnitsValue.Meters: return "米";
                case UnitsValue.Kilometers: return "公里";
                case UnitsValue.MicroInches: return "微英寸";
                case UnitsValue.Mils: return "密尔";
                case UnitsValue.Yards: return "码";
                case UnitsValue.Angstroms: return "埃";
                case UnitsValue.Nanometers: return "纳米";
                case UnitsValue.Microns: return "微米";
                case UnitsValue.Decimeters: return "分米";
                case UnitsValue.Dekameters: return "十米";
                case UnitsValue.Hectometers: return "百米";
                case UnitsValue.Gigameters: return "吉米";
                case UnitsValue.Astronomical: return "天文单位";
                case UnitsValue.LightYears: return "光年";
                case UnitsValue.Parsecs: return "秒差距";
                case UnitsValue.USSurveyFeet: return "美制测量英尺";
                case UnitsValue.USSurveyInch: return "美制测量英寸";
                case UnitsValue.USSurveyYard: return "美制测量码";
                case UnitsValue.USSurveyMile: return "美制测量英里";
                default: return "无单位";
            }
        }
    }

    public static class TextUtil
    {
        private static readonly char[] Separators = new char[] { '-', '_', ' ', '.', '/', '|', ':', '#', '@', '+', '(' };

        /// <summary>取块名前缀用于自动分类（例如 WD-DOOR-A 得到 WD）。</summary>
        public static string Prefix(string name)
        {
            if (string.IsNullOrEmpty(name)) return "未命名";
            if (name.StartsWith("*", StringComparison.Ordinal)) return "匿名块";
            int cut = -1;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (Array.IndexOf(Separators, c) >= 0) { cut = i; break; }
                if (char.IsDigit(c)) { cut = i; break; }
            }
            string prefix = cut > 0 ? name.Substring(0, cut) : name;
            prefix = prefix.Trim();
            if (prefix.Length == 0) return "其他";
            if (prefix.Length > 16) prefix = prefix.Substring(0, 16);
            return prefix;
        }

        public static string SanitizeFileName(string value)
        {
            if (string.IsNullOrEmpty(value)) return "_";
            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
                else sb.Append('_');
            }
            string s = sb.ToString();
            if (s.Length > 48) s = s.Substring(0, 48);
            return s.Length == 0 ? "_" : s;
        }

        public static string FormatNumber(double value)
        {
            if (Math.Abs(value - Math.Round(value)) < 1e-9) return Math.Round(value).ToString(CultureInfo.InvariantCulture);
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        public static string FormatValue(object value)
        {
            if (value == null) return string.Empty;
            if (value is string) return (string)value;
            if (value is bool) return ((bool)value) ? "是" : "否";
            if (value is double) return FormatNumber((double)value);
            if (value is float) return FormatNumber((float)value);
            if (value is short || value is int || value is long) return Convert.ToString(value, CultureInfo.InvariantCulture);
            return value.ToString();
        }
    }

    /// <summary>按凸度(bulge)展开圆弧，用于缩略图预览。</summary>
    public static class BulgeUtil
    {
        public static void AppendArc(List<Point2d> pts, Point2d start, Point2d end, double bulge)
        {
            if (Math.Abs(bulge) < 1e-12) { pts.Add(end); return; }
            double sweep = 4.0 * Math.Atan(bulge);
            double chord = start.GetDistanceTo(end);
            if (chord < 1e-12) { pts.Add(end); return; }
            double radius = chord / (2.0 * Math.Sin(Math.Abs(sweep) / 2.0));
            double angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
            double mid = angle + (Math.PI / 2.0) * Math.Sign(bulge);
            double h = radius * Math.Cos(sweep / 2.0);
            var center = new Point2d(start.X + Math.Cos(mid) * h, start.Y + Math.Sin(mid) * h);
            double a0 = Math.Atan2(start.Y - center.Y, start.X - center.X);
            int steps = 10;
            for (int i = 1; i <= steps; i++)
            {
                double t = a0 + sweep * ((double)i / steps);
                pts.Add(new Point2d(center.X + Math.Cos(t) * radius, center.Y + Math.Sin(t) * radius));
            }
        }
    }
}
