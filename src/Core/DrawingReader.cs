using System;
using System.IO;
using Autodesk.AutoCAD.DatabaseServices;

namespace CodexBlockLib.Core
{
    /// <summary>以侧数据库方式打开其它图纸（dwg/dwt/dws/dxf），不进入当前文档，也不修改源文件。</summary>
    public static class DrawingReader
    {
        public static readonly string[] SupportedExtensions = new string[] { ".dwg", ".dwt", ".dws", ".dxf" };

        public const string FileFilter =
            "图纸文件 (*.dwg;*.dwt;*.dws;*.dxf)|*.dwg;*.dwt;*.dws;*.dxf|" +
            "AutoCAD 图形 (*.dwg)|*.dwg|图形样板 (*.dwt)|*.dwt|图形标准 (*.dws)|*.dws|" +
            "图形交换格式 (*.dxf)|*.dxf|所有文件 (*.*)|*.*";

        public static bool IsSupported(string path)
        {
            return Detect(path) != SourceFormat.Unknown;
        }

        public static SourceFormat Detect(string path)
        {
            if (string.IsNullOrEmpty(path)) return SourceFormat.Unknown;
            string ext = Path.GetExtension(path);
            if (ext == null) return SourceFormat.Unknown;
            switch (ext.ToLowerInvariant())
            {
                case ".dwg": return SourceFormat.Dwg;
                case ".dwt": return SourceFormat.Dwt;
                case ".dws": return SourceFormat.Dws;
                case ".dxf": return SourceFormat.Dxf;
                default: return SourceFormat.Unknown;
            }
        }

        public static string FormatName(SourceFormat format)
        {
            switch (format)
            {
                case SourceFormat.Dwg: return "DWG 图形";
                case SourceFormat.Dwt: return "DWT 样板";
                case SourceFormat.Dws: return "DWS 标准";
                case SourceFormat.Dxf: return "DXF 交换文件";
                default: return "未知";
            }
        }

        /// <summary>打开图纸为侧数据库。失败时返回 null 并给出中文原因。</summary>
        public static Database Open(string path, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrEmpty(path)) { error = "路径为空"; return null; }
            if (!File.Exists(path)) { error = "文件不存在"; return null; }

            SourceFormat format = Detect(path);
            if (format == SourceFormat.Unknown) { error = "不支持的文件类型"; return null; }

            Database db = null;
            try
            {
                if (format == SourceFormat.Dxf)
                {
                    db = new Database(true, false);
                    string logFile = Path.Combine(Path.GetTempPath(), "cbl_dxfin_" + Guid.NewGuid().ToString("N") + ".log");
                    try
                    {
                        db.DxfIn(path, logFile);
                    }
                    catch (Exception ex)
                    {
                        // DXF 常有非致命错误，记录后继续使用已读入的内容
                        Log.Warn("DxfIn 报告问题(" + Path.GetFileName(path) + "): " + ex.Message);
                    }
                    finally
                    {
                        try { if (File.Exists(logFile)) File.Delete(logFile); }
                        catch { }
                    }
                }
                else
                {
                    db = new Database(false, true);
                    db.ReadDwgFile(path, FileOpenMode.OpenForReadAndReadShare, true, null);
                }

                return db;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Error("打开图纸失败: " + path, ex);
                if (db != null)
                {
                    try { db.Dispose(); }
                    catch { }
                }
                return null;
            }
        }
    }
}
