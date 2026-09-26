using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using CodexBlockLib.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CodexBlockLib.UI
{
    /// <summary>自检：把插件各环节的实际结果写入 selftest.txt，便于确认插件是否真的可用。</summary>
    internal static class SelfTest
    {
        public static string Run(string folder) { return Run(folder, string.Empty); }

        private static bool Want(string stages, char stage)
        {
            if (string.IsNullOrEmpty(stages) || stages.IndexOf('A') >= 0 || stages.IndexOf('a') >= 0) return true;
            return stages.IndexOf(stage) >= 0 || stages.IndexOf(char.ToLowerInvariant(stage)) >= 0;
        }

        public static string Run(string folder, string stages)
        {
            var sb = new StringBuilder();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            if (!RibbonMenu.UiAvailable && !string.Equals(stages, "ES", StringComparison.OrdinalIgnoreCase))
            {
                stages = "ES";
                sb.AppendLine("注意: 当前为无图形界面环境(accoreconsole)，仅执行 环境(E)/扫描(S) 阶段；");
                sb.AppendLine("      界面挂载、复制插入、后台扫描请在完整版 AutoCAD 中自检。");
            }
            sb.AppendLine("Codex 图块库 自检报告");
            sb.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine();

            if (Want(stages, 'E')) { Log.Write("自检阶段/环境"); Environment(sb); }
            if (Want(stages, 'U')) { Log.Write("自检阶段/界面"); Ui(sb); }
            List<FileScanResult> scanned = new List<FileScanResult>(); if (Want(stages, 'S')) { Log.Write("自检阶段/扫描"); scanned = Scan(sb, folder); }
            if (Want(stages, 'V')) { Log.Write("自检阶段/复制插入"); ImportInsert(sb, scanned); }
            if (Want(stages, 'B')) { Log.Write("自检阶段/后台"); Background(sb, scanned); }
            sb.AppendLine();
            sb.AppendLine("自检用时: " + watch.Elapsed.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture) + " 秒");
            sb.AppendLine("日志文件: " + AppPaths.LogFile);
            sb.AppendLine("近期日志:");
            sb.AppendLine(Log.Tail(40));

            string report = sb.ToString();
            try
            {
                AppPaths.Ensure();
                File.WriteAllText(AppPaths.SelfTestFile, report, new UTF8Encoding(true));
            }
            catch (Exception ex)
            {
                Log.Error("写入自检报告失败", ex);
            }
            Log.Write("自检完成");
            return report;
        }

        private static void Environment(StringBuilder sb)
        {
            sb.AppendLine("== 环境 ==");
            try { sb.AppendLine("AutoCAD 版本: " + AcApp.Version.ToString()); }
            catch (Exception ex) { sb.AppendLine("AutoCAD 版本: 读取失败 " + ex.Message); }
            sb.AppendLine("插件程序集: " + typeof(SelfTest).Assembly.Location);
            sb.AppendLine("核心程序集: " + typeof(BlockInfo).Assembly.Location);
            sb.AppendLine("初始化标记: " + BlockLibraryPlugin.Initialized);
            sb.AppendLine("配置文件: " + LibraryStore.FilePath + " (存在=" + File.Exists(LibraryStore.FilePath).ToString() + ")");
            sb.AppendLine("缩略图缓存: " + AppPaths.ThumbRoot + " (" + (ThumbCache.CacheSizeBytes() / 1024).ToString(CultureInfo.InvariantCulture) + " KB)");
            sb.AppendLine("设置: 分类方式=" + LibraryStore.Settings.ModeText
                + ", 按单位缩放=" + LibraryStore.Settings.AutoScaleByUnits
                + ", 缩略图=" + LibraryStore.Settings.ThumbSize
                + ", 已登记源图纸=" + LibraryStore.Settings.SourceFiles.Count.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine();
        }

        private static void Ui(StringBuilder sb)
        {
            sb.AppendLine("== 界面挂载 ==");
            if (!RibbonMenu.UiAvailable) { sb.AppendLine("当前进程无图形界面(accoreconsole)：功能区/菜单栏/面板在完整版 AutoCAD 中验证，本次跳过。"); sb.AppendLine(); return; }
            try
            {
                Log.Write("  界面/Build"); RibbonMenu.Build();
                sb.AppendLine("功能区: " + RibbonMenu.DescribeRibbon());
            }
            catch (Exception ex)
            {
                sb.AppendLine("功能区: 异常 " + ex.Message);
            }
            try
            {
                Log.Write("  界面/菜单栏COM"); sb.AppendLine("菜单栏(COM): " + RibbonMenu.DescribeMenuBar());
            }
            catch (Exception ex)
            {
                sb.AppendLine("菜单栏(COM): 异常 " + ex.Message);
            }
            try
            {
                Log.Write("  界面/面板"); MainPalette palette = PaletteHost.EnsureCreated();
                sb.AppendLine("面板: " + (palette == null ? "创建失败" : "创建成功 (PaletteSet 可用)"));
            }
            catch (Exception ex)
            {
                sb.AppendLine("面板: 异常 " + ex.Message);
            }
            sb.AppendLine();
        }

        private static List<FileScanResult> Scan(StringBuilder sb, string folder)
        {
            sb.AppendLine("== 扫描 ==");
            var results = new List<FileScanResult>();
            ScanOptions options = LibraryStore.Settings.ToScanOptions();

            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc != null)
            {
                try
                {
                    string label = string.IsNullOrEmpty(doc.Name) ? "当前图纸(未保存)" : Path.GetFileName(doc.Name);
                    FileScanResult current = BlockScanner.ScanDatabase(doc.Database, doc.Name ?? label, options, null);
                    current.Label = label;
                    CategoryRules.Apply(current.Blocks, LibraryStore.Settings);
                    results.Add(current);
                    sb.AppendLine("当前图纸: " + current.SummaryLine());
                }
                catch (Exception ex)
                {
                    sb.AppendLine("当前图纸扫描失败: " + ex.Message);
                    Log.Error("自检扫描当前图纸失败", ex);
                }
            }
            else
            {
                sb.AppendLine("当前图纸: 没有打开任何图纸");
            }

            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                var files = new List<string>();
                foreach (string extension in DrawingReader.SupportedExtensions)
                {
                    try { files.AddRange(Directory.GetFiles(folder, "*" + extension, SearchOption.AllDirectories)); }
                    catch (Exception ex) { sb.AppendLine("枚举文件夹失败: " + ex.Message); }
                }
                if (files.Count > 40)
                {
                    sb.AppendLine("(该文件夹共 " + files.Count.ToString(CultureInfo.InvariantCulture) + " 个图纸，只测试前 40 个)");
                    files = files.GetRange(0, 40);
                }
                sb.AppendLine("附加扫描文件夹: " + folder + " (" + files.Count.ToString(CultureInfo.InvariantCulture) + " 个文件)");
                foreach (string file in files)
                {
                    try
                    {
                        ScanOptions quick = options.Clone();
                        quick.BuildThumbnails = false;
                        FileScanResult result = BlockScanner.ScanFile(file, quick, null);
                        CategoryRules.Apply(result.Blocks, LibraryStore.Settings);
                        results.Add(result);
                        sb.AppendLine("  " + result.SummaryLine());
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine("  扫描失败: " + Path.GetFileName(file) + " => " + ex.Message);
                    }
                }
            }
            else if (!string.IsNullOrEmpty(folder))
            {
                sb.AppendLine("附加扫描文件夹不存在: " + folder);
            }

            int dynamicFiles = 0;
            foreach (FileScanResult result in results)
            {
                if (result.Ok && result.DynamicDefinitionCount > 0) dynamicFiles++;
            }
            sb.AppendLine("扫描汇总: " + results.Count.ToString(CultureInfo.InvariantCulture) + " 张图纸, 其中 "
                + dynamicFiles.ToString(CultureInfo.InvariantCulture) + " 张含动态块");
            sb.AppendLine();
            return results;
        }
        private static void ImportInsert(StringBuilder sb, List<FileScanResult> results)
        {
            sb.AppendLine("== 复制/插入测试（在临时新建的图纸中进行，完成后自动关闭，不影响当前图纸）==");
            Log.Write("  复制/进入 ImportInsert");

            FileScanResult best;
            BlockInfo block = PickTestBlock(results, out best);
            if (block == null)
            {
                sb.AppendLine("跳过：没有可用于测试的块定义");
                sb.AppendLine();
                return;
            }

            Log.Write("  复制/用例块=" + block.Name);
            sb.AppendLine("测试用块: " + block.Name + " [" + block.TypeText + "] 来自 " + best.DisplayName);
            sb.AppendLine("源块参照句柄: " + (string.IsNullOrEmpty(block.SampleRefHandle) ? "(无)" : block.SampleRefHandle)
                + ", 源比例 " + block.SampleScale.ToString("0.###", CultureInfo.InvariantCulture));

            CopyTest(sb, best, block);
            sb.AppendLine();
        }

        private static BlockInfo PickTestBlock(List<FileScanResult> results, out FileScanResult best)
        {
            best = null;
            foreach (FileScanResult result in results)
            {
                if (!result.Ok || result.IsCurrentDrawing) continue;
                if (result.DynamicDefinitionCount == 0) continue;
                foreach (BlockInfo info in result.BlocksByCount())
                {
                    if (info.IsXref || info.IsUnresolved || info.InstanceCount == 0) continue;
                    best = result;
                    return info;
                }
            }
            foreach (FileScanResult result in results)
            {
                if (!result.Ok) continue;
                foreach (BlockInfo info in result.BlocksByCount())
                {
                    if (info.IsXref || info.IsUnresolved) continue;
                    best = result;
                    return info;
                }
            }
            return null;
        }

        private sealed class CopyState
        {
            public int Imported;
            public int Before;
            public int AfterInsert;
            public int AfterPaste;
            public ObjectId Inserted = ObjectId.Null;
            public ObjectId Pasted = ObjectId.Null;
            public string Error = string.Empty;
        }

        private static void CopyTest(StringBuilder sb, FileScanResult best, BlockInfo block)
        {
            Document scratch = null;
            Database source = null;
            try
            {
                Log.Write("  复制/打开源图纸");
                string error;
                source = DrawingReader.Open(best.Path, out error);
                if (source == null)
                {
                    sb.AppendLine("跳过：无法打开源图纸 " + error);
                    return;
                }
                Log.Write("  复制/源图纸已打开");
                sb.AppendLine("单位换算: 源单位=" + UnitConvert.UnitName(source.Insunits));

                Log.Write("  复制/新建临时图纸");
                scratch = AcApp.DocumentManager.Add("");
                using (scratch.LockDocument())
                {
                    sb.AppendLine("临时图纸单位=" + UnitConvert.UnitName(scratch.Database.Insunits)
                        + ", 插入比例=" + UnitConvert.ScaleFactor(source, scratch.Database).ToString("0.######", CultureInfo.InvariantCulture));
                    var st = new CopyState();
                    StepImport(sb, source, scratch.Database, block, st);
                    if (st.Imported <= 0 && !Importer.HasDefinition(scratch.Database, block.Name))
                    {
                        sb.AppendLine("导入失败，后续测试中止。");
                        return;
                    }
                    StepInsert(sb, source, scratch.Database, block, st);
                    StepClone(sb, source, scratch.Database, block, st);
                    StepDefinition(sb, scratch.Database, block);
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("复制/插入测试异常: " + ex.Message);
                Log.Error("自检复制/插入测试失败", ex);
            }
            finally
            {
                if (source != null) { try { source.Dispose(); } catch { } }
                if (scratch != null)
                {
                    try { scratch.CloseAndDiscard(); }
                    catch (Exception ex) { sb.AppendLine("关闭临时图纸失败: " + ex.Message); }
                }
            }
        }
        private static void StepImport(StringBuilder sb, Database source, Database target, BlockInfo block, CopyState st)
        {
            Log.Write("  复制/导入块定义");
            st.Imported = Importer.ImportDefinitions(source, new string[] { block.Name }, target, out st.Error);
            st.Before = Importer.CountReferences(target, block.Name);
            sb.AppendLine("① 导入块定义: " + (st.Imported > 0 ? "成功 (" + st.Imported.ToString(CultureInfo.InvariantCulture) + " 个)" : "失败: " + st.Error)
                + ", 导入前参照数 " + st.Before.ToString(CultureInfo.InvariantCulture));
        }

        private static void StepInsert(StringBuilder sb, Database source, Database target, BlockInfo block, CopyState st)
        {
            Log.Write("  复制/插入块参照");
            st.Inserted = Importer.InsertDefinition(source, block.Name, target, new Point3d(1000.0, 1000.0, 0.0), 1.0, 0.0, null, out st.Error);
            st.AfterInsert = Importer.CountReferences(target, block.Name);
            sb.AppendLine("② 插入块参照: " + (st.Inserted.IsNull ? "失败: " + st.Error : "成功, 句柄 " + HandleOf(st.Inserted))
                + ", 参照数 " + st.Before.ToString(CultureInfo.InvariantCulture) + " -> " + st.AfterInsert.ToString(CultureInfo.InvariantCulture));
        }

        private static void StepClone(StringBuilder sb, Database source, Database target, BlockInfo block, CopyState st)
        {
            if (string.IsNullOrEmpty(block.SampleRefHandle))
            {
                sb.AppendLine("③ 原样粘贴: 跳过（源块没有参照实例）");
                return;
            }
            Log.Write("  复制/原样粘贴");
            st.Pasted = Importer.CloneReference(source, block.SampleRefHandle, target, target.CurrentSpaceId,
                new Point3d(2000.0, 1000.0, 0.0), false, true, out st.Error);
            st.AfterPaste = Importer.CountReferences(target, block.Name);
            sb.AppendLine("③ 原样粘贴(含动态参数): " + (st.Pasted.IsNull ? "失败: " + st.Error : "成功, 句柄 " + HandleOf(st.Pasted))
                + ", 参照数 " + st.AfterInsert.ToString(CultureInfo.InvariantCulture) + " -> " + st.AfterPaste.ToString(CultureInfo.InvariantCulture));
            if (!st.Pasted.IsNull) DescribeDynamic(sb, target, st.Pasted);
            if (!st.Inserted.IsNull) DescribeDynamic(sb, target, st.Inserted);
        }

        private static void StepDefinition(StringBuilder sb, Database target, BlockInfo block)
        {
            try
            {
                using (Transaction tr = target.TransactionManager.StartTransaction())
                {
                    var table = (BlockTable)tr.GetObject(target.BlockTableId, OpenMode.ForRead);
                    if (table.Has(block.Name))
                    {
                        var record = (BlockTableRecord)tr.GetObject(table[block.Name], OpenMode.ForRead);
                        sb.AppendLine("④ 目标图纸中的块定义: 图元 " + record.GetBlockReferenceIds(true, false).Count.ToString(CultureInfo.InvariantCulture)
                            + " 个直接参照, 动态块=" + DynamicBlockDetector.IsDynamicDefinition(record));
                    }
                    tr.Commit();
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("④ 检查目标块定义失败: " + ex.Message);
            }
        }
        private static void DescribeDynamic(StringBuilder sb, Database db, ObjectId referenceId)
        {
            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    var reference = tr.GetObject(referenceId, OpenMode.ForRead, false) as BlockReference;
                    if (reference == null) { tr.Commit(); return; }
                    bool isDynamic = reference.IsDynamicBlock;
                    var parts = new List<string>();
                    if (isDynamic)
                    {
                        DynamicBlockReferencePropertyCollection properties = reference.DynamicBlockReferencePropertyCollection;
                        for (int i = 0; i < properties.Count; i++)
                        {
                            DynamicBlockReferenceProperty property = properties[i];
                            if (property == null) continue;
                            parts.Add(property.PropertyName + "=" + TextUtil.FormatValue(property.Value));
                        }
                    }
                    sb.AppendLine("   动态块=" + isDynamic + ", 实例参数: " + (parts.Count == 0 ? "(无)" : string.Join(", ", parts.ToArray())));
                    tr.Commit();
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("   读取动态参数失败: " + ex.Message);
            }
        }

        private static void Background(StringBuilder sb, List<FileScanResult> results)
        {
            sb.AppendLine("== 后台线程扫描测试 ==");
            string file = null;
            foreach (FileScanResult result in results)
            {
                if (result.Ok && !result.IsCurrentDrawing && result.DynamicInstanceCount > 0) { file = result.Path; break; }
            }
            if (file == null)
            {
                foreach (FileScanResult result in results)
                {
                    if (result.Ok && !result.IsCurrentDrawing) { file = result.Path; break; }
                }
            }
            if (file == null) { sb.AppendLine("跳过：没有可用于测试的文件"); sb.AppendLine(); return; }

            string target = file;
            try
            {
                ScanOptions options = LibraryStore.Settings.ToScanOptions();
                options.BuildThumbnails = false;
                Task<string> task = Task.Factory.StartNew(delegate
                {
                    FileScanResult result = BlockScanner.ScanFile(target, options, null);
                    if (!result.Ok) return "失败: " + result.Error;
                    return "成功: 动态块定义 " + result.DynamicDefinitionCount.ToString(CultureInfo.InvariantCulture)
                        + " 个, 实例 " + result.DynamicInstanceCount.ToString(CultureInfo.InvariantCulture)
                        + " 个, 用时 " + result.ScanSeconds.ToString("0.00", CultureInfo.InvariantCulture) + "s";
                });
                if (!task.Wait(TimeSpan.FromSeconds(90))) sb.AppendLine("超时(90 秒): " + Path.GetFileName(target));
                else sb.AppendLine("后台扫描 " + Path.GetFileName(target) + " => " + task.Result);
            }
            catch (Exception ex)
            {
                sb.AppendLine("后台扫描异常: " + ex.Message);
                Log.Error("自检后台扫描失败", ex);
            }
            sb.AppendLine();
        }

        private static string HandleOf(ObjectId id)
        {
            try
            {
                using (var tr = id.Database.TransactionManager.StartTransaction())
                {
                    DBObject obj = tr.GetObject(id, OpenMode.ForRead, false);
                    string handle = obj != null ? obj.Handle.ToString() : "?";
                    tr.Commit();
                    return handle;
                }
            }
            catch
            {
                return "?";
            }
        }
    }
}
