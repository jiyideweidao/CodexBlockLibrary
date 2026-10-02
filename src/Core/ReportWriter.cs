using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CodexBlockLib.Core
{
    /// <summary>统计报表输出：CSV（UTF-8 带 BOM，Excel 直接打开不乱码）与命令行文本摘要。</summary>
    public static class ReportWriter
    {
        public static string BuildDefaultCsvPath(string prefix)
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CodexBlockLib");
            try { Directory.CreateDirectory(folder); }
            catch { folder = Path.GetTempPath(); }
            string name = (string.IsNullOrEmpty(prefix) ? "块库统计" : prefix) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv";
            return Path.Combine(folder, name);
        }

        public static string WriteCsv(IList<FileScanResult> results, string path)
        {
            if (results == null) results = new List<FileScanResult>();
            if (string.IsNullOrEmpty(path)) path = BuildDefaultCsvPath("块库统计");

            var sb = new StringBuilder();
            sb.AppendLine("Codex 图块库统计,生成时间," + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine();
            sb.AppendLine("汇总");
            sb.AppendLine("来源,格式,多状态动态块,动态块实例,单位,布局数,图元数(模型),扫描用时(秒),状态");

            int files = results.Count;
            int dynDef = 0, dynIns = 0;
            foreach (FileScanResult result in results)
            {
                if (result == null) continue;
                dynDef += result.DynamicDefinitionCount;
                dynIns += result.DynamicInstanceCount;
                sb.AppendLine(Csv(result.DisplayName) + "," + Csv(result.Format) + ","
                    + result.DynamicDefinitionCount.ToString(CultureInfo.InvariantCulture) + ","
                    + result.DynamicInstanceCount.ToString(CultureInfo.InvariantCulture) + ","
                    + Csv(result.InsUnitsName) + ","
                    + result.LayoutCount.ToString(CultureInfo.InvariantCulture) + ","
                    + result.ModelEntityCount.ToString(CultureInfo.InvariantCulture) + ","
                    + result.ScanSeconds.ToString("0.00", CultureInfo.InvariantCulture) + ","
                    + Csv(result.Ok ? "正常" : ("失败: " + result.Error)));
            }
            sb.AppendLine(Csv("合计(" + files.ToString(CultureInfo.InvariantCulture) + " 张图纸)") + ",," 
                + dynDef.ToString(CultureInfo.InvariantCulture) + "," + dynIns.ToString(CultureInfo.InvariantCulture) + ","
                + ",,,,,,,");
            sb.AppendLine();

            sb.AppendLine("块明细");
            sb.AppendLine("序号,来源图纸,分类,块名,实例合计,模型空间,图纸空间,嵌套引用,动态变体数,可见性状态,参数,主要图层,标签");
            int index = 0;
            foreach (FileScanResult result in results)
            {
                if (result == null || result.Blocks == null) continue;
                foreach (BlockInfo info in result.BlocksByCount())
                {
                    index++;
                    sb.AppendLine(string.Join(",", new string[]
                    {
                        index.ToString(CultureInfo.InvariantCulture),
                        Csv(result.DisplayName),
                        Csv(info.CategoryOrFallback),
                        Csv(info.Name),
                        info.InstanceCount.ToString(CultureInfo.InvariantCulture),
                        info.ModelCount.ToString(CultureInfo.InvariantCulture),
                        info.PaperCount.ToString(CultureInfo.InvariantCulture),
                        info.NestedCount.ToString(CultureInfo.InvariantCulture),
                        info.Variants.Count.ToString(CultureInfo.InvariantCulture),
                        Csv(info.VisibilityStatesText),
                        Csv(ParameterText(info)),
                        Csv(LayerText(info)),
                        Csv(string.Join("; ", info.Tags.ToArray()))
                    }));
                }
            }

            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                Log.Write("已导出统计表: " + path + "（" + index.ToString(CultureInfo.InvariantCulture) + " 行）");
                return path;
            }
            catch (Exception ex)
            {
                Log.Error("导出 CSV 失败", ex);
                throw;
            }
        }

        private static string ParameterText(BlockInfo info)
        {
            var names = new List<string>();
            foreach (DynamicPropertyInfo property in info.Properties)
            {
                names.Add(property.Name + "(" + property.TypeName + ": " + property.CurrentSummary + ")");
            }
            return string.Join("; ", names.ToArray());
        }

        private static string LayerText(BlockInfo info)
        {
            var names = new List<string>();
            int n = 0;
            foreach (KeyValuePair<string, int> pair in info.DefinitionLayers)
            {
                names.Add(pair.Key);
                if (++n >= 5) break;
            }
            return string.Join("; ", names.ToArray());
        }

        /// <summary>命令行摘要：每张图纸一行，并列出实例最多的动态块。</summary>
        public static string SummaryText(IList<FileScanResult> results, int topDynamic)
        {
            var sb = new StringBuilder();
            if (results == null || results.Count == 0) return "(没有扫描结果)";
            int dynDef = 0, dynIns = 0;
            foreach (FileScanResult result in results)
            {
                if (result == null) continue;
                sb.AppendLine(result.SummaryLine());
                dynDef += result.DynamicDefinitionCount;
                dynIns += result.DynamicInstanceCount;
            }
            sb.AppendLine("合计: " + results.Count.ToString(CultureInfo.InvariantCulture) + " 张图纸, 多状态动态块（可见性状态≥2）"
                + dynDef.ToString(CultureInfo.InvariantCulture) + " 个, 动态块实例 " + dynIns.ToString(CultureInfo.InvariantCulture) + " 个");

            if (topDynamic > 0)
            {
                var all = new List<BlockInfo>();
                foreach (FileScanResult result in results)
                {
                    if (result == null) continue;
                    foreach (BlockInfo info in result.DynamicBlocks) all.Add(info);
                }
                all.Sort(delegate(BlockInfo a, BlockInfo b) { return b.InstanceCount.CompareTo(a.InstanceCount); });
                if (all.Count > 0)
                {
                    sb.AppendLine("多状态动态块明细（按实例数，可见性状态≥2）:");
                    for (int i = 0; i < all.Count && i < topDynamic; i++)
                    {
                        BlockInfo info = all[i];
                        sb.AppendLine("  " + (i + 1).ToString(CultureInfo.InvariantCulture) + ". " + info.Name
                            + "  实例 " + info.InstanceCount.ToString(CultureInfo.InvariantCulture)
                            + "  变体 " + info.Variants.Count.ToString(CultureInfo.InvariantCulture)
                            + (info.VisibilityStatesText.Length > 0 ? "  可见性: " + info.VisibilityStatesText : string.Empty)
                            + "  [" + Path.GetFileName(info.SourceFile) + "]");
                    }
                }
            }
            return sb.ToString();
        }

        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            string text = value.Replace("\"", "\"\"");
            if (text.IndexOf(',') >= 0 || text.IndexOf('"') >= 0 || text.IndexOf('\n') >= 0 || text.IndexOf('\r') >= 0 || text.IndexOf(' ') >= 0)
            {
                return "\"" + text + "\"";
            }
            return text;
        }
    }
}
