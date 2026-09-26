using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;
using CodexBlockLib.Core;

namespace CodexBlockLib.UI
{
    /// <summary>运行时绘制功能区图标，避免额外资源文件。</summary>
    internal static class Icons
    {
        private const int Size = 32;

        public static Bitmap MakeBitmap(string kind)
        {
            return Draw(kind);
        }

        public static System.Windows.Media.ImageSource Make(string kind)
        {
            using (Bitmap bitmap = Draw(kind))
            {
                return ToSource(bitmap);
            }
        }

        private static Bitmap Draw(string kind)
        {
            var bitmap = new Bitmap(Size, Size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                Color dark = Color.FromArgb(38, 62, 96);
                Color accent = Color.FromArgb(0, 122, 204);
                using (var darkPen = new Pen(dark, 2.0f))
                using (var accentPen = new Pen(accent, 2.0f))
                using (var darkBrush = new SolidBrush(dark))
                using (var accentBrush = new SolidBrush(accent))
                {
                    switch (kind)
                    {
                        case "library":
                            DrawSquare(g, darkPen, 5, 5);
                            DrawSquare(g, accentPen, 17, 5);
                            DrawSquare(g, accentPen, 5, 17);
                            DrawSquare(g, darkPen, 17, 17);
                            break;
                        case "scan":
                            g.DrawEllipse(darkPen, 6, 6, 15, 15);
                            g.DrawLine(darkPen, 19, 19, 26, 26);
                            g.DrawLine(accentPen, 10, 13, 17, 13);
                            break;
                        case "stats":
                            g.FillRectangle(darkBrush, 7, 18, 4, 8);
                            g.FillRectangle(accentBrush, 14, 12, 4, 14);
                            g.FillRectangle(darkBrush, 21, 7, 4, 19);
                            break;
                        case "export":
                            g.DrawRectangle(darkPen, 7, 4, 13, 17);
                            g.DrawLine(accentPen, 10, 10, 17, 10);
                            g.DrawLine(accentPen, 10, 14, 17, 14);
                            g.DrawLine(accentPen, 13, 22, 20, 29);
                            g.DrawLine(accentPen, 13, 29, 20, 22);
                            break;
                        case "insert":
                            g.DrawRectangle(darkPen, 4, 16, 13, 12);
                            g.DrawLine(accentPen, 9, 12, 22, 12);
                            g.DrawLine(accentPen, 17, 6, 22, 12);
                            g.DrawLine(accentPen, 17, 18, 22, 12);
                            break;
                        case "copy":
                            g.DrawRectangle(darkPen, 5, 5, 14, 16);
                            g.DrawRectangle(accentPen, 13, 11, 14, 16);
                            break;
                        case "settings":
                            g.DrawEllipse(darkPen, 9, 9, 14, 14);
                            g.DrawEllipse(accentPen, 14, 14, 4, 4);
                            g.DrawLine(darkPen, 16, 3, 16, 8);
                            g.DrawLine(darkPen, 16, 24, 16, 29);
                            g.DrawLine(darkPen, 3, 16, 8, 16);
                            g.DrawLine(darkPen, 24, 16, 29, 16);
                            break;
                        case "tag":
                            g.DrawRectangle(darkPen, 4, 6, 17, 12);
                            g.FillEllipse(accentBrush, 22, 20, 6, 6);
                            break;
                        case "close":
                            g.DrawEllipse(darkPen, 6, 6, 20, 20);
                            g.DrawLine(accentPen, 11, 11, 21, 21);
                            g.DrawLine(accentPen, 21, 11, 11, 21);
                            break;
                        case "fold":
                            g.DrawLine(darkPen, 6, 24, 26, 24);
                            g.DrawLine(accentPen, 10, 16, 16, 9);
                            g.DrawLine(accentPen, 16, 9, 22, 16);
                            break;
                        case "uninstall":
                            g.DrawLine(darkPen, 6, 9, 26, 9);
                            g.DrawLine(darkPen, 13, 5, 19, 5);
                            g.DrawRectangle(darkPen, 9, 11, 14, 16);
                            g.DrawLine(accentPen, 14, 15, 18, 23);
                            g.DrawLine(accentPen, 18, 15, 14, 23);
                            break;
                        default:
                            g.FillEllipse(accentBrush, 10, 10, 12, 12);
                            break;
                    }
                }
            }
            return bitmap;
        }

        private static void DrawSquare(Graphics g, Pen pen, int x, int y)
        {
            g.DrawRectangle(pen, x, y, 10, 10);
        }

        private static System.Windows.Media.ImageSource ToSource(Bitmap bitmap)
        {
            using (var stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);
                stream.Position = 0;
                var image = new BitmapImage();
                image.BeginInit();
                image.StreamSource = stream;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                return image;
            }
        }
    }
}
