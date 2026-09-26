using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Autodesk.AutoCAD.Geometry;

namespace CodexBlockLib.Core
{
    /// <summary>把展开后的二维形状渲染成块缩略图。</summary>
    public static class ThumbnailRenderer
    {
        public static Bitmap Render(ShapeSet shapes, int width, int height)
        {
            if (width <= 0) width = 96;
            if (height <= 0) height = 96;

            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.Clear(Color.White);

                if (shapes == null || !shapes.HasData)
                {
                    DrawCentered(graphics, "无图形", width, height, Color.Gainsboro);
                    return bitmap;
                }

                double minX = shapes.MinX, maxX = shapes.MaxX, minY = shapes.MinY, maxY = shapes.MaxY;
                double spanX = maxX - minX;
                double spanY = maxY - minY;
                const double pad = 5.0;

                if (spanX < 1e-9 && spanY < 1e-9)
                {
                    using (var brush = new SolidBrush(Color.FromArgb(60, 60, 60)))
                    {
                        graphics.FillEllipse(brush, width / 2 - 2, height / 2 - 2, 4, 4);
                    }
                    DrawLabels(graphics, shapes, width, height, 1.0, (minX + maxX) / 2.0, (minY + maxY) / 2.0);
                    return bitmap;
                }

                if (spanX < 1e-9) spanX = Math.Max(spanY * 0.15, 1e-9);
                if (spanY < 1e-9) spanY = Math.Max(spanX * 0.15, 1e-9);

                double scale = Math.Min((width - 2.0 * pad) / spanX, (height - 2.0 * pad) / spanY);
                if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0.0) scale = 1.0;
                double centerX = (minX + maxX) / 2.0;
                double centerY = (minY + maxY) / 2.0;

                float MapX(double x) { return (float)(width / 2.0 + (x - centerX) * scale); }
                float MapY(double y) { return (float)(height / 2.0 - (y - centerY) * scale); }

                using (var geometryPen = new Pen(Color.FromArgb(40, 40, 46), 1.0f))
                using (var framePen = new Pen(Color.FromArgb(158, 158, 168), 1.0f))
                {
                    framePen.DashStyle = DashStyle.Dot;

                    foreach (Point2d[] loop in shapes.Loops)
                    {
                        if (loop.Length < 2) continue;
                        var points = new PointF[loop.Length];
                        for (int i = 0; i < loop.Length; i++) points[i] = new PointF(MapX(loop[i].X), MapY(loop[i].Y));
                        try { graphics.DrawLines(geometryPen, points); }
                        catch { }
                    }

                    foreach (Point2d[] frame in shapes.Frames)
                    {
                        if (frame.Length < 2) continue;
                        var points = new PointF[frame.Length];
                        for (int i = 0; i < frame.Length; i++) points[i] = new PointF(MapX(frame[i].X), MapY(frame[i].Y));
                        try { graphics.DrawLines(framePen, points); }
                        catch { }
                    }
                }

                DrawLabels(graphics, shapes, width, height, scale, centerX, centerY);
            }
            return bitmap;
        }

        private static void DrawLabels(Graphics graphics, ShapeSet shapes, int width, int height, double scale, double centerX, double centerY)
        {
            if (shapes.Labels.Count == 0) return;
            int drawn = 0;
            foreach (ShapeLabel label in shapes.Labels)
            {
                if (drawn++ > 120) break;
                float size = (float)(label.Height * scale * 0.85);
                if (size < 3.5f) continue;
                if (size > 36f) size = 36f;

                float x = (float)(width / 2.0 + (label.X - centerX) * scale);
                float y = (float)(height / 2.0 - (label.Y - centerY) * scale);

                using (Font font = CreateFont(size))
                using (var brush = new SolidBrush(Color.FromArgb(24, 84, 160)))
                {
                    var state = graphics.Save();
                    try
                    {
                        graphics.TranslateTransform(x, y);
                        graphics.RotateTransform((float)(-label.AngleRadians * 180.0 / Math.PI));
                        graphics.DrawString(label.Text, font, brush, 0f, (float)(-label.Height * scale));
                    }
                    catch { }
                    finally { graphics.Restore(state); }
                }
            }
        }

        private static Font CreateFont(float pixelSize)
        {
            try { return new Font("Microsoft YaHei", pixelSize, FontStyle.Regular, GraphicsUnit.Pixel); }
            catch { }
            try { return new Font("SimSun", pixelSize, FontStyle.Regular, GraphicsUnit.Pixel); }
            catch { }
            try { return new Font(FontFamily.GenericSansSerif, pixelSize, FontStyle.Regular, GraphicsUnit.Pixel); }
            catch { }
            return null;
        }

        private static void DrawCentered(Graphics graphics, string text, int width, int height, Color color)
        {
            using (Font font = CreateFont(12f))
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                if (font == null) return;
                graphics.DrawString(text, font, brush, new RectangleF(0, 0, width, height), format);
            }
        }
    }

    /// <summary>缩略图磁盘缓存，按源文件路径哈希分目录，源文件被修改后自动失效。</summary>
    public static class ThumbCache
    {
        public static string DirectoryFor(string file)
        {
            return Path.Combine(AppPaths.ThumbRoot, Hash(file));
        }

        public static string PathFor(string file, string blockName)
        {
            return Path.Combine(DirectoryFor(file), TextUtil.SanitizeFileName(blockName) + "_" + Hash(blockName).Substring(0, 6) + ".png");
        }

        public static bool IsFresh(string file, string blockName)
        {
            try
            {
                string stamp = StampFor(file);
                string image = PathFor(file, blockName);
                if (!File.Exists(image) || !File.Exists(stamp)) return false;
                string recorded = File.ReadAllText(stamp, Encoding.UTF8).Trim();
                return string.Equals(recorded, ExpectedStamp(file), StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        public static void Save(string file, string blockName, Bitmap bitmap)
        {
            if (bitmap == null) return;
            try
            {
                string directory = DirectoryFor(file);
                Directory.CreateDirectory(directory);
                string image = PathFor(file, blockName);
                bitmap.Save(image, ImageFormat.Png);
                try { File.WriteAllText(StampFor(file), ExpectedStamp(file), new UTF8Encoding(false)); }
                catch { }
            }
            catch (Exception ex)
            {
                Log.Warn("写入缩略图失败: " + ex.Message);
            }
        }

        public static Bitmap Load(string file, string blockName)
        {
            try
            {
                if (!IsFresh(file, blockName)) return null;
                string image = PathFor(file, blockName);
                using (Image source = Image.FromFile(image))
                {
                    return new Bitmap(source);
                }
            }
            catch
            {
                return null;
            }
        }

        public static void ClearAll()
        {
            try
            {
                if (!Directory.Exists(AppPaths.ThumbRoot)) return;
                foreach (string directory in Directory.GetDirectories(AppPaths.ThumbRoot))
                {
                    try { Directory.Delete(directory, true); }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("清理缩略图缓存失败: " + ex.Message);
            }
        }

        public static long CacheSizeBytes()
        {
            long total = 0;
            try
            {
                if (!Directory.Exists(AppPaths.ThumbRoot)) return 0;
                foreach (string path in Directory.GetFiles(AppPaths.ThumbRoot, "*.png", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(path).Length; }
                    catch { }
                }
            }
            catch { }
            return total;
        }

        private static string StampFor(string file)
        {
            return Path.Combine(DirectoryFor(file), "_stamp.txt");
        }

        private static string ExpectedStamp(string file)
        {
            try
            {
                var info = new FileInfo(file);
                if (!info.Exists) return "missing";
                return info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) + "|" + info.Length.ToString(CultureInfo.InvariantCulture);
            }
            catch
            {
                return "error";
            }
        }

        private static string Hash(string value)
        {
            try
            {
                using (var md5 = MD5.Create())
                {
                    byte[] bytes = md5.ComputeHash(Encoding.UTF8.GetBytes((value ?? string.Empty).ToLowerInvariant()));
                    var sb = new StringBuilder();
                    for (int i = 0; i < 8; i++) sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
                    return sb.ToString();
                }
            }
            catch
            {
                return "default";
            }
        }
    }
}
