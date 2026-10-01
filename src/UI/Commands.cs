using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using CodexBlockLib.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CodexBlockLib.UI
{
    /// <summary>对外命令集合。</summary>
    public static class Commands
    {
        [CommandMethod("BLKLIB", CommandFlags.Modal)]
        public static void ShowLibrary()
        {
            try
            {
                // 启动时若因 COM 忙导致经典菜单栏没挂上，这里立即补挂一次（幂等）。
                RibbonMenu.EnsureMenuBar();
                PaletteHost.Show();
                MainPalette palette = PaletteHost.Control;
                if (palette != null) palette.ReloadFromSession();
            }
            catch (System.Exception ex)
            {
                Log.Error("BLKLIB 失败", ex);
                Write("块库面板打开失败: " + ex.Message);
            }
        }

        /// <summary>只重新扫描面板列表里选中的图纸（面板工具栏「重新扫描选中」的命令行版本）。</summary>
        [CommandMethod("BLKRESCAN", CommandFlags.Modal)]
        public static void RescanRegistered()
        {
            try
            {
                MainPalette palette = PaletteHost.EnsureCreated();
                if (palette == null) { Write("当前环境没有图形界面，无法扫描。"); return; }
                PaletteHost.Show();
                int count = palette.RescanAllSources();
                if (count == 0) Write("请先在面板列表里选中要重新扫描的图纸；要扫描新图纸请点面板里的「添加图纸」。");
                else Write("开始重新扫描选中的 " + count.ToString(CultureInfo.InvariantCulture) + " 张图纸，进度见面板状态栏。");
            }
            catch (System.Exception ex)
            {
                Log.Error("BLKRESCAN 失败", ex);
                Write("扫描失败: " + ex.Message);
            }
        }

        /// <summary>暂停 / 继续正在进行的图纸扫描（面板工具栏“暂停扫描”的命令行版本）。</summary>
        [CommandMethod("BLKPAUSE", CommandFlags.Modal)]
        public static void ToggleScanPause()
        {
            try
            {
                MainPalette palette = PaletteHost.Control;
                if (palette == null) { Write("块库面板尚未创建，请先执行 BLKLIB 打开面板。"); return; }
                palette.ToggleScanPause();
            }
            catch (System.Exception ex)
            {
                Log.Error("BLKPAUSE 失败", ex);
                Write("暂停/继续扫描失败: " + ex.Message);
            }
        }

        [CommandMethod("BLKCOLLAPSE", CommandFlags.Modal)]
        public static void TogglePanelCollapse()
        {
            try
            {
                PaletteHost.EnsureCreated();
                PaletteHost.ToggleCollapsed();
                LibraryStore.Settings.PanelCollapsed = PaletteHost.Collapsed;
                LibraryStore.Save();
                Write(PaletteHost.Collapsed ? "面板已折叠（再次执行 BLKCOLLAPSE 可展开）。" : "面板已展开。");
            }
            catch (System.Exception ex)
            {
                Log.Error("BLKCOLLAPSE 失败", ex);
                Write("切换折叠失败: " + ex.Message);
            }
        }

        [CommandMethod("BLKUNINSTALL", CommandFlags.Modal)]
        public static void UninstallCommand()
        {
            try { UninstallPluginInteractive(Owner()); }
            catch (System.Exception ex)
            {
                Log.Error("BLKUNINSTALL 失败", ex);
                Write("卸载失败: " + ex.Message);
            }
        }

        /// <summary>卸载交互流程（面板“卸载”按钮与命令共用）。</summary>
        internal static void UninstallPluginInteractive(IWin32Window owner)
        {
            string bundleDir;
            string reason;
            if (!Uninstaller.Validate(out bundleDir, out reason))
            {
                MessageBox.Show(owner, reason, "无法卸载", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string question = "确定要卸载 Codex 图块库吗？\r\n\r\n将删除插件目录：\r\n" + bundleDir +
                "\r\n\r\n删除后 AutoCAD 下次启动不再加载本插件；本次会话中的命令仍可用。";
            if (MessageBox.Show(owner, question, "卸载 Codex 图块库", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

            DialogResult data = MessageBox.Show(owner,
                "是否同时删除用户配置？\r\n\r\n" + AppPaths.Root + "\r\n（已登记源图纸、自定义分类与标签、缩略图缓存、日志）",
                "删除用户配置", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);

            string message = Uninstaller.Run(data == DialogResult.Yes);
            Log.Write("卸载: " + message.Replace("\r", string.Empty).Replace("\n", " | "));
            Write(message);
            MessageBox.Show(owner, message + "\r\n\r\n提示：请重启 AutoCAD 完成卸载。", "卸载完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        [CommandMethod("BLKCLOSE", CommandFlags.Modal)]
        public static void CloseLibrary()
        {
            try
            {
                PaletteHost.Hide();
                Write("图块库面板已关闭。输入 BLKLIB 可重新打开。");
            }
            catch (System.Exception ex)
            {
                Log.Error("BLKCLOSE 失败", ex);
                Write("关闭面板失败: " + ex.Message);
            }
        }

        [CommandMethod("BLKSCAN", CommandFlags.Modal)]
        public static void ScanDrawings()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor editor = doc.Editor;
            try
            {
                var options = new PromptKeywordOptions("\n选择扫描对象 [文件(F)/文件夹(D)]: ", "F D");
                PromptResult keyword = editor.GetKeywords(options);
                if (keyword.Status != PromptStatus.OK) return;

                var files = new List<string>();
                if (keyword.StringResult == "F")
                {
                    using (var dialog = new OpenFileDialog())
                    {
                        dialog.Title = "选择要扫描的图纸（可多选）";
                        dialog.Filter = DrawingReader.FileFilter;
                        dialog.Multiselect = true;
                        if (dialog.ShowDialog(Owner()) != DialogResult.OK) return;
                        files.AddRange(dialog.FileNames);
                    }
                }
                else
                {
                    string folder = GetString(editor, "\n输入文件夹路径: ");
                    if (string.IsNullOrEmpty(folder)) return;
                    if (!Directory.Exists(folder)) { Write("文件夹不存在: " + folder); return; }
                    foreach (string extension in DrawingReader.SupportedExtensions)
                    {
                        files.AddRange(Directory.GetFiles(folder, "*" + extension, SearchOption.AllDirectories));
                    }
                }

                if (files.Count == 0) { Write("没有找到受支持的图纸文件。"); return; }
                Write("准备扫描 " + files.Count.ToString(CultureInfo.InvariantCulture) + " 张图纸...");

                ScanOptions scanOptions = LibraryStore.Settings.ToScanOptions();
                var results = new List<FileScanResult>();
                for (int i = 0; i < files.Count; i++)
                {
                    string file = files[i];
                    Write("  (" + (i + 1).ToString(CultureInfo.InvariantCulture) + "/" + files.Count.ToString(CultureInfo.InvariantCulture) + ") "
                        + Path.GetFileName(file));
                    try
                    {
                        FileScanResult result = BlockScanner.ScanFile(file, scanOptions, null);
                        CategoryRules.Apply(result.Blocks, LibraryStore.Settings);
                        results.Add(result);
                        ScanSession.AddOrReplace(result);
                        LibraryStore.AddSourceFile(file);
                    }
                    catch (System.Exception ex)
                    {
                        Log.Error("扫描失败: " + file, ex);
                        Write("    失败: " + ex.Message);
                    }
                }

                int dynamicTotal = 0;
                int instanceTotal = 0;
                foreach (FileScanResult scanned in results)
                {
                    foreach (BlockInfo info in scanned.Blocks)
                    {
                        if (!info.IsDynamic) continue;
                        dynamicTotal++;
                        instanceTotal += info.InstanceCount;
                    }
                }
                Log.Write("扫描任务完成: 图纸 " + files.Count.ToString(CultureInfo.InvariantCulture) + " 张, 动态块定义 " + dynamicTotal.ToString(CultureInfo.InvariantCulture) + " 个, 参照 " + instanceTotal.ToString(CultureInfo.InvariantCulture) + " 处");

                Write(ReportWriter.SummaryText(results, 15));
                Write("提示: 输入 BLKLIB 打开块库面板查看/插入；输入 BLKSTATS 导出统计表。");
            }
            catch (System.Exception ex)
            {
                Log.Error("BLKSCAN 失败", ex);
                Write("扫描失败: " + ex.Message);
            }
        }

        /// <summary>扫描“当前打开的图纸”，把它的图块加入块库，并打印动态块统计（可选导出 CSV）。</summary>
        [CommandMethod("BLKCOUNT", CommandFlags.Modal)]
        public static void CountDynamicBlocks()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor editor = doc.Editor;
            try
            {
                string label = string.IsNullOrEmpty(doc.Name) ? "当前图纸(未保存)" : Path.GetFileName(doc.Name);
                FileScanResult result = BlockScanner.ScanDatabase(doc.Database, doc.Name ?? label, LibraryStore.Settings.ToScanOptions(), null);
                result.Label = label;
                CategoryRules.Apply(result.Blocks, LibraryStore.Settings);
                ScanSession.AddOrReplace(result);

                Write(ReportWriter.SummaryText(new FileScanResult[] { result }, 20));

                var options = new PromptKeywordOptions("\n是否导出统计表 [是(Y)/否(N)]: ", "Y N");
                PromptResult keyword = editor.GetKeywords(options);
                if (keyword.Status == PromptStatus.OK && keyword.StringResult == "Y")
                {
                    string path = ReportWriter.WriteCsv(new List<FileScanResult>(new FileScanResult[] { result }), ReportWriter.BuildDefaultCsvPath("动态块统计"));
                    Write("已导出: " + path);
                }
                Write("提示: 输入 BLKLIB 打开块库面板；BLKSCAN 扫描其它图纸。");
            }
            catch (System.Exception ex)
            {
                Log.Error("BLKCOUNT 失败", ex);
                Write("扫描当前图纸失败: " + ex.Message);
            }
        }

        [CommandMethod("BLKSTATS", CommandFlags.Modal)]
        public static void ExportStatistics()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor editor = doc.Editor;
            try
            {
                List<FileScanResult> results = ScanSession.Results;
                if (results.Count == 0)
                {
                    Write("尚未扫描任何图纸，先扫描当前图纸...");
                    string label = string.IsNullOrEmpty(doc.Name) ? "当前图纸(未保存)" : Path.GetFileName(doc.Name);
                    FileScanResult current = BlockScanner.ScanDatabase(doc.Database, doc.Name ?? label, LibraryStore.Settings.ToScanOptions(), null);
                    current.Label = label;
                    CategoryRules.Apply(current.Blocks, LibraryStore.Settings);
                    ScanSession.AddOrReplace(current);
                    results = ScanSession.Results;
                }

                string path = ReportWriter.WriteCsv(results, ReportWriter.BuildDefaultCsvPath("块库统计"));
                Write("已导出统计表: " + path);
                var options = new PromptKeywordOptions("\n是否打开所在文件夹 [是(Y)/否(N)]: ", "Y N");
                PromptResult keyword = editor.GetKeywords(options);
                if (keyword.Status == PromptStatus.OK && keyword.StringResult == "Y")
                {
                    try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\""); }
                    catch (System.Exception ex) { Log.Warn("打开文件夹失败: " + ex.Message); }
                }
            }
            catch (System.Exception ex)
            {
                Log.Error("BLKSTATS 失败", ex);
                Write("导出失败: " + ex.Message);
            }
        }

        [CommandMethod("BLKIMPORT", CommandFlags.Modal)]
        public static void ImportBlocks()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor editor = doc.Editor;
            try
            {
                string file;
                using (var dialog = new OpenFileDialog())
                {
                    dialog.Title = "选择包含图块的源图纸";
                    dialog.Filter = DrawingReader.FileFilter;
                    if (dialog.ShowDialog(Owner()) != DialogResult.OK) return;
                    file = dialog.FileName;
                }

                var options = new PromptKeywordOptions("\n导入范围 [全部(A)/仅动态块(D)]: ", "A D");
                PromptResult keyword = editor.GetKeywords(options);
                if (keyword.Status != PromptStatus.OK) return;
                bool dynamicOnly = keyword.StringResult == "D";

                string error;
                Database source = DrawingReader.Open(file, out error);
                if (source == null) { Write("打开源图纸失败: " + error); return; }

                try
                {
                    List<string> names = Importer.ListDefinitions(source, dynamicOnly);
                    if (names.Count == 0) { Write("源图纸中没有可导入的块定义。"); return; }
                    Write("源图纸 " + Path.GetFileName(file) + " 中共 " + names.Count.ToString(CultureInfo.InvariantCulture) + " 个块定义，开始导入...");

                    int imported;
                    using (doc.LockDocument())
                    {
                        imported = Importer.ImportDefinitions(source, names, doc.Database, out error);
                    }
                    if (imported > 0) Write("已导入 " + imported.ToString(CultureInfo.InvariantCulture) + " 个块定义到当前图纸（可用 INSERT 插入）。");
                    else Write("导入失败: " + error);
                }
                finally
                {
                    try { source.Dispose(); } catch { }
                }
            }
            catch (System.Exception ex)
            {
                Log.Error("BLKIMPORT 失败", ex);
                Write("导入失败: " + ex.Message);
            }
        }

        [CommandMethod("BLKSETTINGS", CommandFlags.Modal)]
        public static void ShowSettings()
        {
            try
            {
                using (var form = new SettingsForm())
                {
                    AcApp.ShowModalDialog(form);
                }
                PaletteHost.Refresh();
                Write("设置已更新（配置文件: " + LibraryStore.FilePath + "）。");
            }
            catch (System.Exception ex)
            {
                Log.Error("BLKSETTINGS 失败", ex);
                Write("打开设置失败: " + ex.Message);
            }
        }

        [CommandMethod("BLKSELFTEST", CommandFlags.Modal)]
        public static void SelfTestCommand()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor editor = doc.Editor;
            try
            {
                var options = new PromptKeywordOptions("\n自检范围 [仅当前图纸(C)/附加扫描文件夹(F)]: ", "C F");
                PromptResult keyword = editor.GetKeywords(options);
                string folder = string.Empty;
                if (keyword.Status == PromptStatus.OK && keyword.StringResult == "F")
                {
                    folder = GetString(editor, "\n输入要扫描的文件夹（例如 AutoCAD 示例图纸目录）: ");
                }

                string stages = GetString(editor, "\n自检阶段(可组合) [全部=A / 环境=E / 界面=U / 扫描=S / 复制插入=V / 后台=B，直接回车=全部]: ");
                if (string.IsNullOrEmpty(stages)) stages = "A";

                string report = SelfTest.Run(folder, stages);
                Write("\n自检完成，报告已写入: " + AppPaths.SelfTestFile);
                Write(FirstLines(report, 60));
            }
            catch (System.Exception ex)
            {
                Log.Error("BLKSELFTEST 失败", ex);
                Write("自检失败: " + ex.Message);
            }
        }

        [CommandMethod("BLKABOUT", CommandFlags.Modal)]
        public static void About()
        {
            Write("\n=== " + PluginInfo.DisplayName + " v" + PluginInfo.Version + " ===");
            Write("功能: 扫描 dwg / dwt / dws / dxf 图纸中的图块，统计动态块数量，按分类和标签管理，并复制到当前图纸。");
            Write("命令: BLKLIB 块库面板 | BLKSCAN 扫描图纸 | BLKCOUNT 扫描当前图纸 | BLKSTATS 导出CSV | BLKIMPORT 导入块定义");
            Write("      BLKSETTINGS 设置 | BLKSELFTEST 自检 | BLKABOUT 关于");
            Write("配置目录: " + AppPaths.Root);
            Write("日志文件: " + AppPaths.LogFile);
        }

        private static string GetString(Editor editor, string prompt)
        {
            var options = new PromptStringOptions(prompt);
            options.AllowSpaces = true;
            options.UseDefaultValue = false;
            PromptResult result = editor.GetString(options);
            if (result.Status != PromptStatus.OK) return string.Empty;
            string value = (result.StringResult ?? string.Empty).Trim().Trim('"');
            return value;
        }

        /// <summary>把 WinForms 对话框挂到 AutoCAD 主窗口，避免对话框出现在窗口后面。</summary>
        private static IWin32Window Owner()
        {
            try
            {
                Autodesk.AutoCAD.Windows.Window window = AcApp.MainWindow;
                if (window != null && window.Handle != IntPtr.Zero) return new NativeOwner(window.Handle);
            }
            catch { }
            return null;
        }

        private sealed class NativeOwner : IWin32Window
        {
            private readonly IntPtr handle;
            public NativeOwner(IntPtr value) { handle = value; }
            public IntPtr Handle { get { return handle; } }
        }

        private static void Write(string message)
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            try { doc.Editor.WriteMessage("\n" + message); }
            catch { }
        }

        private static string FirstLines(string text, int maxLines)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sb = new StringBuilder();
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length && i < maxLines; i++)
            {
                sb.AppendLine(lines[i]);
            }
            return sb.ToString();
        }
    }
}
