using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using CodexBlockLib.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CodexBlockLib.UI
{
    /// <summary>块库面板：扫描其它图纸 -> 分类/打标签 -> 复制粘贴到当前图纸。</summary>
    public sealed class MainPalette : UserControl
    {
        private sealed class BlockRow
        {
            public FileScanResult Result;
            public BlockInfo Info;
            public ListViewItem Item;
        }

        private readonly List<BlockRow> rows = new List<BlockRow>();
        private readonly List<BlockRow> shown = new List<BlockRow>();
        private readonly Dictionary<string, int> thumbIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private ImageList thumbnails;
        private TreeView tree;
        private ListView list;
        private DataGridView statsGrid;
        private TabControl tabs;
        private ToolStrip toolStrip;
        private ToolStripStatusLabel statusLabel;
        private ToolStripProgressBar progressBar;
        private ToolStripTextBox searchBox;
        private ToolStripComboBox modeBox;
        private ToolStripButton dynamicOnlyButton;
        private ToolStripButton closeButton;
        private ToolStripButton statusCloseButton;
        private ToolStripButton foldButton;
        private ToolStripButton uninstallButton;
        private PictureBox previewBox;
        private Label detailLabel;
        private ComboBox categoryBox;
        private TextBox tagBox;
        private Button applyMetaButton;
        private Button insertButton;
        private Button pasteButton;
        private Button importButton;
        private Button copyNameButton;
        private SplitContainer outerSplit;

        private string filterKind = "all";
        private string filterValue = string.Empty;
        private bool refreshing;
        private bool scanning;
        private bool thumbLoaderRunning;
        private bool replaceAllRequested = true;
        private int scanSeq;
        private bool started;
        private bool userMovedOuterSplit;

        public MainPalette()
        {
            BuildUi();
            LibraryStore.Load();
            ScanSession.Changed += OnSessionChanged;
            LoadSettingsIntoUi();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (started) return;
            started = true;
            try { BeginInvoke(new Action(InitialLoad)); } catch (System.Exception ex) { Log.Warn("面板首次加载失败: " + ex.Message); }
        }

        private void InitialLoad()
        {
            if (IsDisposed) return;
            try
            {
                ReloadFromSession();
                if (ScanSession.Count == 0 && LibraryStore.Settings.SourceFiles.Count > 0) StartScan(AllRegisteredFiles(), true);
            }
            catch (System.Exception ex)
            {
                Log.Error("面板初始化失败", ex);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) ScanSession.Changed -= OnSessionChanged;
            base.Dispose(disposing);
        }
        // ------------------------------------------------------------------ 界面搭建

        private void BuildUi()
        {
            SuspendLayout();
            Font = new System.Drawing.Font("Microsoft YaHei", 9F, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = Color.FromArgb(246, 247, 249);
            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;

            toolStrip = new ToolStrip();
            toolStrip.Dock = DockStyle.Top;
            toolStrip.GripStyle = ToolStripGripStyle.Hidden;
            toolStrip.RenderMode = ToolStripRenderMode.System;
            toolStrip.Items.Add(MakeButton("添加图纸", "library", delegate { AddFilesDialog(); }));
            toolStrip.Items.Add(MakeButton("添加文件夹", "library", delegate { AddFolderDialog(); }));
            toolStrip.Items.Add(MakeButton("重新扫描", "scan", delegate { StartScan(AllRegisteredFiles(), true); }));
            toolStrip.Items.Add(MakeButton("统计当前图纸", "stats", delegate { ScanCurrentDrawing(); }));
            toolStrip.Items.Add(new ToolStripSeparator());
            toolStrip.Items.Add(MakeButton("移除", "copy", delegate { RemoveSelectedFile(); }));
            toolStrip.Items.Add(MakeButton("清空", "copy", delegate { ClearAll(); }));
            toolStrip.Items.Add(new ToolStripSeparator());
            toolStrip.Items.Add(new ToolStripLabel("分类:"));
            modeBox = new ToolStripComboBox();
            modeBox.DropDownStyle = ComboBoxStyle.DropDownList;
            modeBox.Width = 150;
            modeBox.Items.AddRange(new object[] { "按名称前缀", "按来源图纸", "按所在文件夹", "按主要图层", "按块类型", "按可见性状态", "按自定义规则", "不分类" });
            modeBox.SelectedIndexChanged += delegate { OnModeChanged(); };
            toolStrip.Items.Add(modeBox);
            toolStrip.Items.Add(new ToolStripLabel("搜索:"));
            searchBox = new ToolStripTextBox();
            searchBox.Width = 150;
            searchBox.TextChanged += delegate { RefreshList(); };
            toolStrip.Items.Add(searchBox);
            dynamicOnlyButton = new ToolStripButton("仅动态块");
            dynamicOnlyButton.CheckOnClick = true;
            dynamicOnlyButton.CheckedChanged += delegate { RefreshList(); };
            toolStrip.Items.Add(dynamicOnlyButton);
            toolStrip.Items.Add(new ToolStripSeparator());
            toolStrip.Items.Add(MakeButton("导出统计表", "export", delegate { ExportCsv(); }));
            toolStrip.Items.Add(MakeButton("设置", "settings", delegate { OpenSettings(); }));
            toolStrip.Items.Add(MakeButton("日志", "tag", delegate { OpenLog(); }));
            closeButton = MakeButton("关闭", "close", delegate { ClosePanel(); });
            closeButton.Alignment = ToolStripItemAlignment.Right;
            closeButton.ToolTipText = "关闭图块库面板（快捷键 Esc，重新打开用 BLKLIB）";
            toolStrip.Items.Add(closeButton);

            statusLabel = new ToolStripStatusLabel("就绪");
            progressBar = new ToolStripProgressBar();
            progressBar.Visible = false;
            progressBar.Width = 160;
            var statusStrip = new StatusStrip();
            statusStrip.Dock = DockStyle.Bottom;
            statusStrip.Items.Add(statusLabel);
            statusStrip.Items.Add(progressBar);
            var statusSpring = new ToolStripStatusLabel();
            statusSpring.Spring = true;
            statusStrip.Items.Add(statusSpring);
            foldButton = new ToolStripButton("折叠");
            try { foldButton.Image = Icons.MakeBitmap("fold"); foldButton.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText; }
            catch { }
            foldButton.ToolTipText = "折叠/展开面板（命令 BLKCOLLAPSE）";
            foldButton.Click += delegate { ToggleCollapsed(); };
            statusStrip.Items.Add(foldButton);
            uninstallButton = new ToolStripButton("卸载");
            try { uninstallButton.Image = Icons.MakeBitmap("uninstall"); uninstallButton.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText; }
            catch { }
            uninstallButton.ToolTipText = "卸载插件（AutoCAD 重启后生效，命令 BLKUNINSTALL）";
            uninstallButton.Click += delegate { UninstallPlugin(); };
            statusStrip.Items.Add(uninstallButton);
            statusCloseButton = new ToolStripButton("关闭");
            try { statusCloseButton.Image = Icons.MakeBitmap("close"); statusCloseButton.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText; }
            catch { }
            statusCloseButton.ToolTipText = "关闭图块库面板（快捷键 Esc，重新打开用 BLKLIB）";
            statusCloseButton.Click += delegate { ClosePanel(); };
            statusStrip.Items.Add(statusCloseButton);

            tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            var libraryPage = new TabPage("块库");
            libraryPage.Controls.Add(BuildLibraryPage());
            var statsPage = new TabPage("统计表");
            statsPage.Controls.Add(BuildStatsPage());
            tabs.TabPages.Add(libraryPage);
            tabs.TabPages.Add(statsPage);

            Controls.Add(tabs);
            Controls.Add(statusStrip);
            Controls.Add(toolStrip);
            ResumeLayout(true);
        }

        private ToolStripButton MakeButton(string text, string icon, EventHandler handler)
        {
            var button = new ToolStripButton(text);
            button.DisplayStyle = ToolStripItemDisplayStyle.Text;
            button.Click += handler;
            try
            {
                button.Image = Icons.MakeBitmap(icon);
                button.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            }
            catch { }
            return button;
        }
        /// <summary>折叠/展开：折叠后只保留工具条与状态栏，不占用绘图区。</summary>
        internal void ApplyCollapsedState()
        {
            if (IsDisposed) return;
            try
            {
                bool isCollapsed = PaletteHost.Collapsed;
                if (tabs != null) tabs.Visible = !isCollapsed;
                if (foldButton != null) foldButton.Text = isCollapsed ? "展开" : "折叠";
            }
            catch (System.Exception ex) { Log.Warn("应用折叠状态失败: " + ex.Message); }
        }

        private void ToggleCollapsed()
        {
            try
            {
                PaletteHost.ToggleCollapsed();
                LibraryStore.Settings.PanelCollapsed = PaletteHost.Collapsed;
                LibraryStore.Save();
                SetStatus(PaletteHost.Collapsed ? "面板已折叠，点击“展开”恢复。" : "面板已展开。", 0, 0);
            }
            catch (System.Exception ex) { Log.Warn("切换折叠失败: " + ex.Message); }
        }

        private void UninstallPlugin()
        {
            try { Commands.UninstallPluginInteractive(this); }
            catch (System.Exception ex) { Log.Error("卸载失败", ex); MessageBox.Show(this, ex.Message, "卸载失败"); }
        }

        /// <summary>关闭(隐藏)图块库面板；AutoCAD 中面板关闭后随时可用 BLKLIB 重新打开。</summary>
        private void ClosePanel()
        {
            try { PaletteHost.Hide(); }
            catch (System.Exception ex) { Log.Warn("关闭面板失败: " + ex.Message); }
        }

        /// <summary>面板获得焦点时按 Esc 关闭。</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { ClosePanel(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private Control BuildLibraryPage()
        {
            var page = new Panel();
            page.Dock = DockStyle.Fill;

            outerSplit = new SplitContainer();
            outerSplit.Dock = DockStyle.Fill;
            outerSplit.Orientation = Orientation.Horizontal;
            outerSplit.SplitterWidth = 6;
            outerSplit.Size = new Size(940, 520);
            TrySetMinSizes(outerSplit, 120, 190);

            var innerSplit = new SplitContainer();
            innerSplit.Dock = DockStyle.Fill;
            innerSplit.Orientation = Orientation.Vertical;
            innerSplit.SplitterWidth = 6;
            innerSplit.Size = new Size(560, 520);
            TrySetMinSizes(innerSplit, 120, 220);

            tree = new TreeView();
            tree.Dock = DockStyle.Fill;
            tree.HideSelection = false;
            tree.BorderStyle = BorderStyle.FixedSingle;
            tree.AfterSelect += OnTreeSelect;

            list = new ListView();
            list.Dock = DockStyle.Fill;
            list.View = View.LargeIcon;
            list.MultiSelect = false;
            list.HideSelection = false;
            list.BorderStyle = BorderStyle.FixedSingle;
            list.DoubleClick += delegate { InsertSelected(false); };
            list.SelectedIndexChanged += delegate { UpdateDetails(); };
            list.ContextMenuStrip = BuildContextMenu();

            thumbnails = new ImageList();
            int size = LibraryStore.Settings.ThumbSize >= 32 ? LibraryStore.Settings.ThumbSize : 96;
            thumbnails.ImageSize = new Size(size, size);
            thumbnails.ColorDepth = ColorDepth.Depth32Bit;
            list.LargeImageList = thumbnails;

            innerSplit.Panel1.Controls.Add(tree);
            innerSplit.Panel2.Controls.Add(list);
            outerSplit.Panel1.Controls.Add(innerSplit);
            outerSplit.Panel2.Controls.Add(BuildDetailPanel());
            page.Controls.Add(outerSplit);

            outerSplit.SplitterMoved += delegate { userMovedOuterSplit = true; };
            outerSplit.SizeChanged += delegate
            {
                if (userMovedOuterSplit) return;
                SafeSplit(outerSplit, outerSplit.Height - 236);
            };
            Load += delegate
            {
                SafeSplit(innerSplit, 240);
                SafeSplit(outerSplit, outerSplit.Height - 236);
            };
            return page;
        }

        /// <summary>安全设置面板最小尺寸：SplitContainer 未完成布局时直接赋值会抛 InvalidOperationException。</summary>
        private static void TrySetMinSizes(SplitContainer container, int panel1, int panel2)
        {
            int total = container.Orientation == Orientation.Vertical ? container.Width : container.Height;
            int cap = Math.Max(25, total / 2);
            try { container.Panel1MinSize = Math.Max(0, Math.Min(panel1, cap)); } catch { }
            try { container.Panel2MinSize = Math.Max(0, Math.Min(panel2, cap)); } catch { }
        }

        private static void SafeSplit(SplitContainer container, int distance)
        {
            try
            {
                int total = container.Orientation == Orientation.Vertical ? container.Width : container.Height;
                int max = total - container.Panel2MinSize - container.SplitterWidth;
                if (max > container.Panel1MinSize && distance > container.Panel1MinSize && distance < max) container.SplitterDistance = distance;
            }
            catch { }
        }

        private ContextMenuStrip BuildContextMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("插入到当前图纸", null, delegate { InsertSelected(false); });
            menu.Items.Add("粘贴原样（保留动态参数）", null, delegate { PasteSelected(); });
            menu.Items.Add("仅导入块定义", null, delegate { InsertSelected(true); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("复制块名", null, delegate { CopySelectedName(); });
            menu.Items.Add("设置分类...", null, delegate { AssignCategoryDialog(); });
            menu.Items.Add("编辑标签...", null, delegate { if (tagBox != null) tagBox.Focus(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("重新统计该图纸", null, delegate { RescanSelectedFile(); });
            menu.Items.Add("从库中移除该图纸", null, delegate { RemoveSelectedFile(); });
            return menu;
        }

        private Control BuildDetailPanel()
        {
            var panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = Color.White;
            panel.BorderStyle = BorderStyle.FixedSingle;

            var content = new Panel();
            content.Dock = DockStyle.Fill;
            content.Padding = new Padding(8);
            content.AutoScroll = true;

            var bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 78;

            previewBox = new PictureBox();
            previewBox.Location = new Point(8, 8);
            previewBox.Size = new Size(132, 132);
            previewBox.BorderStyle = BorderStyle.FixedSingle;
            previewBox.BackColor = Color.White;
            previewBox.SizeMode = PictureBoxSizeMode.Zoom;

            detailLabel = new Label();
            detailLabel.Location = new Point(150, 8);
            detailLabel.Size = new Size(560, 132);
            detailLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            detailLabel.AutoSize = false;

            var metaPanel = new Panel();
            metaPanel.Location = new Point(8, 4);
            metaPanel.Size = new Size(700, 30);
            metaPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            var categoryLabel = new Label();
            categoryLabel.Text = "分类:";
            categoryLabel.Location = new Point(0, 6);
            categoryLabel.AutoSize = true;
            categoryBox = new ComboBox();
            categoryBox.Location = new Point(42, 2);
            categoryBox.Width = 150;
            categoryBox.DropDownStyle = ComboBoxStyle.DropDown;
            var tagLabel = new Label();
            tagLabel.Text = "标签:";
            tagLabel.Location = new Point(202, 6);
            tagLabel.AutoSize = true;
            tagBox = new TextBox();
            tagBox.Location = new Point(242, 2);
            tagBox.Width = 300;
            tagBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            applyMetaButton = new Button();
            applyMetaButton.Text = "应用";
            applyMetaButton.Location = new Point(556, 1);
            applyMetaButton.Size = new Size(64, 26);
            applyMetaButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            applyMetaButton.Click += delegate { ApplyMeta(); };
            metaPanel.Controls.Add(categoryLabel);
            metaPanel.Controls.Add(categoryBox);
            metaPanel.Controls.Add(tagLabel);
            metaPanel.Controls.Add(tagBox);
            metaPanel.Controls.Add(applyMetaButton);

            var actionPanel = new Panel();
            actionPanel.Location = new Point(8, 36);
            actionPanel.Size = new Size(700, 38);
            actionPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            insertButton = MakeAction("插入到图纸", 0, 150, delegate { InsertSelected(false); });
            pasteButton = MakeAction("粘贴原样", 158, 140, delegate { PasteSelected(); });
            importButton = MakeAction("仅导入定义", 306, 155, delegate { InsertSelected(true); });
            copyNameButton = MakeAction("复制块名", 469, 140, delegate { CopySelectedName(); });
            actionPanel.Controls.Add(insertButton);
            actionPanel.Controls.Add(pasteButton);
            actionPanel.Controls.Add(importButton);
            actionPanel.Controls.Add(copyNameButton);

            bottom.Controls.Add(metaPanel);
            bottom.Controls.Add(actionPanel);
            content.Controls.Add(previewBox);
            content.Controls.Add(detailLabel);
            panel.Controls.Add(content);
            panel.Controls.Add(bottom);
            return panel;
        }

        private Button MakeAction(string text, int x, int width, EventHandler handler)
        {
            var button = new Button();
            button.Text = text;
            button.Location = new Point(x, 2);
            button.Size = new Size(width, 28);
            button.Click += handler;
            return button;
        }

        private Control BuildStatsPage()
        {
            var page = new Panel();
            page.Dock = DockStyle.Fill;

            var tools = new Panel();
            tools.Dock = DockStyle.Top;
            tools.Height = 34;
            var exportButton = new Button();
            exportButton.Text = "导出 CSV";
            exportButton.Location = new Point(4, 4);
            exportButton.Size = new Size(90, 26);
            exportButton.Click += delegate { ExportCsv(); };
            var currentButton = new Button();
            currentButton.Text = "统计当前图纸";
            currentButton.Location = new Point(100, 4);
            currentButton.Size = new Size(110, 26);
            currentButton.Click += delegate { ScanCurrentDrawing(); };
            var clearButton = new Button();
            clearButton.Text = "清空结果";
            clearButton.Location = new Point(216, 4);
            clearButton.Size = new Size(90, 26);
            clearButton.Click += delegate { ClearAll(); };
            tools.Controls.Add(exportButton);
            tools.Controls.Add(currentButton);
            tools.Controls.Add(clearButton);

            statsGrid = new DataGridView();
            statsGrid.Dock = DockStyle.Fill;
            statsGrid.ReadOnly = true;
            statsGrid.AllowUserToAddRows = false;
            statsGrid.AllowUserToDeleteRows = false;
            statsGrid.RowHeadersVisible = false;
            statsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            statsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            statsGrid.BackgroundColor = Color.White;

            page.Controls.Add(statsGrid);
            page.Controls.Add(tools);
            return page;
        }
        // ------------------------------------------------------------------ 会话 / 列表

        private void OnSessionChanged(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            try { BeginInvoke(new Action(ReloadFromSession)); }
            catch { }
        }

        public void ReloadFromSession()
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(ReloadFromSession)); }
                catch { }
                return;
            }

            List<FileScanResult> results = ScanSession.Results;
            rows.Clear();
            foreach (FileScanResult result in results)
            {
                if (result == null || result.Blocks == null) continue;
                foreach (BlockInfo info in result.Blocks)
                {
                    rows.Add(new BlockRow { Result = result, Info = info });
                }
            }
            RebuildTree();
            RefreshList();
            RefreshStats();
            SetStatus("已载入 " + results.Count.ToString(CultureInfo.InvariantCulture) + " 张图纸 / "
                + rows.Count.ToString(CultureInfo.InvariantCulture) + " 个块定义", 0, 0);
        }

        private void RebuildTree()
        {
            refreshing = true;
            try
            {
                tree.BeginUpdate();
                tree.Nodes.Clear();

                var root = new TreeNode("块库");
                var all = new TreeNode("全部块 (" + rows.Count.ToString(CultureInfo.InvariantCulture) + ")");
                all.Tag = new string[] { "all", string.Empty };
                root.Nodes.Add(all);

                var categories = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var files = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var tags = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var kinds = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int dynamicRows = 0;

                foreach (BlockRow row in rows)
                {
                    AddCount(categories, row.Info.CategoryOrFallback);
                    AddCount(files, row.Result.DisplayName);
                    AddCount(kinds, row.Info.TypeText);
                    foreach (string tag in row.Info.Tags) AddCount(tags, tag);
                    if (row.Info.IsDynamic) dynamicRows++;
                }

                var categoryNode = new TreeNode("分类 (" + categories.Count.ToString(CultureInfo.InvariantCulture) + ")");
                foreach (KeyValuePair<string, int> pair in categories)
                {
                    var node = new TreeNode(pair.Key + " (" + pair.Value.ToString(CultureInfo.InvariantCulture) + ")");
                    node.Tag = new string[] { "category", pair.Key };
                    categoryNode.Nodes.Add(node);
                }

                var fileNode = new TreeNode("来源图纸 (" + files.Count.ToString(CultureInfo.InvariantCulture) + ")");
                foreach (KeyValuePair<string, int> pair in files)
                {
                    var node = new TreeNode(pair.Key + " (" + pair.Value.ToString(CultureInfo.InvariantCulture) + ")");
                    node.Tag = new string[] { "file", pair.Key };
                    fileNode.Nodes.Add(node);
                }

                var tagNode = new TreeNode("标签 (" + tags.Count.ToString(CultureInfo.InvariantCulture) + ")");
                foreach (KeyValuePair<string, int> pair in tags)
                {
                    var node = new TreeNode(pair.Key + " (" + pair.Value.ToString(CultureInfo.InvariantCulture) + ")");
                    node.Tag = new string[] { "tag", pair.Key };
                    tagNode.Nodes.Add(node);
                }

                var kindNode = new TreeNode("类型");
                var dynamicNode = new TreeNode("动态块 (" + dynamicRows.ToString(CultureInfo.InvariantCulture) + ")");
                dynamicNode.Tag = new string[] { "dynamic", string.Empty };
                kindNode.Nodes.Add(dynamicNode);
                foreach (KeyValuePair<string, int> pair in kinds)
                {
                    var node = new TreeNode(pair.Key + " (" + pair.Value.ToString(CultureInfo.InvariantCulture) + ")");
                    node.Tag = new string[] { "type", pair.Key };
                    kindNode.Nodes.Add(node);
                }

                root.Nodes.Add(categoryNode);
                root.Nodes.Add(fileNode);
                root.Nodes.Add(tagNode);
                root.Nodes.Add(kindNode);
                tree.Nodes.Add(root);
                root.Expand();
                categoryNode.Expand();

                TreeNode restore = FindNode(root, filterKind, filterValue);
                tree.SelectedNode = restore != null ? restore : all;
                if (restore == null) { filterKind = "all"; filterValue = string.Empty; }
            }
            catch (Exception ex)
            {
                Log.Error("刷新分类树失败", ex);
            }
            finally
            {
                tree.EndUpdate();
                refreshing = false;
            }
        }

        private static TreeNode FindNode(TreeNode node, string kind, string value)
        {
            string[] tag = node.Tag as string[];
            if (tag != null && tag.Length >= 2 && tag[0] == kind && string.Equals(tag[1], value, StringComparison.OrdinalIgnoreCase)) return node;
            foreach (TreeNode child in node.Nodes)
            {
                TreeNode found = FindNode(child, kind, value);
                if (found != null) return found;
            }
            return null;
        }

        private static void AddCount(SortedDictionary<string, int> map, string key)
        {
            if (string.IsNullOrEmpty(key)) key = "未分类";
            int count;
            if (map.TryGetValue(key, out count)) map[key] = count + 1;
            else map[key] = 1;
        }

        private void OnTreeSelect(object sender, TreeViewEventArgs e)
        {
            if (refreshing || e.Node == null) return;
            string[] tag = e.Node.Tag as string[];
            if (tag != null && tag.Length >= 2)
            {
                filterKind = tag[0];
                filterValue = tag[1];
            }
            else
            {
                filterKind = "all";
                filterValue = string.Empty;
            }
            RefreshList();
        }

        private void RefreshList()
        {
            if (IsDisposed || list == null) return;
            shown.Clear();

            string search = searchBox == null ? string.Empty : (searchBox.Text ?? string.Empty).Trim();
            bool dynamicOnly = dynamicOnlyButton != null && dynamicOnlyButton.Checked;

            foreach (BlockRow row in rows)
            {
                if (dynamicOnly && !row.Info.IsDynamic) continue;
                if (!MatchFilter(row)) continue;
                if (search.Length > 0 && !MatchSearch(row, search)) continue;
                shown.Add(row);
            }

            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                foreach (BlockRow row in shown)
                {
                    var item = new ListViewItem(row.Info.DisplayName);
                    item.ToolTipText = row.Info.TooltipText();
                    item.Tag = row;
                    int index;
                    if (thumbIndex.TryGetValue(row.Info.Key, out index)) item.ImageIndex = index;
                    row.Item = item;
                    list.Items.Add(item);
                }
            }
            catch (Exception ex)
            {
                Log.Error("刷新列表失败", ex);
            }
            finally
            {
                list.EndUpdate();
            }

            UpdateDetails();
            StartThumbnailLoader();
        }

        private bool MatchFilter(BlockRow row)
        {
            switch (filterKind)
            {
                case "category":
                    return string.Equals(row.Info.CategoryOrFallback, filterValue, StringComparison.OrdinalIgnoreCase);
                case "file":
                    return string.Equals(row.Result.DisplayName, filterValue, StringComparison.OrdinalIgnoreCase);
                case "tag":
                    foreach (string tag in row.Info.Tags)
                    {
                        if (string.Equals(tag, filterValue, StringComparison.OrdinalIgnoreCase)) return true;
                    }
                    return false;
                case "type":
                    return string.Equals(row.Info.TypeText, filterValue, StringComparison.OrdinalIgnoreCase);
                case "dynamic":
                    return row.Info.IsDynamic;
                default:
                    return true;
            }
        }

        private static bool MatchSearch(BlockRow row, string search)
        {
            if (row.Info.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (row.Info.CategoryOrFallback.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (row.Result.DisplayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            foreach (string tag in row.Info.Tags)
            {
                if (tag.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            foreach (string state in row.Info.VisibilityStates)
            {
                if (state.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            foreach (DynamicPropertyInfo property in row.Info.Properties)
            {
                if (property.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }
        // ------------------------------------------------------------------ 缩略图

        private void StartThumbnailLoader()
        {
            if (thumbLoaderRunning || shown.Count == 0) return;

            var pending = new List<BlockRow>();
            foreach (BlockRow row in shown)
            {
                if (row.Item == null) continue;
                int index;
                if (thumbIndex.TryGetValue(row.Info.Key, out index)) { row.Item.ImageIndex = index; continue; }
                if (row.Info.IsXref) continue;
                pending.Add(row);
                if (pending.Count >= 300) break;
            }
            if (pending.Count == 0) return;

            int size = LibraryStore.Settings.ThumbSize >= 32 ? LibraryStore.Settings.ThumbSize : 96;
            thumbLoaderRunning = true;
            Task.Factory.StartNew(delegate
            {
                try
                {
                    foreach (BlockRow row in pending)
                    {
                        try
                        {
                            Bitmap bitmap = ThumbCache.Load(row.Info.SourceFile, row.Info.Name);
                            if (bitmap == null && !row.Result.IsCurrentDrawing) bitmap = RenderThumbnailFromFile(row, size);
                            if (bitmap != null)
                            {
                                BlockRow target = row;
                                Bitmap local = bitmap;
                                try { BeginInvoke(new Action(delegate { AddThumbnail(target, local); })); }
                                catch { local.Dispose(); }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warn("生成缩略图失败(" + row.Info.Name + "): " + ex.Message);
                        }
                    }
                }
                finally
                {
                    try { BeginInvoke(new Action(delegate { thumbLoaderRunning = false; })); }
                    catch { thumbLoaderRunning = false; }
                }
            });
        }

        private static Bitmap RenderThumbnailFromFile(BlockRow row, int size)
        {
            string error;
            Database db = DrawingReader.Open(row.Info.SourceFile, out error);
            if (db == null) return null;
            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    if (!table.Has(row.Info.Name)) { tr.Commit(); return null; }
                    ShapeSet shapes = GeometryCapture.CaptureBlock(table[row.Info.Name], tr, db);
                    tr.Commit();
                    if (!shapes.HasData) return null;
                    Bitmap bitmap = ThumbnailRenderer.Render(shapes, size, size);
                    ThumbCache.Save(row.Info.SourceFile, row.Info.Name, bitmap);
                    return bitmap;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("渲染缩略图失败(" + row.Info.Name + "): " + ex.Message);
                return null;
            }
            finally
            {
                try { db.Dispose(); } catch { }
            }
        }

        private void AddThumbnail(BlockRow row, Bitmap bitmap)
        {
            try
            {
                if (IsDisposed || bitmap == null) return;
                int size = thumbnails.ImageSize.Width;
                if (bitmap.Width != size || bitmap.Height != size)
                {
                    var resized = new Bitmap(bitmap, new Size(size, size));
                    bitmap.Dispose();
                    bitmap = resized;
                }
                int index = thumbnails.Images.Count;
                thumbnails.Images.Add(bitmap);
                thumbIndex[row.Info.Key] = index;
                if (row.Item != null) row.Item.ImageIndex = index;
                if (IsSelected(row)) UpdateDetails();
            }
            catch (Exception ex)
            {
                Log.Warn("载入缩略图失败: " + ex.Message);
            }
            finally
            {
                if (bitmap != null) bitmap.Dispose();
            }
        }

        // ------------------------------------------------------------------ 详情 / 分类标签

        private BlockRow SelectedRow()
        {
            if (list == null || list.SelectedItems.Count == 0) return null;
            return list.SelectedItems[0].Tag as BlockRow;
        }

        private bool IsSelected(BlockRow row)
        {
            BlockRow selected = SelectedRow();
            return selected != null && ReferenceEquals(selected, row);
        }

        private void UpdateDetails()
        {
            BlockRow row = SelectedRow();
            if (row == null)
            {
                if (detailLabel != null) detailLabel.Text = "在上方列表中选择一个块，即可查看详情并插入到当前图纸。";
                if (previewBox != null) previewBox.Image = null;
                refreshing = true;
                try
                {
                    if (categoryBox != null) categoryBox.Text = string.Empty;
                    if (tagBox != null) tagBox.Text = string.Empty;
                }
                finally { refreshing = false; }
                return;
            }

            if (detailLabel != null) detailLabel.Text = BuildDetailText(row);
            if (previewBox != null)
            {
                previewBox.Image = null;
                int index;
                if (thumbIndex.TryGetValue(row.Info.Key, out index) && index < thumbnails.Images.Count)
                {
                    previewBox.Image = thumbnails.Images[index];
                }
            }

            refreshing = true;
            try
            {
                if (categoryBox != null)
                {
                    categoryBox.Items.Clear();
                    foreach (string category in LibraryStore.AllUserCategories()) categoryBox.Items.Add(category);
                    categoryBox.Text = row.Info.CategoryOrFallback;
                }
                if (tagBox != null) tagBox.Text = string.Join("; ", row.Info.Tags.ToArray());
            }
            finally
            {
                refreshing = false;
            }

            BlockMeta meta = LibraryStore.Find(row.Info.Key);
            if (meta != null && !string.IsNullOrEmpty(meta.Tags))
            {
                refreshing = true;
                try { if (tagBox != null) tagBox.Text = meta.Tags; }
                finally { refreshing = false; }
            }
        }

        private static string BuildDetailText(BlockRow row)
        {
            BlockInfo info = row.Info;
            var sb = new StringBuilder();
            sb.AppendLine("块名: " + info.Name + "    [" + info.TypeText + "]");
            sb.AppendLine("来源: " + row.Result.DisplayName + "   (" + Path.GetFileName(info.SourceFile) + ")");
            sb.AppendLine("实例: " + info.InstanceCount.ToString(CultureInfo.InvariantCulture)
                + " 个（模型空间 " + info.ModelCount.ToString(CultureInfo.InvariantCulture)
                + " / 图纸空间 " + info.PaperCount.ToString(CultureInfo.InvariantCulture)
                + (info.NestedCount > 0 ? " / 嵌套 " + info.NestedCount.ToString(CultureInfo.InvariantCulture) : string.Empty) + "）");
            if (info.Variants.Count > 0) sb.AppendLine("动态变体: " + info.Variants.Count.ToString(CultureInfo.InvariantCulture) + " 种");
            if (info.VisibilityStatesText.Length > 0) sb.AppendLine("可见性状态: " + info.VisibilityStatesText);
            if (info.Properties.Count > 0)
            {
                sb.AppendLine("动态参数: " + info.Properties.Count.ToString(CultureInfo.InvariantCulture) + " 个");
                int shownCount = 0;
                foreach (DynamicPropertyInfo property in info.Properties)
                {
                    if (shownCount++ >= 4) break;
                    sb.AppendLine("   - " + property.Name + " (" + property.TypeName + ")  " + property.CurrentSummary);
                }
            }
            if (info.LayoutCounts.Count > 0)
            {
                var parts = new List<string>();
                foreach (KeyValuePair<string, int> pair in info.LayoutCounts)
                {
                    parts.Add(pair.Key + " x" + pair.Value.ToString(CultureInfo.InvariantCulture));
                }
                sb.AppendLine("分布: " + string.Join(", ", parts.ToArray()));
            }
            if (info.DefinitionLayers.Count > 0)
            {
                var layers = new List<string>();
                int count = 0;
                foreach (KeyValuePair<string, int> pair in info.DefinitionLayers)
                {
                    layers.Add(pair.Key);
                    if (++count >= 6) break;
                }
                sb.AppendLine("图层: " + string.Join(", ", layers.ToArray()));
            }
            if (info.HasUserMeta) sb.AppendLine("（该块带有自定义分类/标签）");
            return sb.ToString();
        }

        private void ApplyMeta()
        {
            BlockRow row = SelectedRow();
            if (row == null) { SetStatus("请先选择块", 0, 0); return; }

            string category = categoryBox == null ? string.Empty : (categoryBox.Text ?? string.Empty).Trim();
            string tags = tagBox == null ? string.Empty : (tagBox.Text ?? string.Empty).Trim();
            var normalized = new List<string>();
            foreach (string raw in tags.Split(new char[] { ';', ',', '，', '、' }))
            {
                string tag = raw.Trim();
                if (tag.Length > 0 && !normalized.Contains(tag)) normalized.Add(tag);
            }

            try
            {
                LibraryStore.SetCategory(row.Info.Key, category);
                LibraryStore.SetTags(row.Info.Key, string.Join("; ", normalized.ToArray()));
                CategoryRules.Apply(new BlockInfo[] { row.Info }, LibraryStore.Settings);
                RebuildTree();
                RefreshList();
                SetStatus("已保存分类/标签: " + row.Info.Name, 0, 0);
            }
            catch (Exception ex)
            {
                Log.Error("保存分类/标签失败", ex);
                ShowError("保存失败: " + ex.Message);
            }
        }

        private void AssignCategoryDialog()
        {
            BlockRow row = SelectedRow();
            if (row == null) return;
            string value = InputBox.Show(this, "设置分类", "分类名称:", row.Info.CategoryOrFallback);
            if (string.IsNullOrEmpty(value)) return;
            if (categoryBox != null) categoryBox.Text = value;
            ApplyMeta();
        }

        private void CopySelectedName()
        {
            BlockRow row = SelectedRow();
            if (row == null) return;
            try
            {
                Clipboard.SetText(row.Info.Name);
                SetStatus("已复制块名: " + row.Info.Name, 0, 0);
            }
            catch (Exception ex)
            {
                Log.Warn("复制块名失败: " + ex.Message);
            }
        }
        // ------------------------------------------------------------------ 插入 / 粘贴

        private void InsertSelected(bool definitionOnly)
        {
            BlockRow row = SelectedRow();
            if (row == null) { SetStatus("请先选择一个块", 0, 0); return; }

            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) { ShowError("没有打开的图纸。"); return; }

            Database source = null;
            bool ownSource = false;
            string error = null;
            try
            {
                source = ResolveSource(row, doc, out ownSource, out error);
                if (source == null) { ShowError("打开源图纸失败: " + error); return; }

                if (definitionOnly)
                {
                    int imported;
                    using (doc.LockDocument())
                    {
                        imported = Importer.ImportDefinitions(source, new string[] { row.Info.Name }, doc.Database, out error);
                    }
                    if (imported > 0) SetStatus("已导入块定义: " + row.Info.Name + "（可用 INSERT 命令插入）", 0, 0);
                    else ShowError("导入失败: " + error);
                    return;
                }

                double unitScale = 1.0;
                if (LibraryStore.Settings.AutoScaleByUnits && !ReferenceEquals(source, doc.Database))
                {
                    unitScale = UnitConvert.ScaleFactor(source, doc.Database);
                }
                if (unitScale <= 0.0) unitScale = 1.0;

                double scale = unitScale;
                double rotation = 0.0;
                Editor editor = doc.Editor;
                editor.WriteMessage("\n[图块库] 插入 " + row.Info.Name + "，回车/右键结束。");

                while (true)
                {
                    var options = new PromptPointOptions("\n指定插入点 [原样(Y)/比例(S)/旋转(R)/结束(E)]:");
                    options.Keywords.Add("Y");
                    options.Keywords.Add("S");
                    options.Keywords.Add("R");
                    options.Keywords.Add("E");
                    options.AllowNone = true;
                    PromptPointResult result = editor.GetPoint(options);

                    if (result.Status == PromptStatus.Cancel || result.Status == PromptStatus.None) break;
                    if (result.Status == PromptStatus.Keyword)
                    {
                        string keyword = (result.StringResult ?? string.Empty).ToUpperInvariant();
                        if (keyword == "E") break;
                        if (keyword == "Y")
                        {
                            scale = unitScale * (Math.Abs(row.Info.SampleScale) > 1e-9 ? row.Info.SampleScale : 1.0);
                            rotation = row.Info.SampleRotation;
                            SetStatus("已切换为源实例的比例/角度", 0, 0);
                        }
                        else if (keyword == "S")
                        {
                            var prompt = new PromptDoubleOptions("\n输入插入比例 <1>: ");
                            prompt.DefaultValue = 1.0;
                            prompt.UseDefaultValue = true;
                            PromptDoubleResult value = editor.GetDouble(prompt);
                            if (value.Status == PromptStatus.OK && Math.Abs(value.Value) > 1e-9) scale = value.Value;
                        }
                        else if (keyword == "R")
                        {
                            var prompt = new PromptDoubleOptions("\n输入旋转角度(度) <0>: ");
                            prompt.DefaultValue = 0.0;
                            prompt.UseDefaultValue = true;
                            PromptDoubleResult value = editor.GetDouble(prompt);
                            if (value.Status == PromptStatus.OK) rotation = value.Value * Math.PI / 180.0;
                        }
                        continue;
                    }
                    if (result.Status != PromptStatus.OK) break;

                    ObjectId inserted = ObjectId.Null;
                    using (doc.LockDocument())
                    {
                        inserted = Importer.InsertDefinition(source, row.Info.Name, doc.Database, result.Value, scale, rotation, null, out error);
                    }
                    if (inserted.IsNull)
                    {
                        ShowError("插入失败: " + error);
                        break;
                    }
                    SetStatus("已插入 " + row.Info.Name + "（比例 " + TextUtil.FormatNumber(scale) + "）", 0, 0);
                }
            }
            catch (Exception ex)
            {
                Log.Error("插入块失败", ex);
                ShowError("插入失败: " + ex.Message);
            }
            finally
            {
                if (ownSource && source != null) { try { source.Dispose(); } catch { } }
                try { doc.Editor.Regen(); } catch { }
            }
        }

        private void PasteSelected()
        {
            BlockRow row = SelectedRow();
            if (row == null) { SetStatus("请先选择一个块", 0, 0); return; }
            if (string.IsNullOrEmpty(row.Info.SampleRefHandle))
            {
                ShowError("该块在源图纸中没有参照实例，无法原样粘贴，请改用“插入到图纸”。");
                return;
            }

            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) { ShowError("没有打开的图纸。"); return; }

            Database source = null;
            bool ownSource = false;
            string error = null;
            try
            {
                source = ResolveSource(row, doc, out ownSource, out error);
                if (source == null) { ShowError("打开源图纸失败: " + error); return; }

                Editor editor = doc.Editor;
                editor.WriteMessage("\n[图块库] 原样粘贴 " + row.Info.Name + "（保留动态参数），回车/右键结束。");

                while (true)
                {
                    var options = new PromptPointOptions("\n指定粘贴位置 [结束(E)]:");
                    options.Keywords.Add("E");
                    options.AllowNone = true;
                    PromptPointResult result = editor.GetPoint(options);

                    if (result.Status == PromptStatus.Cancel || result.Status == PromptStatus.None) break;
                    if (result.Status == PromptStatus.Keyword) break;
                    if (result.Status != PromptStatus.OK) break;

                    ObjectId created = ObjectId.Null;
                    using (doc.LockDocument())
                    {
                        created = Importer.CloneReference(source, row.Info.SampleRefHandle, doc.Database, doc.Database.CurrentSpaceId,
                            result.Value, false, true, out error);
                    }
                    if (created.IsNull) { ShowError("粘贴失败: " + error); break; }
                    SetStatus("已粘贴 " + row.Info.Name, 0, 0);
                }
            }
            catch (Exception ex)
            {
                Log.Error("粘贴块失败", ex);
                ShowError("粘贴失败: " + ex.Message);
            }
            finally
            {
                if (ownSource && source != null) { try { source.Dispose(); } catch { } }
                try { doc.Editor.Regen(); } catch { }
            }
        }

        private static Database ResolveSource(BlockRow row, Document doc, out bool ownSource, out string error)
        {
            error = string.Empty;
            ownSource = false;
            if (row.Result.IsCurrentDrawing) return doc.Database;
            Database db = DrawingReader.Open(row.Info.SourceFile, out error);
            ownSource = db != null;
            return db;
        }
        // ------------------------------------------------------------------ 扫描

        private List<string> AllRegisteredFiles()
        {
            var files = new List<string>();
            foreach (string file in LibraryStore.Settings.SourceFiles)
            {
                if (File.Exists(file)) files.Add(file);
            }
            return files;
        }

        private void AddFilesDialog()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "选择图纸（可多选）";
                dialog.Filter = DrawingReader.FileFilter;
                dialog.Multiselect = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                foreach (string file in dialog.FileNames) LibraryStore.AddSourceFile(file);
                StartScan(new List<string>(dialog.FileNames), true);
            }
        }

        private void AddFolderDialog()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择包含图纸的文件夹（含子文件夹）";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var found = new List<string>();
                try
                {
                    foreach (string extension in DrawingReader.SupportedExtensions)
                    {
                        found.AddRange(Directory.GetFiles(dialog.SelectedPath, "*" + extension, SearchOption.AllDirectories));
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("枚举文件夹失败: " + ex.Message);
                }
                if (found.Count == 0) { SetStatus("该文件夹下没有找到受支持的图纸文件", 0, 0); return; }
                if (found.Count > 200 && MessageBox.Show(this, "找到 " + found.Count.ToString(CultureInfo.InvariantCulture)
                        + " 个图纸文件，扫描可能较慢，是否继续？", "Codex 图块库", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    return;
                }
                foreach (string file in found) LibraryStore.AddSourceFile(file);
                StartScan(found, false);
            }
        }

        private void StartScan(List<string> files, bool replaceAll)
        {
            if (scanning) { SetStatus("已有扫描任务在进行中", 0, 0); return; }
            if (files == null || files.Count == 0) { SetStatus("没有可扫描的图纸，请先添加图纸", 0, 0); return; }

            scanning = true;
            replaceAllRequested = replaceAll;
            int sequence = ++scanSeq;
            ScanOptions options = LibraryStore.Settings.ToScanOptions();
            var collected = new List<FileScanResult>();

            progressBar.Visible = true;
            progressBar.Value = 0;
            progressBar.Maximum = files.Count;
            SetStatus("开始扫描 " + files.Count.ToString(CultureInfo.InvariantCulture) + " 张图纸...", 0, files.Count);

            Task.Factory.StartNew(delegate
            {
                for (int i = 0; i < files.Count; i++)
                {
                    if (sequence != scanSeq) return collected;
                    string file = files[i];
                    int index = i;
                    Report("正在扫描 (" + (index + 1).ToString(CultureInfo.InvariantCulture) + "/" + files.Count.ToString(CultureInfo.InvariantCulture) + "): "
                        + Path.GetFileName(file), index, files.Count);
                    try
                    {
                        FileScanResult result = BlockScanner.ScanFile(file, options, delegate (ScanProgress progress)
                        {
                            Report(progress.Message + " - " + Path.GetFileName(file), index, files.Count);
                        });
                        try { CategoryRules.Apply(result.Blocks, LibraryStore.Settings); }
                        catch (Exception ex) { Log.Error("应用分类规则失败", ex); }
                        collected.Add(result);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("扫描失败: " + file, ex);
                        var failed = new FileScanResult();
                        failed.Path = file;
                        failed.Label = Path.GetFileName(file);
                        failed.Ok = false;
                        failed.Error = ex.Message;
                        collected.Add(failed);
                    }
                }
                return collected;
            })
            .ContinueWith(delegate (Task<List<FileScanResult>> task)
            {
                List<FileScanResult> results = null;
                bool faulted = task.IsFaulted;
                if (!faulted && task.Result != null) results = task.Result;
                try
                {
                    BeginInvoke(new Action(delegate { OnScanFinished(sequence, results, files, faulted); }));
                }
                catch (Exception ex)
                {
                    Log.Error("回填扫描结果失败", ex);
                }
            });
        }

        private void OnScanFinished(int sequence, List<FileScanResult> results, List<string> files, bool faulted)
        {
            scanning = false;
            progressBar.Visible = false;
            if (sequence != scanSeq) return;

            if (faulted || results == null)
            {
                Log.Warn("后台扫描失败，改为在界面线程同步重试");
                var fallback = new List<FileScanResult>();
                ScanOptions options = LibraryStore.Settings.ToScanOptions();
                foreach (string file in files)
                {
                    try
                    {
                        FileScanResult result = BlockScanner.ScanFile(file, options, null);
                        CategoryRules.Apply(result.Blocks, LibraryStore.Settings);
                        fallback.Add(result);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("同步扫描失败: " + file, ex);
                    }
                }
                results = fallback;
            }

            if (replaceAllRequested) ScanSession.Replace(results);
            else
            {
                foreach (FileScanResult result in results) ScanSession.AddOrReplace(result);
            }
            replaceAllRequested = true;

            int dynamicTotal = 0;
            int failed = 0;
            foreach (FileScanResult result in results)
            {
                if (result == null) continue;
                if (result.Ok) dynamicTotal += result.DynamicInstanceCount;
                else failed++;
            }
            SetStatus("扫描完成: " + results.Count.ToString(CultureInfo.InvariantCulture) + " 张图纸, 动态块实例 "
                + dynamicTotal.ToString(CultureInfo.InvariantCulture) + " 个" + (failed > 0 ? ", 失败 " + failed.ToString(CultureInfo.InvariantCulture) + " 张" : ""), 0, 0);
            ReloadFromSession();
        }

        private void ScanCurrentDrawing()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            try
            {
                SetStatus("正在统计当前图纸...", 0, 0);
                string label = string.IsNullOrEmpty(doc.Name) ? "当前图纸(未保存)" : Path.GetFileName(doc.Name);
                FileScanResult result = BlockScanner.ScanDatabase(doc.Database, doc.Name ?? label, LibraryStore.Settings.ToScanOptions(), null);
                result.Label = label;
                CategoryRules.Apply(result.Blocks, LibraryStore.Settings);
                ScanSession.AddOrReplace(result);
                ReloadFromSession();
                SetStatus("当前图纸: 动态块定义 " + result.DynamicDefinitionCount.ToString(CultureInfo.InvariantCulture)
                    + " 个 / 实例 " + result.DynamicInstanceCount.ToString(CultureInfo.InvariantCulture) + " 个", 0, 0);
            }
            catch (Exception ex)
            {
                Log.Error("统计当前图纸失败", ex);
                ShowError("统计当前图纸失败: " + ex.Message);
            }
        }

        private void RescanSelectedFile()
        {
            BlockRow row = SelectedRow();
            if (row == null || row.Result.IsCurrentDrawing) { ScanCurrentDrawing(); return; }
            StartScan(new List<string>(new string[] { row.Info.SourceFile }), false);
        }

        private void RefreshStats()
        {
            if (statsGrid == null) return;
            try
            {
                statsGrid.Rows.Clear();
                statsGrid.Columns.Clear();
                string[] headers = new string[] { "图纸", "格式", "动态块定义", "动态块实例", "静态块定义", "静态块实例", "外部参照", "单位", "布局", "模型图元", "用时(s)", "状态" };
                foreach (string header in headers) statsGrid.Columns.Add(header, header);

                int dynamicDefinitions = 0, dynamicInstances = 0, staticDefinitions = 0, staticInstances = 0, xrefs = 0;
                foreach (FileScanResult result in ScanSession.Results)
                {
                    if (result == null) continue;
                    dynamicDefinitions += result.DynamicDefinitionCount;
                    dynamicInstances += result.DynamicInstanceCount;
                    staticDefinitions += result.StaticDefinitionCount;
                    staticInstances += result.StaticInstanceCount;
                    xrefs += result.XrefCount;
                    statsGrid.Rows.Add(new object[]
                    {
                        result.DisplayName,
                        result.Format,
                        result.DynamicDefinitionCount,
                        result.DynamicInstanceCount,
                        result.StaticDefinitionCount,
                        result.StaticInstanceCount,
                        result.XrefCount,
                        result.InsUnitsName,
                        result.LayoutCount,
                        result.ModelEntityCount,
                        result.ScanSeconds.ToString("0.00", CultureInfo.InvariantCulture),
                        result.Ok ? "正常" : ("失败: " + result.Error)
                    });
                }
                statsGrid.Rows.Add(new object[]
                {
                    "合计 (" + ScanSession.Count.ToString(CultureInfo.InvariantCulture) + " 张)",
                    string.Empty, dynamicDefinitions, dynamicInstances, staticDefinitions, staticInstances, xrefs,
                    string.Empty, string.Empty, string.Empty, string.Empty, string.Empty
                });
            }
            catch (Exception ex)
            {
                Log.Error("刷新统计表失败", ex);
            }
        }
        // ------------------------------------------------------------------ 其它

        private void RemoveSelectedFile()
        {
            BlockRow row = SelectedRow();
            if (row == null) return;
            if (row.Result.IsCurrentDrawing)
            {
                ScanSession.Remove(row.Result.Path);
                return;
            }
            if (MessageBox.Show(this, "从库中移除图纸 " + row.Result.DisplayName + " ？", "Codex 图块库",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            ScanSession.Remove(row.Result.Path);
            LibraryStore.RemoveSourceFile(row.Result.Path);
        }

        private void ClearAll()
        {
            if (MessageBox.Show(this, "清空所有扫描结果（不会删除图纸文件）？", "Codex 图块库",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            ScanSession.Clear();
        }

        private void ExportCsv()
        {
            try
            {
                List<FileScanResult> results = ScanSession.Results;
                if (results.Count == 0 && AcApp.DocumentManager.MdiActiveDocument != null)
                {
                    ScanCurrentDrawing();
                    results = ScanSession.Results;
                }
                if (results.Count == 0) { SetStatus("没有可导出的数据", 0, 0); return; }
                string path = ReportWriter.WriteCsv(results, ReportWriter.BuildDefaultCsvPath("块库统计"));
                SetStatus("已导出: " + path, 0, 0);
                if (MessageBox.Show(this, "已导出统计表:\n" + path + "\n\n是否打开所在文件夹？", "Codex 图块库",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                {
                    OpenFolder(path);
                }
            }
            catch (Exception ex)
            {
                Log.Error("导出统计表失败", ex);
                ShowError("导出失败: " + ex.Message);
            }
        }

        private void OpenSettings()
        {
            try
            {
                using (var form = new SettingsForm())
                {
                    AcApp.ShowModalDialog(form);
                }
                LoadSettingsIntoUi();
                int size = LibraryStore.Settings.ThumbSize >= 32 ? LibraryStore.Settings.ThumbSize : 96;
                if (thumbnails.ImageSize.Width != size)
                {
                    thumbnails.ImageSize = new Size(size, size);
                    thumbIndex.Clear();
                    thumbnails.Images.Clear();
                }
                foreach (FileScanResult result in ScanSession.Results) CategoryRules.Apply(result.Blocks, LibraryStore.Settings);
                ReloadFromSession();
            }
            catch (Exception ex)
            {
                Log.Error("打开设置失败", ex);
                ShowError("打开设置失败: " + ex.Message);
            }
        }

        private static void OpenLog()
        {
            try
            {
                AppPaths.Ensure();
                if (File.Exists(AppPaths.LogFile)) System.Diagnostics.Process.Start("notepad.exe", "\"" + AppPaths.LogFile + "\"");
                else MessageBox.Show("暂无日志文件: " + AppPaths.LogFile, "Codex 图块库");
            }
            catch (Exception ex)
            {
                Log.Warn("打开日志失败: " + ex.Message);
            }
        }

        private static void OpenFolder(string file)
        {
            try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + file + "\""); }
            catch (Exception ex) { Log.Warn("打开文件夹失败: " + ex.Message); }
        }

        private void LoadSettingsIntoUi()
        {
            if (modeBox == null) return;
            refreshing = true;
            try
            {
                int index = 0;
                switch (LibraryStore.Settings.Mode)
                {
                    case CategoryMode.Prefix: index = 0; break;
                    case CategoryMode.File: index = 1; break;
                    case CategoryMode.Folder: index = 2; break;
                    case CategoryMode.Layer: index = 3; break;
                    case CategoryMode.Type: index = 4; break;
                    case CategoryMode.Visibility: index = 5; break;
                    case CategoryMode.Rule: index = 6; break;
                    default: index = 7; break;
                }
                modeBox.SelectedIndex = index;
            }
            finally
            {
                refreshing = false;
            }
        }

        private void OnModeChanged()
        {
            if (refreshing || modeBox == null || modeBox.SelectedIndex < 0) return;
            CategoryMode mode = CategoryMode.Prefix;
            switch (modeBox.SelectedIndex)
            {
                case 1: mode = CategoryMode.File; break;
                case 2: mode = CategoryMode.Folder; break;
                case 3: mode = CategoryMode.Layer; break;
                case 4: mode = CategoryMode.Type; break;
                case 5: mode = CategoryMode.Visibility; break;
                case 6: mode = CategoryMode.Rule; break;
                case 7: mode = CategoryMode.None; break;
            }
            LibraryStore.Settings.Mode = mode;
            LibraryStore.Save();
            try
            {
                foreach (FileScanResult result in ScanSession.Results) CategoryRules.Apply(result.Blocks, LibraryStore.Settings);
            }
            catch (Exception ex)
            {
                Log.Error("重新分类失败", ex);
            }
            ReloadFromSession();
        }

        private void Report(string message, int current, int total)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(new Action(delegate { SetStatus(message, current, total); })); }
            catch { }
        }

        private void SetStatus(string message, int current, int total)
        {
            if (IsDisposed) return;
            try
            {
                if (statusLabel != null) statusLabel.Text = message;
                if (progressBar != null)
                {
                    progressBar.Visible = total > 0 && current < total;
                    if (total > 0)
                    {
                        progressBar.Maximum = Math.Max(1, total);
                        progressBar.Value = Math.Max(0, Math.Min(total, current));
                    }
                }
            }
            catch { }
        }

        private void ShowError(string message)
        {
            try { MessageBox.Show(this, message, "Codex 图块库", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            catch { }
            SetStatus(message, 0, 0);
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            try
            {
                string[] dropped = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (dropped == null || dropped.Length == 0) return;
                var files = new List<string>();
                foreach (string path in dropped)
                {
                    if (Directory.Exists(path))
                    {
                        foreach (string extension in DrawingReader.SupportedExtensions)
                        {
                            files.AddRange(Directory.GetFiles(path, "*" + extension, SearchOption.AllDirectories));
                        }
                    }
                    else if (DrawingReader.IsSupported(path))
                    {
                        files.Add(path);
                    }
                }
                if (files.Count == 0) { SetStatus("拖入的内容中没有受支持的图纸", 0, 0); return; }
                foreach (string file in files) LibraryStore.AddSourceFile(file);
                StartScan(files, false);
            }
            catch (Exception ex)
            {
                Log.Error("拖放处理失败", ex);
            }
        }
    }
}
