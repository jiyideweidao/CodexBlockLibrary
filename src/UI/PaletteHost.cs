using System;
using System.Drawing;
using Autodesk.AutoCAD.Windows;
using CodexBlockLib.Core;

namespace CodexBlockLib.UI
{
    /// <summary>
    /// AutoCAD 面板 (PaletteSet) 宿主，全局只创建一次。
    /// 注意：面板初始尺寸必须在窗口创建之前设置（PaletteSet.Add 之前）；
    /// 运行期再设置 Size 会被当作物理像素处理，因此用 Dpi 缩放补偿（见 ScaledSize）。
    /// </summary>
    internal static class PaletteHost
    {
        private static readonly Guid PaletteId = new Guid("8F1C2E4A-6B3D-4E77-9C21-5A7D3B9E1F42");
        private const int ExpandedWidth = 1120;
        private const int ExpandedHeight = 740;
        private const int CollapsedHeight = 46;

        private static PaletteSet paletteSet;
        private static MainPalette control;
        private static bool collapsed;
        private static bool stateApplied;

        public static MainPalette Control { get { return control; } }

        public static bool Collapsed { get { return collapsed; } }

        public static bool IsVisible
        {
            get { return paletteSet != null && paletteSet.Visible; }
        }

        public static MainPalette EnsureCreated()
        {
            if (!RibbonMenu.UiAvailable) { Log.Warn("当前环境无图形界面(accoreconsole)，跳过面板创建"); return null; }
            if (paletteSet != null && control != null) return control;
            try
            {
                control = new MainPalette();
                paletteSet = new PaletteSet("Codex 图块库", PaletteId);
                try
                {
                    paletteSet.Style = PaletteSetStyles.ShowPropertiesMenu | PaletteSetStyles.ShowAutoHideButton | PaletteSetStyles.ShowCloseButton;
                }
                catch { }
                paletteSet.MinimumSize = new Size(880, 600);
                paletteSet.Size = new Size(ExpandedWidth, ExpandedHeight);
                try { paletteSet.DockEnabled = DockSides.Left | DockSides.Right; } catch { }
                paletteSet.Add("图块库", control);
                paletteSet.KeepFocus = false;
                Log.Write("面板已创建: Codex 图块库 (初始尺寸 " + ExpandedWidth + "x" + ExpandedHeight + ")");
            }
            catch (System.Exception ex)
            {
                Log.Error("创建面板失败", ex);
                paletteSet = null;
                control = null;
            }
            return control;
        }

        public static void Show()
        {
            EnsureCreated();
            if (paletteSet == null) return;
            try
            {
                paletteSet.Visible = true;
                paletteSet.Activate(0);
                if (!stateApplied)
                {
                    stateApplied = true;
                    collapsed = LibraryStore.Settings.PanelCollapsed;
                    if (collapsed) SetCollapsed(true);
                    else if (control != null) control.ApplyCollapsedState();
                }
            }
            catch (System.Exception ex)
            {
                Log.Warn("显示面板失败: " + ex.Message);
            }
        }

        /// <summary>关闭（隐藏）面板，随时可用 BLKLIB 重新打开。</summary>
        public static void Hide()
        {
            if (paletteSet == null) return;
            try
            {
                paletteSet.Visible = false;
                Log.Write("面板已关闭");
            }
            catch (System.Exception ex)
            {
                Log.Warn("关闭面板失败: " + ex.Message);
            }
        }

        public static void Toggle()
        {
            EnsureCreated();
            if (paletteSet == null) return;
            try
            {
                paletteSet.Visible = !paletteSet.Visible;
                if (paletteSet.Visible) paletteSet.Activate(0);
            }
            catch (System.Exception ex)
            {
                Log.Warn("切换面板失败: " + ex.Message);
            }
        }

        public static void ToggleCollapsed()
        {
            EnsureCreated();
            if (paletteSet == null) return;
            SetCollapsed(!collapsed);
        }

        /// <summary>折叠 = 只保留工具条与状态栏；展开 = 恢复列表/详情区域。</summary>
        public static void SetCollapsed(bool value)
        {
            collapsed = value;
            if (control != null) control.ApplyCollapsedState();
            if (paletteSet == null) return;
            try
            {
                if (value)
                {
                    paletteSet.MinimumSize = new Size(320, 40);
                    paletteSet.Size = ScaledSize(ExpandedWidth, CollapsedHeight);
                }
                else
                {
                    paletteSet.MinimumSize = new Size(880, 600);
                    paletteSet.Size = ScaledSize(ExpandedWidth, ExpandedHeight);
                }
                Log.Write(value ? "面板已折叠" : "面板已展开");
            }
            catch (System.Exception ex)
            {
                Log.Warn("设置面板折叠状态失败: " + ex.Message);
            }
        }

        /// <summary>运行期设置 PaletteSet 尺寸会被当作物理像素，这里按当前 DPI 补偿。</summary>
        private static Size ScaledSize(int width, int height)
        {
            float scale = 1f;
            try { if (control != null && control.DeviceDpi > 0) scale = control.DeviceDpi / 96f; }
            catch { }
            if (scale < 1f) scale = 1f;
            return new Size((int)Math.Round(width * scale), (int)Math.Round(height * scale));
        }

        public static void Refresh()
        {
            if (control == null) return;
            try { control.ReloadFromSession(); }
            catch (System.Exception ex) { Log.Error("刷新面板失败", ex); }
        }

        public static void DisposePalette()
        {
            try
            {
                if (control != null) { control.Dispose(); control = null; }
                if (paletteSet != null) { paletteSet.Dispose(); paletteSet = null; }
            }
            catch (System.Exception ex)
            {
                Log.Warn("释放面板失败: " + ex.Message);
            }
        }
    }
}