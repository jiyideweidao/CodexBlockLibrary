using System;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Input;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.Windows;
using CodexBlockLib.Core;

namespace CodexBlockLib.UI
{
    /// <summary>功能区按钮 -> AutoCAD 命令。</summary>
    internal sealed class RibbonCommandRouter : ICommand
    {
        public static readonly RibbonCommandRouter Instance = new RibbonCommandRouter();

        public bool CanExecute(object parameter) { return true; }

        public event EventHandler CanExecuteChanged { add { } remove { } }

        public void Execute(object parameter)
        {
            string command = parameter as string;
            if (string.IsNullOrEmpty(command)) return;
            try
            {
                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                if (!command.EndsWith(" ") && !command.EndsWith("\n")) command += " ";
                doc.SendStringToExecute(command, true, false, true);
            }
            catch (System.Exception ex)
            {
                Log.Error("功能区命令执行失败: " + command, ex);
            }
        }
    }

    /// <summary>把插件挂到 AutoCAD 功能区（新增“块库”选项卡）与经典菜单栏。</summary>
    internal static class RibbonMenu
    {
        public const string TabId = "CODEX_BLOCKLIB_TAB";
        private const string PanelId = "CODEX_BLOCKLIB_PANEL";
        private const string AddinsPanelId = "CODEX_BLOCKLIB_PANEL_ADDINS";
        private const string MenuGroupName = "CODEXBLOCKLIB";
        private const string MenuTitle = "块库(&K)";

        private static int attempts;
        private static bool ribbonDone;
        private static bool menuDone;
        private static bool addinsDone;
        private static string lastMessage = "尚未尝试";

        public static bool UiBuilt { get { return ribbonDone && menuDone && addinsDone; } }
        public static string LastMessage { get { return lastMessage; } }
        public static int Attempts { get { return attempts; } }

        /// <summary>当前进程是否有图形界面。accoreconsole 无界面，访问功能区会导致进程崩溃，必须跳过。</summary>
        public static bool UiAvailable
        {
            get
            {
                try
                {
                    string name = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
                    if (!string.IsNullOrEmpty(name) && name.IndexOf("accoreconsole", StringComparison.OrdinalIgnoreCase) >= 0) return false;
                }
                catch { }
                return true;
            }
        }

        public static void Build()
        {
            if (!UiAvailable)
            {
                attempts++;
                ribbonDone = true; menuDone = true; addinsDone = true;
                lastMessage = "当前环境无图形界面(accoreconsole)，已跳过功能区/菜单栏挂载";
                return;
            }
            attempts++;
            LibrarySettings settings = LibraryStore.Settings;

            if (!settings.RibbonEnabled) { ribbonDone = true; addinsDone = true; }
            if (!settings.MenuBarEnabled) menuDone = true;

            if (!ribbonDone)
            {
                try
                {
                    if (BuildRibbon(settings)) ribbonDone = true;
                }
                catch (System.Exception ex)
                {
                    Log.Error("创建功能区选项卡失败", ex);
                    ribbonDone = true;
                }
            }

            if (!addinsDone)
            {
                try
                {
                    if (BuildAddinsPanel(settings)) addinsDone = true;
                }
                catch (System.Exception ex)
                {
                    Log.Warn("添加到附加模块选项卡失败: " + ex.Message);
                    addinsDone = true;
                }
            }

            if (!menuDone)
            {
                // 启动瞬间 AutoCAD 可能拒绝 COM 调用(忙/拒绝)，因此不能一失败就永久放弃：
                // 前 60 轮每轮都试，之后降到每 25 轮一次，直到挂上为止（BLKLIB 也可立即重试）。
                if (attempts <= 60 || (attempts % 25) == 0)
                {
                    try
                    {
                        if (BuildMenuBar()) menuDone = true;
                    }
                    catch (System.Exception ex)
                    {
                        Log.Error("创建下拉菜单失败", ex);
                        menuDone = true;
                    }
                }
                if (!menuDone && attempts == 61)
                    WarnOnce("menupending", "经典菜单栏尚未挂上，改为后台低频重试（执行 BLKLIB 可立即重试）");
            }

            if (ribbonDone && addinsDone && menuDone) lastMessage = "已就绪";
            else if (ribbonDone && addinsDone) lastMessage = "功能区/附加模块已就绪，经典菜单栏仍在重试（执行 BLKLIB 可立即重试）";
        }

        public static bool BuildNow()
        {
            Build();
            return UiBuilt;
        }

        /// <summary>强制重试挂载经典菜单栏：启动时若因 COM 忙被拒，可用它自愈（BLKLIB 会调用）。</summary>
        public static void EnsureMenuBar()
        {
            if (!UiAvailable) return;
            try { if (!LibraryStore.Settings.MenuBarEnabled) return; }
            catch { return; }
            if (menuDone && MenuBarHasMenu()) return;
            menuDone = false;
            try
            {
                if (BuildMenuBar()) menuDone = true;
            }
            catch (System.Exception ex)
            {
                Log.Error("重新挂载经典菜单栏失败", ex);
            }
        }

        private static bool MenuBarHasMenu()
        {
            try
            {
                object app = Autodesk.AutoCAD.ApplicationServices.Application.AcadApplication;
                object menuBar = GetProperty(app, "MenuBar");
                return menuBar != null && FindByName(menuBar, MenuTitle) != null;
            }
            catch { return false; }
        }

        private static bool BuildRibbon(LibrarySettings settings)
        {
            RibbonControl ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return false;
            if (FindTab(ribbon, TabId) != null) { lastMessage = "功能区选项卡已存在"; return true; }

            var tab = new RibbonTab();
            tab.Title = "块库";
            tab.Id = TabId;
            tab.KeyTip = "BK";

            var source = new RibbonPanelSource();
            source.Id = PanelId;
            source.Title = "图块库";
            source.Items.Add(CreateButton("块库面板", "BLKLIB ", "浏览其它图纸中的动态块，并复制到当前图纸 (BLKLIB)", "library", true));
            source.Items.Add(CreateButton("扫描图纸", "BLKSCAN ", "扫描 dwg / dwt / dws / dxf 图纸中的图块 (BLKSCAN)", "scan", true));
            source.Items.Add(CreateButton("重新扫描选中", "BLKRESCAN ", "只重新扫描列表里选中的图纸 (BLKRESCAN)", "scan", true));
            source.Items.Add(CreateButton("暂停扫描", "BLKPAUSE ", "暂停 / 继续正在进行的扫描 (BLKPAUSE)", "pause", false));
            source.Items.Add(CreateButton("扫描当前图纸", "BLKCOUNT ", "读取当前打开的图纸并加入块库 (BLKCOUNT)", "scan", true));
            source.Items.Add(CreateButton("导出统计表", "BLKSTATS ", "导出 CSV 统计表 (BLKSTATS)", "export", false));
            source.Items.Add(CreateButton("设置", "BLKSETTINGS ", "分类方式、缩略图、菜单等设置 (BLKSETTINGS)", "settings", false));

            var panel = new RibbonPanel();
            panel.Source = source;
            tab.Panels.Add(panel);
            ribbon.Tabs.Add(tab);
            lastMessage = "功能区选项卡已创建";
            Log.Write("功能区选项卡已创建: " + TabId);
            return true;
        }

        private static bool BuildAddinsPanel(LibrarySettings settings)
        {
            if (!settings.AddToAddinsTab) return true;
            RibbonControl ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return false;
            RibbonTab addins = FindAddinsTab(ribbon);
            if (addins == null) return false;
            if (FindPanel(addins, AddinsPanelId) != null) return true;

            var source = new RibbonPanelSource();
            source.Id = AddinsPanelId;
            source.Title = "Codex 图块库";
            source.Items.Add(CreateButton("块库面板", "BLKLIB ", "浏览其它图纸中的动态块 (BLKLIB)", "library", false));
            source.Items.Add(CreateButton("扫描当前图纸", "BLKCOUNT ", "扫描当前图纸并加入块库 (BLKCOUNT)", "scan", false));

            var panel = new RibbonPanel();
            panel.Source = source;
            addins.Panels.Add(panel);
            Log.Write("已添加到“附加模块”选项卡");
            return true;
        }

        private static RibbonButton CreateButton(string text, string command, string tooltip, string icon, bool large)
        {
            var button = new RibbonButton();
            button.Text = text;
            button.ShowText = true;
            button.ShowImage = true;
            button.Size = large ? RibbonItemSize.Large : RibbonItemSize.Standard;
            button.Orientation = large ? Orientation.Vertical : Orientation.Horizontal;
            button.CommandHandler = RibbonCommandRouter.Instance;
            button.CommandParameter = command;
            button.ToolTip = tooltip;
            try
            {
                System.Windows.Media.ImageSource image = Icons.Make(icon);
                button.Image = image;
                if (large) button.LargeImage = image;
            }
            catch (System.Exception ex)
            {
                Log.Warn("生成图标失败: " + ex.Message);
            }
            return button;
        }

        private static RibbonTab FindTab(RibbonControl ribbon, string id)
        {
            try
            {
                if (ribbon.Tabs == null) return null;
                foreach (RibbonTab tab in ribbon.Tabs)
                {
                    if (tab == null) continue;
                    if (string.Equals(tab.Id, id, StringComparison.OrdinalIgnoreCase)) return tab;
                }
            }
            catch (System.Exception ex)
            {
                Log.Warn("枚举功能区选项卡失败: " + ex.Message);
            }
            return null;
        }

        private static RibbonTab FindAddinsTab(RibbonControl ribbon)
        {
            try
            {
                if (ribbon.Tabs == null) return null;
                foreach (RibbonTab tab in ribbon.Tabs)
                {
                    if (tab == null || tab.Title == null) continue;
                    string title = tab.Title.Trim();
                    if (title == "附加模块" || title.IndexOf("Add-ins", StringComparison.OrdinalIgnoreCase) >= 0
                        || title.IndexOf("Add-Ins", StringComparison.OrdinalIgnoreCase) >= 0) return tab;
                }
                foreach (RibbonTab tab in ribbon.Tabs)
                {
                    if (tab == null || tab.Id == null) continue;
                    if (tab.Id.IndexOf("ADDIN", StringComparison.OrdinalIgnoreCase) >= 0) return tab;
                }
            }
            catch (System.Exception ex)
            {
                Log.Warn("查找“附加模块”选项卡失败: " + ex.Message);
            }
            return null;
        }

        private static RibbonPanel FindPanel(RibbonTab tab, string id)
        {
            try
            {
                if (tab == null || tab.Panels == null) return null;
                foreach (RibbonPanel panel in tab.Panels)
                {
                    if (panel == null || panel.Source == null) continue;
                    if (string.Equals(panel.Source.Id, id, StringComparison.OrdinalIgnoreCase)) return panel;
                }
            }
            catch { }
            return null;
        }

        public static string DescribeRibbon()
        {
            if (!UiAvailable) return "当前环境无图形界面，跳过功能区检查";
            try
            {
                RibbonControl ribbon = ComponentManager.Ribbon;
                if (ribbon == null) return "功能区控件未创建 (ComponentManager.Ribbon = null)";
                RibbonTab tab = FindTab(ribbon, TabId);
                if (tab == null) return "未找到选项卡 " + TabId;
                int panels = tab.Panels == null ? 0 : tab.Panels.Count;
                int items = 0;
                try { if (tab.Panels.Count > 0 && tab.Panels[0].Source != null) items = tab.Panels[0].Source.Items.Count; }
                catch { }
                return "选项卡存在 (Id=" + TabId + ", 标题=" + tab.Title + ", 面板=" + panels + ", 按钮=" + items + ")";
            }
            catch (System.Exception ex)
            {
                return "查询功能区失败: " + ex.Message;
            }
        }

        // ---------- 经典菜单栏（COM 晚绑定，避免额外引用） ----------

        public static bool BuildMenuBar()
        {
            if (!UiAvailable) return false;
            object app = null;
            try { app = Autodesk.AutoCAD.ApplicationServices.Application.AcadApplication; }
            catch (System.Exception ex) { Log.Warn("获取 AcadApplication 失败: " + ex.Message); return false; }
            if (app == null) { WarnOnce("noapp", "AcadApplication 不可用，经典菜单栏稍后重试"); return false; }

            object groups = GetProperty(app, "MenuGroups");
            if (groups == null) { WarnOnce("nogroups", "AutoCAD 菜单组(MenuGroups)本次不可用，经典菜单栏稍后重试"); return false; }

            object group = PickMenuGroup(groups);
            if (group == null) { WarnOnce("nogroup", "没有可用的菜单组，跳过经典菜单栏挂载"); return false; }

            object menus = GetProperty(group, "Menus");
            if (menus == null) { WarnOnce("nomenus", "菜单组 " + NameOf(group) + " 的 Menus 本次不可用，经典菜单栏稍后重试"); return false; }

            object popup = FindByName(menus, MenuTitle);
            bool created = false;
            if (popup == null)
            {
                popup = Invoke(menus, "Add", MenuTitle);
                created = popup != null;
            }
            if (popup == null) { WarnOnce("nopopup", "创建/查找下拉菜单 " + MenuTitle + " 失败（菜单组 " + NameOf(group) + "），经典菜单栏稍后重试"); return false; }

            if (created)
            {
                Invoke(popup, "AddMenuItem", 0, "块库面板(&L)", "BLKLIB ");
                Invoke(popup, "AddMenuItem", 1, "扫描图纸(&S)...", "BLKSCAN ");
                Invoke(popup, "AddMenuItem", 2, "重新扫描选中的图纸(&R)", "BLKRESCAN ");
                Invoke(popup, "AddMenuItem", 3, "暂停/继续扫描(&P)", "BLKPAUSE ");
                Invoke(popup, "AddMenuItem", 4, "扫描当前图纸(&C)", "BLKCOUNT ");
                Invoke(popup, "AddMenuItem", 5, "导出统计表(&E)...", "BLKSTATS ");
                Invoke(popup, "AddMenuItem", 6, "导入块定义(&I)...", "BLKIMPORT ");
                Invoke(popup, "AddMenuItem", 7, "设置(&O)...", "BLKSETTINGS ");
                Invoke(popup, "AddMenuItem", 8, "自检(&T)", "BLKSELFTEST ");
                Invoke(popup, "AddMenuItem", 9, "关于(&A)", "BLKABOUT ");
                Log.Write("已创建下拉菜单: " + MenuTitle + " (菜单组 " + NameOf(group) + ")");
            }

            object menuBar = GetProperty(app, "MenuBar");
            if (menuBar == null) { WarnOnce("nobar", "AutoCAD 菜单栏(MenuBar)本次不可用，经典菜单栏稍后重试"); return false; }
            if (FindByName(menuBar, MenuTitle) == null)
            {
                int barCount = CountOf(menuBar);
                if (!TryInvoke(popup, "InsertInMenuBar", barCount)) TryInvoke(popup, "InsertInMenuBar", 0);
                if (FindByName(menuBar, MenuTitle) == null)
                { WarnOnce("insertfail", "菜单栏插入未生效: " + MenuTitle + "，稍后重试"); return false; }
                Log.Write("已插入菜单栏: " + MenuTitle);
            }

            if (LibraryStore.Settings.ShowMenuBar) TryShowMenuBar();
            return true;
        }

        /// <summary>挑选一个可写的菜单组：优先 CUSTOM（AutoCAD 专门留给自定义的组），否则第一个非 ACAD 组。</summary>
        private static object PickMenuGroup(object groups)
        {
            object group = FindByName(groups, "CUSTOM");
            if (group == null) group = FindByName(groups, "自定义");
            if (group != null) return group;
            int count = CountOf(groups);
            for (int i = 0; i < count; i++)
            {
                object candidate = Invoke(groups, "Item", i);
                string name = NameOf(candidate);
                if (string.IsNullOrEmpty(name)) continue;
                if (name.Equals("ACAD", StringComparison.OrdinalIgnoreCase)) continue;
                return candidate;
            }
            return count > 0 ? Invoke(groups, "Item", 0) : null;
        }

        private static string NameOf(object comObject)
        {
            object raw = GetProperty(comObject, "Name");
            return raw == null ? string.Empty : raw.ToString();
        }

        /// <summary>AutoCAD 2024 默认隐藏经典菜单栏(MENUBAR=0)，这里按设置打开它。</summary>
        private static void TryShowMenuBar()
        {
            try
            {
                object current = Autodesk.AutoCAD.ApplicationServices.Application.GetSystemVariable("MENUBAR");
                if (current != null && Convert.ToInt32(current) == 1) return;
                Autodesk.AutoCAD.ApplicationServices.Application.SetSystemVariable("MENUBAR", 1);
                Log.Write("已打开经典菜单栏 (MENUBAR=1)");
            }
            catch (System.Exception ex)
            {
                Log.Warn("设置 MENUBAR 失败: " + ex.Message);
            }
        }
        public static bool RemoveMenuBar()
        {
            if (!UiAvailable) return false;
            try
            {
                object app = Autodesk.AutoCAD.ApplicationServices.Application.AcadApplication;
                if (app == null) return false;
                object groups = GetProperty(app, "MenuGroups");
                object group = FindByName(groups, MenuGroupName);
                if (group == null) return false;
                object menus = GetProperty(group, "Menus");
                object popup = FindByName(menus, MenuTitle);
                if (popup != null) TryInvoke(popup, "RemoveFromMenuBar");
                return popup != null;
            }
            catch (System.Exception ex)
            {
                Log.Warn("移除菜单栏失败: " + ex.Message);
                return false;
            }
        }

        public static string DescribeMenuBar()
        {
            if (!UiAvailable) return "当前环境无图形界面，跳过菜单栏检查";
            try
            {
                object app = Autodesk.AutoCAD.ApplicationServices.Application.AcadApplication;
                if (app == null) return "AcadApplication 不可用";
                object groups = GetProperty(app, "MenuGroups");
                if (groups == null) return "MenuGroups 不可用";
                int groupCount = CountOf(groups);
                string foundIn = null;
                for (int i = 0; i < groupCount; i++)
                {
                    object g = Invoke(groups, "Item", i);
                    object menus = GetProperty(g, "Menus");
                    if (menus == null) continue;
                    if (FindByName(menus, MenuTitle) != null) { foundIn = NameOf(g); break; }
                }
                object menuBar = GetProperty(app, "MenuBar");
                int barCount = CountOf(menuBar);
                bool inBar = menuBar != null && FindByName(menuBar, MenuTitle) != null;
                string barText = menuBar == null ? "菜单栏不可用" : ("菜单栏共 " + barCount + " 项, 含“块库”=" + inBar);
                if (foundIn == null) return "未找到菜单 " + MenuTitle + " (共 " + groupCount + " 个菜单组), " + barText;
                return "菜单 " + MenuTitle + " 位于菜单组 " + foundIn + ", " + barText;
            }
            catch (System.Exception ex)
            {
                return "查询菜单失败: " + ex.Message;
            }
        }
        private static readonly System.Collections.Generic.HashSet<string> Warned = new System.Collections.Generic.HashSet<string>();

        private static void WarnOnce(string key, string message)
        {
            bool write;
            lock (Warned) { write = Warned.Add(key); }
            if (write) Log.Warn(message);
        }
        // ---------- COM 晚绑定辅助 ----------

        private static object GetProperty(object target, string name)
        {
            if (target == null) return null;
            try { return target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null); }
            catch { return null; }
        }

        private static object Invoke(object target, string name, params object[] args)
        {
            if (target == null) return null;
            try { return target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, args); }
            catch (System.Exception ex) { WarnOnce(name + "|" + ex.Message, "COM 调用失败 " + name + ": " + ex.Message); return null; }
        }

        private static bool TryInvoke(object target, string name, params object[] args)
        {
            if (target == null) return false;
            try { target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, args); return true; }
            catch (System.Exception ex) { WarnOnce(name + "|" + ex.Message, "COM 调用失败 " + name + ": " + ex.Message); return false; }
        }


        private static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("&", string.Empty).Trim();
        }

        private static object FindByName(object collection, string name)
        {
            if (collection == null) return null;
            int count = CountOf(collection);
            for (int i = 0; i < count; i++)
            {
                object item = Invoke(collection, "Item", i);
                if (item == null) continue;
                object raw = GetProperty(item, "Name");
                string text = raw == null ? null : raw.ToString();
                if (text == null) continue;
                if (string.Equals(Normalize(text), Normalize(name), StringComparison.OrdinalIgnoreCase)) return item;
            }
            return null;
        }

        private static int CountOf(object collection)
        {
            object raw = GetProperty(collection, "Count");
            if (raw == null) return 0;
            try { return Convert.ToInt32(raw); }
            catch { return 0; }
        }
    }
}
