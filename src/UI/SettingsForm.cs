using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using CodexBlockLib.Core;

namespace CodexBlockLib.UI
{
    /// <summary>简单单行输入框，用于设置分类名称等。</summary>
    internal static class InputBox
    {
        public static string Show(IWin32Window owner, string title, string prompt, string defaultValue)
        {
            using (var form = new Form())
            {
                form.Text = title;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterParent;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ClientSize = new Size(360, 130);
                form.Font = new Font("Microsoft YaHei", 9F, FontStyle.Regular, GraphicsUnit.Point);

                var label = new Label();
                label.Text = prompt;
                label.Location = new Point(12, 14);
                label.AutoSize = true;

                var textBox = new TextBox();
                textBox.Location = new Point(14, 40);
                textBox.Size = new Size(330, 24);
                textBox.Text = defaultValue ?? string.Empty;
                textBox.SelectAll();

                var ok = new Button();
                ok.Text = "确定";
                ok.Location = new Point(180, 80);
                ok.Size = new Size(78, 28);
                ok.DialogResult = DialogResult.OK;

                var cancel = new Button();
                cancel.Text = "取消";
                cancel.Location = new Point(266, 80);
                cancel.Size = new Size(78, 28);
                cancel.DialogResult = DialogResult.Cancel;

                form.Controls.Add(label);
                form.Controls.Add(textBox);
                form.Controls.Add(ok);
                form.Controls.Add(cancel);
                form.AcceptButton = ok;
                form.CancelButton = cancel;

                if (form.ShowDialog(owner) != DialogResult.OK) return null;
                return textBox.Text.Trim();
            }
        }
    }

    /// <summary>插件设置对话框。</summary>
    public sealed class SettingsForm : Form
    {
        private ComboBox modeBox;
        private NumericUpDown thumbSize;
        private NumericUpDown maxThumbs;
        private NumericUpDown maxProps;
        private CheckBox autoScale;
        private CheckBox countNested;
        private CheckBox ribbonEnabled;
        private CheckBox menuBarEnabled;
        private CheckBox showMenuBar;
        private CheckBox addinsEnabled;
        private CheckBox panelCollapsed;
        private DataGridView rulesGrid;
        private Label cacheLabel;

        public SettingsForm()
        {
            BuildUi();
            LoadFromSettings();
        }

        private void BuildUi()
        {
            Text = "Codex 图块库 - 设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(760, 646);
            Font = new Font("Microsoft YaHei", 9F, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = Color.FromArgb(246, 247, 249);

            var categoryGroup = new GroupBox();
            categoryGroup.Text = "分类方式";
            categoryGroup.Location = new Point(12, 10);
            categoryGroup.Size = new Size(736, 70);
            modeBox = new ComboBox();
            modeBox.DropDownStyle = ComboBoxStyle.DropDownList;
            modeBox.Location = new Point(16, 28);
            modeBox.Width = 180;
            modeBox.Items.AddRange(new object[] { "按名称前缀", "按来源图纸", "按所在文件夹", "按主要图层", "按块类型", "按可见性状态", "按自定义规则", "不分类" });
            var modeHint = new Label();
            modeHint.Text = "提示：分类改变后，面板中的分类树会立即按新方式重新分组；手动设置的分类始终优先。";
            modeHint.Location = new Point(210, 31);
            modeHint.AutoSize = true;
            categoryGroup.Controls.Add(modeBox);
            categoryGroup.Controls.Add(modeHint);

            var scanGroup = new GroupBox();
            scanGroup.Text = "统计与缩略图";
            scanGroup.Location = new Point(12, 88);
            scanGroup.Size = new Size(736, 110);
            autoScale = MakeCheck("按图纸单位自动换算插入比例", 16, 24);
            countNested = MakeCheck("统计嵌套在其它块内的参照", 16, 50);
            thumbSize = MakeNumeric("缩略图尺寸(px)", 380, 22, 32, 256, 16);
            maxThumbs = MakeNumeric("每张图纸缩略图上限", 380, 50, 0, 5000, 50);
            maxProps = MakeNumeric("动态块参数读取上限", 380, 78, 0, 5000, 10);
            scanGroup.Controls.Add(autoScale);
            scanGroup.Controls.Add(countNested);
            scanGroup.Controls.Add(thumbSize);
            scanGroup.Controls.Add((Control)thumbSize.Tag);
            scanGroup.Controls.Add(maxThumbs);
            scanGroup.Controls.Add((Control)maxThumbs.Tag);
            scanGroup.Controls.Add(maxProps);
            scanGroup.Controls.Add((Control)maxProps.Tag);

            var uiGroup = new GroupBox();
            uiGroup.Text = "CAD 界面（重新启动 AutoCAD 后完全生效）";
            uiGroup.Location = new Point(12, 206);
            uiGroup.Size = new Size(736, 112);
            ribbonEnabled = MakeCheck("在功能区创建“块库”选项卡", 16, 24);
            menuBarEnabled = MakeCheck("在经典菜单栏创建“块库”下拉菜单", 16, 50);
            addinsEnabled = MakeCheck("同时在“附加模块”选项卡添加按钮", 380, 24);
            uiGroup.Controls.Add(ribbonEnabled);
            showMenuBar = MakeCheck("自动显示经典菜单栏 (MENUBAR=1)", 380, 50);
            uiGroup.Controls.Add(addinsEnabled);
            uiGroup.Controls.Add(showMenuBar);
            panelCollapsed = MakeCheck("打开面板时默认折叠为窄条（可随时展开）", 16, 76);
            uiGroup.Controls.Add(panelCollapsed);

            var ruleGroup = new GroupBox();
            ruleGroup.Text = "自定义分类规则（分类方式选择“按自定义规则”时生效，从上到下匹配）";
            ruleGroup.Location = new Point(12, 326);
            ruleGroup.Size = new Size(736, 240);
            rulesGrid = new DataGridView();
            rulesGrid.Location = new Point(14, 24);
            rulesGrid.Size = new Size(706, 170);
            rulesGrid.AllowUserToAddRows = true;
            rulesGrid.RowHeadersVisible = false;
            rulesGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            rulesGrid.Columns.Add("pattern", "块名包含（或正则）");
            rulesGrid.Columns.Add("category", "归入分类");
            var regexColumn = new DataGridViewCheckBoxColumn();
            regexColumn.Name = "regex";
            regexColumn.HeaderText = "用正则";
            regexColumn.FillWeight = 30;
            rulesGrid.Columns.Add(regexColumn);
            var ruleHint = new Label();
            ruleHint.Text = "示例：匹配内容 WD- 归入 门；匹配内容 ^ELE-.* 用正则 归入 电气。";
            ruleHint.Location = new Point(14, 200);
            ruleHint.AutoSize = true;
            ruleGroup.Controls.Add(rulesGrid);
            ruleGroup.Controls.Add(ruleHint);

            var saveButton = new Button();
            saveButton.Text = "保存";
            saveButton.Location = new Point(470, 578);
            saveButton.Size = new Size(84, 30);
            saveButton.Click += delegate { SaveAndClose(); };
            var cancelButton = new Button();
            cancelButton.Text = "取消";
            cancelButton.Location = new Point(562, 578);
            cancelButton.Size = new Size(84, 30);
            cancelButton.DialogResult = DialogResult.Cancel;
            var openFolderButton = new Button();
            openFolderButton.Text = "打开配置目录";
            openFolderButton.Location = new Point(12, 578);
            openFolderButton.Size = new Size(120, 30);
            openFolderButton.Click += delegate { OpenConfigFolder(); };
            var clearCacheButton = new Button();
            clearCacheButton.Text = "清理缩略图缓存";
            clearCacheButton.Location = new Point(140, 578);
            clearCacheButton.Size = new Size(130, 30);
            clearCacheButton.Click += delegate { ClearCache(); };
            cacheLabel = new Label();
            cacheLabel.Location = new Point(280, 584);
            cacheLabel.AutoSize = true;

            Controls.Add(categoryGroup);
            Controls.Add(scanGroup);
            Controls.Add(uiGroup);
            Controls.Add(ruleGroup);
            Controls.Add(saveButton);
            Controls.Add(cancelButton);
            Controls.Add(openFolderButton);
            Controls.Add(clearCacheButton);
            Controls.Add(cacheLabel);

            AcceptButton = saveButton;
            CancelButton = cancelButton;
            UpdateCacheLabel();
        }

        private CheckBox MakeCheck(string text, int x, int y)
        {
            var box = new CheckBox();
            box.Text = text;
            box.Location = new Point(x, y);
            box.AutoSize = true;
            return box;
        }

        private NumericUpDown MakeNumeric(string text, int x, int y, int min, int max, int increment)
        {
            var label = new Label();
            label.Text = text + ":";
            label.Location = new Point(x, y + 4);
            label.AutoSize = true;
            var numeric = new NumericUpDown();
            numeric.Location = new Point(x + 150, y);
            numeric.Width = 80;
            numeric.Minimum = min;
            numeric.Maximum = max;
            numeric.Increment = increment;
            numeric.Tag = label;
            label.Tag = numeric;
            return numeric;
        }
        private void LoadFromSettings()
        {
            LibrarySettings settings = LibraryStore.Settings;
            int index = 0;
            switch (settings.Mode)
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
            autoScale.Checked = settings.AutoScaleByUnits;
            countNested.Checked = settings.CountNested;
            ribbonEnabled.Checked = settings.RibbonEnabled;
            menuBarEnabled.Checked = settings.MenuBarEnabled;
            addinsEnabled.Checked = settings.AddToAddinsTab;
            panelCollapsed.Checked = settings.PanelCollapsed;

            decimal thumb = settings.ThumbSize >= 32 ? settings.ThumbSize : 96;
            if (thumb > thumbSize.Maximum) thumb = thumbSize.Maximum;
            thumbSize.Value = thumb;
            decimal maxThumb = settings.MaxThumbnailsPerFile > 0 ? settings.MaxThumbnailsPerFile : 400;
            if (maxThumb > maxThumbs.Maximum) maxThumb = maxThumbs.Maximum;
            maxThumbs.Value = maxThumb;
            decimal maxProperty = settings.MaxPropertyReadsPerBlock >= 0 ? settings.MaxPropertyReadsPerBlock : 60;
            if (maxProperty > maxProps.Maximum) maxProperty = maxProps.Maximum;
            maxProps.Value = maxProperty;

            try
            {
                rulesGrid.Rows.Clear();
                foreach (CategoryRule rule in settings.Rules)
                {
                    if (rule == null) continue;
                    rulesGrid.Rows.Add(rule.Pattern, rule.Category, rule.UseRegex);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("载入分类规则失败: " + ex.Message);
            }
        }

        private void SaveAndClose()
        {
            try
            {
                LibrarySettings settings = LibraryStore.Settings;
                switch (modeBox.SelectedIndex)
                {
                    case 0: settings.Mode = CategoryMode.Prefix; break;
                    case 1: settings.Mode = CategoryMode.File; break;
                    case 2: settings.Mode = CategoryMode.Folder; break;
                    case 3: settings.Mode = CategoryMode.Layer; break;
                    case 4: settings.Mode = CategoryMode.Type; break;
                    case 5: settings.Mode = CategoryMode.Visibility; break;
                    case 6: settings.Mode = CategoryMode.Rule; break;
                    default: settings.Mode = CategoryMode.None; break;
                }
                settings.AutoScaleByUnits = autoScale.Checked;
                settings.CountNested = countNested.Checked;
                settings.RibbonEnabled = ribbonEnabled.Checked;
                settings.MenuBarEnabled = menuBarEnabled.Checked;
                settings.AddToAddinsTab = addinsEnabled.Checked;
                settings.PanelCollapsed = panelCollapsed.Checked;
                settings.ThumbSize = (int)thumbSize.Value;
                settings.MaxThumbnailsPerFile = (int)maxThumbs.Value;
                settings.MaxPropertyReadsPerBlock = (int)maxProps.Value;

                settings.Rules.Clear();
                foreach (DataGridViewRow row in rulesGrid.Rows)
                {
                    if (row == null || row.IsNewRow) continue;
                    string pattern = Convert.ToString(row.Cells[0].Value, CultureInfo.InvariantCulture);
                    string category = Convert.ToString(row.Cells[1].Value, CultureInfo.InvariantCulture);
                    if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(category)) continue;
                    var rule = new CategoryRule();
                    rule.Pattern = pattern.Trim();
                    rule.Category = category.Trim();
                    object regex = row.Cells[2].Value;
                    rule.UseRegex = regex != null && Convert.ToBoolean(regex, CultureInfo.InvariantCulture);
                    settings.Rules.Add(rule);
                }

                LibraryStore.Save();
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                Log.Error("保存设置失败", ex);
                MessageBox.Show(this, "保存失败: " + ex.Message, "Codex 图块库");
            }
        }

        private void OpenConfigFolder()
        {
            try
            {
                AppPaths.Ensure();
                System.Diagnostics.Process.Start("explorer.exe", "\"" + AppPaths.Root + "\"");
            }
            catch (Exception ex)
            {
                Log.Warn("打开配置目录失败: " + ex.Message);
            }
        }

        private void UpdateCacheLabel()
        {
            try
            {
                long bytes = ThumbCache.CacheSizeBytes();
                cacheLabel.Text = "缩略图缓存: " + (bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
            }
            catch
            {
                cacheLabel.Text = string.Empty;
            }
        }

        private void ClearCache()
        {
            if (MessageBox.Show(this, "确定要清理缩略图缓存吗？下次扫描时会重新生成。", "Codex 图块库",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            ThumbCache.ClearAll();
            UpdateCacheLabel();
            SetStatusHint("缩略图缓存已清理");
        }

        private void SetStatusHint(string text)
        {
            cacheLabel.Text = text;
        }
    }
}
