// Codex 图块库 - 单文件安装程序（把内嵌的插件包安装到 AutoCAD 的 ApplicationPlugins 目录）
// 支持图形界面安装，也支持静默安装：Setup.exe /silent [/allusers|/uninstall]
using System;
using System.IO;
using System.Drawing;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CodexBlockLibrary.Setup
{
    internal static class Program
    {
        internal const string BundleName = "CodexBlockLibrary.bundle";
        internal const string PayloadRes = "CodexBlockLibrary.payload.zip";
        internal const string ReadmeRes = "CodexBlockLibrary.readme.md";
        internal const string Version = "1.0.0";

        internal const int ExitOk = 0;
        internal const int ExitFail = 1;

        [STAThread]
        private static void Main(string[] args)
        {
            bool allUsers = false;
            bool auto = false;
            bool silent = false;
            bool uninstall = false;
            bool purge = false;

            foreach (string raw in args)
            {
                string a = raw.TrimStart('-', '/').ToLowerInvariant();
                if (a == "allusers" || a == "machine") allUsers = true;
                else if (a == "auto") auto = true;
                else if (a == "silent" || a == "quiet") silent = true;
                else if (a == "uninstall" || a == "remove") uninstall = true;
                else if (a == "purge") purge = true;
            }

            if (silent)
            {
                Environment.ExitCode = SilentRun(allUsers, uninstall, purge);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm(allUsers, auto));
        }

        /// <summary>静默模式：不打界面，结果写入 %TEMP%\CodexBlockLibrary-setup.log，退出码 0 成功 / 1 失败。</summary>
        private static int SilentRun(bool allUsers, bool uninstall, bool purge)
        {
            StringBuilder log = new StringBuilder();
            log.AppendLine("Codex 图块库静默安装 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            int code = ExitOk;
            string logPath = Path.Combine(Path.GetTempPath(), "CodexBlockLibrary-setup.log");
            try
            {
                if (uninstall) Installer.Uninstall(allUsers, purge, log);
                else Installer.Install(allUsers, log);
            }
            catch (Exception ex)
            {
                log.AppendLine("[X] " + ex.Message);
                code = ExitFail;
            }
            try { File.WriteAllText(logPath, log.ToString(), new UTF8Encoding(true)); } catch { }
            return code;
        }

        internal static bool IsAdmin()
        {
            try
            {
                System.Security.Principal.WindowsIdentity id = System.Security.Principal.WindowsIdentity.GetCurrent();
                System.Security.Principal.WindowsPrincipal p = new System.Security.Principal.WindowsPrincipal(id);
                return p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        internal static bool AcadRunning()
        {
            try { return System.Diagnostics.Process.GetProcessesByName("acad").Length > 0; }
            catch { return false; }
        }

        /// <summary>从注册表尽力找出本机 AutoCAD 安装路径（仅用于提示）。</summary>
        internal static string FindAcad()
        {
            string[] roots = new string[] { @"SOFTWARE\Autodesk\AutoCAD", @"SOFTWARE\WOW6432Node\Autodesk\AutoCAD" };
            foreach (string root in roots)
            {
                try
                {
                    using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(root))
                    {
                        if (k == null) continue;
                        foreach (string ver in k.GetSubKeyNames())
                        {
                            using (Microsoft.Win32.RegistryKey vk = k.OpenSubKey(ver))
                            {
                                if (vk == null) continue;
                                foreach (string prod in vk.GetSubKeyNames())
                                {
                                    using (Microsoft.Win32.RegistryKey pk = vk.OpenSubKey(prod))
                                    {
                                        if (pk == null) continue;
                                        object o = pk.GetValue("AcadLocation");
                                        if (o == null) continue;
                                        string loc = Convert.ToString(o);
                                        if (!string.IsNullOrEmpty(loc) && File.Exists(Path.Combine(loc, "acad.exe"))) return loc;
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
            }
            return null;
        }

        internal static Stream OpenResource(string name)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            foreach (string n in asm.GetManifestResourceNames())
            {
                if (n.EndsWith(name, StringComparison.OrdinalIgnoreCase)) return asm.GetManifestResourceStream(n);
            }
            return null;
        }
    }
}
namespace CodexBlockLibrary.Setup
{
    /// <summary>安装 / 卸载的实际动作，界面模式与静默模式共用。</summary>
    internal static class Installer
    {
        internal static string TargetDir(bool allUsers)
        {
            string baseDir = allUsers
                ? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
                : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(Path.Combine(Path.Combine(baseDir, "Autodesk"), "ApplicationPlugins"), Program.BundleName);
        }

        internal static string ConfigDir()
        {
            return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk"), "CodexBlockLib");
        }

        internal static string InstalledVersion(string dir)
        {
            try
            {
                string xml = Path.Combine(dir, "PackageContents.xml");
                if (!File.Exists(xml)) return null;
                Match m = Regex.Match(File.ReadAllText(xml, Encoding.UTF8), "AppVersion=\"([^\"]+)\"");
                return m.Success ? m.Groups[1].Value : "未知";
            }
            catch { return null; }
        }

        /// <summary>只允许删除 ApplicationPlugins 下的 *.bundle 目录，避免误删其它文件。</summary>
        internal static void SafeDeleteBundle(string dir)
        {
            if (!Directory.Exists(dir)) return;
            string name = Path.GetFileName(dir);
            string parent = Path.GetDirectoryName(dir);
            if (!name.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("拒绝删除非 .bundle 目录：" + dir);
            if (string.IsNullOrEmpty(parent) || !string.Equals(Path.GetFileName(parent), "ApplicationPlugins", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("拒绝删除不在 ApplicationPlugins 下的目录：" + dir);
            Directory.Delete(dir, true);
        }

        private static int ExtractPayload(string destDir)
        {
            using (Stream s = Program.OpenResource(Program.PayloadRes))
            {
                if (s == null) throw new InvalidOperationException("安装程序内部数据缺失，请重新下载安装包。");
                using (ZipArchive zip = new ZipArchive(s, ZipArchiveMode.Read))
                {
                    int count = 0;
                    foreach (ZipArchiveEntry e in zip.Entries)
                    {
                        if (string.IsNullOrEmpty(e.Name)) continue;
                        string rel = e.FullName.Replace('/', Path.DirectorySeparatorChar);
                        if (rel.Contains("..")) throw new InvalidOperationException("安装包内容异常：" + e.FullName);
                        string dst = Path.Combine(destDir, rel);
                        string dir = Path.GetDirectoryName(dst);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                        using (Stream input = e.Open())
                        using (FileStream output = new FileStream(dst, FileMode.Create, FileAccess.Write))
                        {
                            input.CopyTo(output);
                        }
                        count++;
                    }
                    return count;
                }
            }
        }

        internal static void Install(bool allUsers, StringBuilder log)
        {
            if (allUsers && !Program.IsAdmin())
                throw new InvalidOperationException("安装给所有用户需要管理员权限，请以管理员身份运行。");

            string target = TargetDir(allUsers);
            log.AppendLine("安装范围：" + (allUsers ? "本机所有用户" : "当前用户"));
            log.AppendLine("目标目录：" + target);

            if (Program.AcadRunning() && Directory.Exists(target))
            {
                log.AppendLine("[X] AutoCAD 正在运行，旧版本插件的 dll 被占用，无法覆盖。");
                throw new InvalidOperationException("AutoCAD 正在运行，请先关闭 AutoCAD 再安装。");
            }
            if (Program.AcadRunning()) log.AppendLine("[!] AutoCAD 正在运行：安装后需要重启 AutoCAD 才会加载。");

            string parent = Path.GetDirectoryName(target);
            if (!Directory.Exists(parent)) Directory.CreateDirectory(parent);
            if (Directory.Exists(target))
            {
                log.AppendLine("检测到已安装，先移除旧版本...");
                SafeDeleteBundle(target);
            }
            int n = ExtractPayload(target);
            log.AppendLine("已释放 " + n + " 个文件。");

            if (!File.Exists(Path.Combine(target, "PackageContents.xml")))
                throw new InvalidOperationException("安装后校验失败：缺少 PackageContents.xml");
            if (!File.Exists(Path.Combine(target, "Contents\\Windows\\CodexBlockLib.UI.dll")))
                throw new InvalidOperationException("安装后校验失败：缺少 CodexBlockLib.UI.dll");

            log.AppendLine("[OK] 安装完成：" + target);
            log.AppendLine("下一步：启动或重启 AutoCAD，命令行输入 BLKLIB 打开图块库面板。");
        }

        internal static void Uninstall(bool allUsers, bool purgeConfig, StringBuilder log)
        {
            string target = TargetDir(allUsers);
            if (!Directory.Exists(target))
            {
                string other = TargetDir(!allUsers);
                if (!Directory.Exists(other))
                {
                    log.AppendLine("[!] 没有找到已安装的 Codex 图块库。");
                    return;
                }
                log.AppendLine("在另一个位置找到插件：" + other);
                target = other;
            }

            bool isCommon = target.StartsWith(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                StringComparison.OrdinalIgnoreCase);
            if (isCommon && !Program.IsAdmin())
                throw new InvalidOperationException("该副本安装在所有用户目录下，需要管理员权限才能删除。");

            if (Program.AcadRunning())
                throw new InvalidOperationException("AutoCAD 正在运行，插件文件被占用，请先关闭 AutoCAD。");

            SafeDeleteBundle(target);
            log.AppendLine("[OK] 已删除 " + target);

            if (purgeConfig && Directory.Exists(ConfigDir()))
            {
                Directory.Delete(ConfigDir(), true);
                log.AppendLine("[OK] 已删除用户配置 " + ConfigDir());
            }
            log.AppendLine("卸载完成：下次启动 AutoCAD 不会再加载本插件。");
        }
    }
    internal sealed class SetupForm : Form
    {
        private RadioButton userRadio;
        private RadioButton machineRadio;
        private CheckBox purgeCheck;
        private RichTextBox logBox;
        private Button installButton;
        private Button uninstallButton;
        private Button readmeButton;
        private Button closeButton;
        private string acadHome;
        private bool autoInstall;

        internal SetupForm(bool allUsers, bool auto)
        {
            autoInstall = auto && allUsers && Program.IsAdmin();
            acadHome = Program.FindAcad();
            BuildUi();
            if (allUsers && Program.IsAdmin()) machineRadio.Checked = true;
            Shown += delegate
            {
                Hello();
                ShowState();
                if (autoInstall) DoInstall();
            };
        }

        private void BuildUi()
        {
            Text = "Codex 图块库 " + Program.Version + " 安装程序";
            Font = new Font("Microsoft YaHei", 9F, FontStyle.Regular, GraphicsUnit.Point);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(620, 452);
            BackColor = Color.FromArgb(247, 248, 250);

            Label title = new Label();
            title.Text = "Codex 图块库  " + Program.Version;
            title.Font = new Font("Microsoft YaHei", 14F, FontStyle.Bold, GraphicsUnit.Point);
            title.Location = new Point(16, 12);
            title.AutoSize = true;

            Label sub = new Label();
            sub.Text = "AutoCAD 插件：扫描 dwg / dwt / dws / dxf 中的图块，统计动态块数量，按分类和标签管理，并可复制插入到当前图纸。";
            sub.Location = new Point(18, 46);
            sub.Size = new Size(586, 20);
            sub.ForeColor = Color.FromArgb(88, 94, 104);

            GroupBox g = new GroupBox();
            g.Text = "安装位置";
            g.Location = new Point(16, 72);
            g.Size = new Size(588, 104);

            userRadio = new RadioButton();
            userRadio.Text = "安装给当前用户（推荐，不需要管理员权限）";
            userRadio.Location = new Point(14, 22);
            userRadio.AutoSize = true;
            userRadio.Checked = true;

            Label up = new Label();
            up.Text = Installer.TargetDir(false);
            up.Location = new Point(32, 44);
            up.AutoSize = true;
            up.ForeColor = Color.Gray;

            machineRadio = new RadioButton();
            machineRadio.Text = "安装给本机所有用户（需要管理员权限，会弹出 UAC 授权）";
            machineRadio.Location = new Point(14, 68);
            machineRadio.AutoSize = true;

            g.Controls.Add(userRadio);
            g.Controls.Add(up);
            g.Controls.Add(machineRadio);

            purgeCheck = new CheckBox();
            purgeCheck.Text = "卸载时一并删除用户配置（已登记图纸、分类标签、缩略图缓存、日志）";
            purgeCheck.Location = new Point(20, 186);
            purgeCheck.AutoSize = true;

            logBox = new RichTextBox();
            logBox.Location = new Point(16, 212);
            logBox.Size = new Size(588, 152);
            logBox.ReadOnly = true;
            logBox.BackColor = Color.White;
            logBox.BorderStyle = BorderStyle.FixedSingle;
            logBox.ScrollBars = RichTextBoxScrollBars.Vertical;
            logBox.DetectUrls = false;
            logBox.Font = new Font("Microsoft YaHei", 9F, FontStyle.Regular, GraphicsUnit.Point);

            installButton = new Button();
            installButton.Text = "安装插件";
            installButton.Location = new Point(16, 374);
            installButton.Size = new Size(112, 32);
            installButton.Click += delegate { DoInstall(); };

            uninstallButton = new Button();
            uninstallButton.Text = "卸载插件";
            uninstallButton.Location = new Point(136, 374);
            uninstallButton.Size = new Size(112, 32);
            uninstallButton.Click += delegate { DoUninstall(); };

            readmeButton = new Button();
            readmeButton.Text = "查看说明";
            readmeButton.Location = new Point(256, 374);
            readmeButton.Size = new Size(112, 32);
            readmeButton.Click += delegate { OpenReadme(); };

            closeButton = new Button();
            closeButton.Text = "关闭";
            closeButton.Location = new Point(492, 374);
            closeButton.Size = new Size(112, 32);
            closeButton.Click += delegate { Close(); };

            Label footer = new Label();
            footer.Text = "安装后请启动或重启 AutoCAD；命令行输入 BLKLIB 打开面板，菜单栏也有“块库(K)”。";
            footer.Location = new Point(16, 414);
            footer.AutoSize = true;
            footer.ForeColor = Color.FromArgb(110, 116, 126);

            Controls.Add(title);
            Controls.Add(sub);
            Controls.Add(g);
            Controls.Add(purgeCheck);
            Controls.Add(logBox);
            Controls.Add(installButton);
            Controls.Add(uninstallButton);
            Controls.Add(readmeButton);
            Controls.Add(closeButton);
            Controls.Add(footer);

            AcceptButton = installButton;
        }

        private void Append(string text, Color color)
        {
            logBox.SelectionStart = logBox.TextLength;
            logBox.SelectionLength = 0;
            logBox.SelectionColor = color;
            logBox.AppendText(text + Environment.NewLine);
            logBox.SelectionColor = logBox.ForeColor;
            logBox.ScrollToCaret();
        }

        private void Log(string t) { Append(t, Color.FromArgb(32, 34, 40)); }
        private void LogOk(string t) { Append("[OK] " + t, Color.FromArgb(0, 118, 60)); }
        private void LogWarn(string t) { Append("[!] " + t, Color.FromArgb(168, 108, 0)); }
        private void LogErr(string t) { Append("[X] " + t, Color.FromArgb(190, 40, 40)); }

        private void Report(string text)
        {
            using (StringReader reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0) continue;
                    if (line.StartsWith("[OK]")) LogOk(line.Substring(4).Trim());
                    else if (line.StartsWith("[!]")) LogWarn(line.Substring(3).Trim());
                    else if (line.StartsWith("[X]")) LogErr(line.Substring(3).Trim());
                    else Log(line);
                }
            }
        }

        private void Hello()
        {
            Log("欢迎使用 Codex 图块库安装程序。");
            if (!Program.IsAdmin()) Log("当前未以管理员身份运行：只能安装给当前用户。");
            if (acadHome != null) Log("检测到 AutoCAD：" + acadHome);
            else LogWarn("未在注册表中找到 AutoCAD：本插件需要 AutoCAD 2024 或更高版本（完整版，不支持 LT）。");
            if (Program.AcadRunning()) LogWarn("AutoCAD 正在运行：安装或卸载后请重启 AutoCAD 才会生效。");
        }

        private void ShowState()
        {
            string v1 = Installer.InstalledVersion(Installer.TargetDir(false));
            if (v1 != null) Log("检测到当前用户已安装：版本 " + v1);
            string v2 = Installer.InstalledVersion(Installer.TargetDir(true));
            if (v2 != null) Log("检测到所有用户已安装：版本 " + v2);
            if (v1 == null && v2 == null) Log("当前尚未安装 Codex 图块库。");
        }

        private void Elevate()
        {
            try
            {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo();
                psi.FileName = Application.ExecutablePath;
                psi.Arguments = "/allusers /auto";
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                System.Diagnostics.Process.Start(psi);
                Log("已请求管理员权限，安装会在新窗口中继续。");
                Close();
            }
            catch (Exception ex)
            {
                LogErr("提权失败：" + ex.Message);
                MessageBox.Show(this, "未能获得管理员权限。\r\n\r\n可以右键本程序，选择“以管理员身份运行”后重试。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void DoInstall()
        {
            bool allUsers = machineRadio.Checked;
            if (allUsers && !Program.IsAdmin()) { Elevate(); return; }
            Cursor = Cursors.WaitCursor;
            try
            {
                StringBuilder log = new StringBuilder();
                Installer.Install(allUsers, log);
                Report(log.ToString());
                MessageBox.Show(this, "安装完成。\r\n\r\n请启动或重启 AutoCAD，再在命令行输入 BLKLIB 打开图块库面板。", "安装成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Report(ex.Message);
                MessageBox.Show(this, "安装失败：\r\n\r\n" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { Cursor = Cursors.Default; }
        }

        private void DoUninstall()
        {
            bool allUsers = machineRadio.Checked;
            string target = Installer.TargetDir(allUsers);
            if (!Directory.Exists(target))
            {
                string other = Installer.TargetDir(!allUsers);
                if (!Directory.Exists(other))
                {
                    LogWarn("没有找到已安装的 Codex 图块库。");
                    MessageBox.Show(this, "没有找到已安装的 Codex 图块库。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                target = other;
                Log("在另一个位置找到插件：" + target);
            }

            bool isCommon = target.StartsWith(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                StringComparison.OrdinalIgnoreCase);
            if (isCommon && !Program.IsAdmin())
            {
                LogErr("这是“所有用户”安装的副本，需要管理员权限才能删除。");
                MessageBox.Show(this, "该副本安装在所有用户目录下，需要管理员权限。\r\n\r\n请右键本程序，选择“以管理员身份运行”后再卸载。", "需要管理员权限", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string question = "确定要卸载 Codex 图块库吗？\r\n\r\n将删除：\r\n" + target;
            if (purgeCheck.Checked) question += "\r\n\r\n以及用户配置：\r\n" + Installer.ConfigDir();
            if (MessageBox.Show(this, question, "卸载确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                Log("已取消卸载。");
                return;
            }
            Cursor = Cursors.WaitCursor;
            try
            {
                StringBuilder log = new StringBuilder();
                Installer.Uninstall(allUsers, purgeCheck.Checked, log);
                Report(log.ToString());
                MessageBox.Show(this, "卸载完成。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Report(ex.Message);
                MessageBox.Show(this, "卸载失败：\r\n\r\n" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { Cursor = Cursors.Default; }
        }

        private void OpenReadme()
        {
            try
            {
                string path = Path.Combine(Path.GetTempPath(), "CodexBlockLibrary-README.md");
                using (Stream s = Program.OpenResource(Program.ReadmeRes))
                {
                    if (s == null) { LogErr("安装程序内部缺少说明文件。"); return; }
                    using (FileStream f = new FileStream(path, FileMode.Create, FileAccess.Write)) { s.CopyTo(f); }
                }
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
                catch { System.Diagnostics.Process.Start("notepad.exe", "\"" + path + "\""); }
                Log("已打开说明文件：" + path);
            }
            catch (Exception ex) { LogErr("打开说明失败：" + ex.Message); }
        }
    }
}
