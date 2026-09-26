using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using CodexBlockLib.Core;

namespace CodexBlockLib.UI
{
    /// <summary>
    /// 卸载：删除 ApplicationPlugins 下的插件包（AutoCAD 重启后不再加载），可选删除用户配置。
    /// 被 AutoCAD 占用的 dll 无法当场删除，因此写一个后台脚本等 AutoCAD 退出后再删。
    /// </summary>
    internal static class Uninstaller
    {
        /// <summary>插件包目录：...\Autodesk\ApplicationPlugins\CodexBlockLibrary.bundle</summary>
        public static string BundleDirectory
        {
            get
            {
                try
                {
                    string dir = Path.GetDirectoryName(typeof(Uninstaller).Assembly.Location);
                    if (string.IsNullOrEmpty(dir)) return string.Empty;
                    dir = Path.GetDirectoryName(dir);
                    if (string.IsNullOrEmpty(dir)) return string.Empty;
                    return Path.GetDirectoryName(dir) ?? string.Empty;
                }
                catch { return string.Empty; }
            }
        }

        /// <summary>严格校验目标目录确实是本插件包，避免误删用户其它文件。</summary>
        public static bool Validate(out string bundleDir, out string reason)
        {
            bundleDir = BundleDirectory;
            reason = string.Empty;
            if (string.IsNullOrEmpty(bundleDir)) { reason = "无法定位插件安装目录。"; return false; }
            if (!Directory.Exists(bundleDir)) { reason = "插件安装目录不存在: " + bundleDir; return false; }
            string name = Path.GetFileName(bundleDir);
            if (!name.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase)) { reason = "目录名不是 .bundle，已拒绝删除: " + bundleDir; return false; }
            string parent = Path.GetDirectoryName(bundleDir);
            if (string.IsNullOrEmpty(parent) || !string.Equals(Path.GetFileName(parent), "ApplicationPlugins", StringComparison.OrdinalIgnoreCase))
            { reason = "父目录不是 ApplicationPlugins，已拒绝删除: " + bundleDir; return false; }
            if (!File.Exists(Path.Combine(bundleDir, "PackageContents.xml")))
            { reason = "目录内缺少 PackageContents.xml，可能不是本插件: " + bundleDir; return false; }
            return true;
        }

        public static string Run(bool removeUserData)
        {
            string bundleDir; string reason;
            if (!Validate(out bundleDir, out reason)) return reason;

            int removed = 0;
            string manifest = Path.Combine(bundleDir, "PackageContents.xml");
            try
            {
                File.Delete(manifest);
                removed++;
            }
            catch (System.Exception ex)
            {
                Log.Warn("删除 PackageContents.xml 失败: " + ex.Message);
            }

            removed += TryDeleteTree(bundleDir);

            string userData = removeUserData ? AppPaths.Root : null;
            string script = WriteCleanupScript(bundleDir, userData);
            var text = new StringBuilder();
            text.Append("已移除插件注册信息：AutoCAD 下次启动不再加载本插件（本次已删除 ").Append(removed).Append(" 个文件）。");

            if (script == null)
            {
                text.Append("\n请退出 AutoCAD 后手动删除目录: ").Append(bundleDir);
            }
            else if (TryStartCleanup(script))
            {
                text.Append("\n后台清理已启动：AutoCAD 退出后自动删除目录 ").Append(bundleDir).Append("。");
            }
            else
            {
                text.Append("\n请退出 AutoCAD 后手动执行清理脚本: ").Append(script);
            }

            if (removeUserData) text.Append("\n用户配置（库文件/分类/标签/缩略图缓存）将在清理时一并删除: ").Append(AppPaths.Root);
            return text.ToString();
        }

        private static int TryDeleteTree(string root)
        {
            int count = 0;
            try
            {
                foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    try { File.Delete(file); count++; }
                    catch { }
                }
                string[] dirs = Directory.GetDirectories(root, "*", SearchOption.AllDirectories);
                Array.Sort(dirs, delegate(string a, string b) { return b.Length.CompareTo(a.Length); });
                foreach (string dir in dirs)
                {
                    try { if (Directory.GetFileSystemEntries(dir).Length == 0) Directory.Delete(dir); }
                    catch { }
                }
                try { if (Directory.GetFileSystemEntries(root).Length == 0) Directory.Delete(root); }
                catch { }
            }
            catch (System.Exception ex)
            {
                Log.Warn("清理插件目录失败: " + ex.Message);
            }
            return count;
        }

        private static string WriteCleanupScript(string bundleDir, string userDataDir)
        {
            try
            {
                string path = Path.Combine(Path.GetTempPath(), "CodexBlockLib-uninstall.cmd");
                var sb = new StringBuilder();
                sb.AppendLine("@echo off");
                sb.AppendLine(":wait");
                sb.AppendLine("tasklist /FI \"IMAGENAME eq acad.exe\" 2>nul | find /I \"acad.exe\" >nul");
                sb.AppendLine("if not errorlevel 1 (");
                sb.AppendLine("  ping -n 3 127.0.0.1 >nul");
                sb.AppendLine("  goto wait");
                sb.AppendLine(")");
                sb.AppendLine("rmdir /s /q \"" + bundleDir + "\" 2>nul");
                if (!string.IsNullOrEmpty(userDataDir)) sb.AppendLine("rmdir /s /q \"" + userDataDir + "\" 2>nul");
                sb.AppendLine("del /f /q \"%~f0\" >nul 2>nul");
                File.WriteAllText(path, sb.ToString(), Encoding.Default);
                return path;
            }
            catch (System.Exception ex)
            {
                Log.Warn("写入卸载清理脚本失败: " + ex.Message);
                return null;
            }
        }

        private static bool TryStartCleanup(string scriptPath)
        {
            try
            {
                var info = new ProcessStartInfo();
                info.FileName = "cmd.exe";
                info.Arguments = "/c \"" + scriptPath + "\"";
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.WindowStyle = ProcessWindowStyle.Hidden;
                Process.Start(info);
                return true;
            }
            catch (System.Exception ex)
            {
                Log.Warn("启动卸载清理脚本失败: " + ex.Message);
                return false;
            }
        }
    }
}