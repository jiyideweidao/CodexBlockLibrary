using System;
using Autodesk.AutoCAD.Runtime;
using CodexBlockLib.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CodexBlockLib.UI
{
    /// <summary>插件入口：AutoCAD 通过 bundle 自动加载或 NETLOAD 载入。</summary>
    public sealed class BlockLibraryPlugin : IExtensionApplication
    {
        public static bool Initialized { get; private set; }

        private static bool idleHooked;
        private static int idleCalls;

        public void Initialize()
        {
            try
            {
                AppPaths.Ensure();
                Log.Write("=== Codex 图块库 " + PluginInfo.Version + " 加载 (" + typeof(BlockLibraryPlugin).Assembly.Location + ") ===");
                try { LibraryStore.Load(true); }
                catch (System.Exception ex) { Log.Error("读取配置失败", ex); }

                HookIdle();
                Initialized = true;
                Log.Write("初始化完成: 分类方式=" + LibraryStore.Settings.ModeText
                    + ", 已登记源图纸 " + LibraryStore.Settings.SourceFiles.Count.ToString() + " 张, 日志=" + AppPaths.LogFile);
            }
            catch (System.Exception ex)
            {
                Log.Error("插件初始化失败", ex);
            }
        }

        public void Terminate()
        {
            try
            {
                UnhookIdle();
                if (LibraryStore.Settings.MenuBarEnabled) RibbonMenu.RemoveMenuBar();
                Log.Write("插件已卸载");
            }
            catch (System.Exception ex)
            {
                Log.Error("插件卸载失败", ex);
            }
        }

        private static void HookIdle()
        {
            if (idleHooked) return;
            try
            {
                AcApp.Idle += OnIdle;
                idleHooked = true;
            }
            catch (System.Exception ex)
            {
                Log.Warn("注册 Idle 事件失败: " + ex.Message);
            }
        }

        private static void UnhookIdle()
        {
            if (!idleHooked) return;
            try { AcApp.Idle -= OnIdle; }
            catch { }
            idleHooked = false;
        }

        /// <summary>功能区只有在 AutoCAD 完成界面初始化后才存在，因此在 Idle 里循环尝试。</summary>
        private static void OnIdle(object sender, EventArgs e)
        {
            idleCalls++;
            try
            {
                RibbonMenu.Build();
                if (RibbonMenu.UiBuilt || idleCalls > 600) UnhookIdle();
            }
            catch (System.Exception ex)
            {
                Log.Error("创建功能区/菜单失败", ex);
                UnhookIdle();
            }
        }
    }

    public static class PluginInfo
    {
        public const string Version = "1.0.0";
        public const string DisplayName = "Codex 图块库 (CodexBlockLibrary)";
    }
}
