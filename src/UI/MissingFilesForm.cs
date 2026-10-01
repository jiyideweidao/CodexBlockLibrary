using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using CodexBlockLib.Core;

namespace CodexBlockLib.UI
{
    /// <summary>修复失效的源图纸路径：重新定位到新位置，或把失效条目清掉。</summary>
    public sealed class MissingFilesForm : Form
    {
        private readonly List<string> files;
        private readonly ListBox listBox;
        private readonly Label hint;
        private readonly Button relocateButton;
        private readonly Button removeButton;
        private readonly Button removeAllButton;
        private string message = string.Empty;

        /// <summary>用户是否改动了源图纸登记（调用方据此决定是否重新扫描）。</summary>
        public bool Changed { get; private set; }

        public MissingFilesForm(List<string> missing)
        {
            files = new List<string>(missing ?? new List<string>());
            Changed = false;

            Text = "修复失效的源图纸路径";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(720, 410);
            Font = new Font("Microsoft YaHei", 9F, FontStyle.Regular, GraphicsUnit.Point);

            hint = new Label();
            hint.Location = new Point(12, 10);
            hint.Size = new Size(696, 48);

            listBox = new ListBox();
            listBox.Location = new Point(12, 62);
            listBox.Size = new Size(696, 292);
            listBox.SelectionMode = SelectionMode.MultiExtended;
            listBox.HorizontalScrollbar = true;
            listBox.IntegralHeight = false;

            relocateButton = new Button();
            relocateButton.Text = "重新定位...";
            relocateButton.Location = new Point(12, 366);
            relocateButton.Size = new Size(104, 30);
            relocateButton.Click += delegate { OnRelocate(); };

            removeButton = new Button();
            removeButton.Text = "移除选中";
            removeButton.Location = new Point(124, 366);
            removeButton.Size = new Size(96, 30);
            removeButton.Click += delegate { OnRemoveSelected(); };

            removeAllButton = new Button();
            removeAllButton.Text = "全部移除";
            removeAllButton.Location = new Point(228, 366);
            removeAllButton.Size = new Size(96, 30);
            removeAllButton.Click += delegate { OnRemoveAll(); };

            var closeButton = new Button();
            closeButton.Text = "关闭";
            closeButton.Location = new Point(630, 366);
            closeButton.Size = new Size(78, 30);
            closeButton.DialogResult = DialogResult.OK;

            Controls.Add(hint);
            Controls.Add(listBox);
            Controls.Add(relocateButton);
            Controls.Add(removeButton);
            Controls.Add(removeAllButton);
            Controls.Add(closeButton);
            AcceptButton = closeButton;
            CancelButton = closeButton;

            Refresh2();
        }

        private void Refresh2()
        {
            listBox.BeginUpdate();
            listBox.Items.Clear();
            foreach (string file in files) listBox.Items.Add(file);
            listBox.EndUpdate();

            bool any = files.Count > 0;
            relocateButton.Enabled = any;
            removeButton.Enabled = any;
            removeAllButton.Enabled = any;

            if (!any)
            {
                hint.Text = string.IsNullOrEmpty(message)
                    ? "所有源图纸路径都有效，无需修复。"
                    : message;
            }
            else
            {
                hint.Text = "共 " + files.Count.ToString(CultureInfo.InvariantCulture)
                    + " 个源图纸路径已失效（文件被移动或删除），插件无法再扫描它们。\r\n"
                    + "可以「重新定位...」指向新位置，或移除失效条目后重新添加。"
                    + (string.IsNullOrEmpty(message) ? string.Empty : "\r\n" + message);
            }
        }

        private void SetMessage(string text)
        {
            message = text;
            Refresh2();
        }

        private void OnRelocate()
        {
            if (listBox.SelectedIndex < 0)
            {
                MessageBox.Show(this, "请先在列表里选中要重新定位的图纸。", "Codex 图块库",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int index = listBox.SelectedIndex;
            string oldPath = files[index];
            string fileName = Path.GetFileName(oldPath);

            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "为「" + fileName + "」指定新位置";
                dialog.Filter = DrawingReader.FileFilter;
                dialog.Multiselect = false;
                dialog.FileName = fileName;
                try
                {
                    string initial = Path.GetDirectoryName(oldPath);
                    if (!string.IsNullOrEmpty(initial) && Directory.Exists(initial)) dialog.InitialDirectory = initial;
                }
                catch { }

                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                string picked = dialog.FileName;
                if (string.IsNullOrEmpty(picked)) return;

                try
                {
                    LibraryStore.RemoveSourceFile(oldPath);
                    LibraryStore.AddSourceFile(picked);
                    LibraryStore.Save();
                }
                catch (Exception ex)
                {
                    Log.Error("重新定位源图纸失败", ex);
                    MessageBox.Show(this, "重新定位失败：" + ex.Message, "Codex 图块库",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                files.RemoveAt(index);
                Changed = true;
                SetMessage("已把「" + fileName + "」重新定位到：" + picked);
            }
        }

        private void OnRemoveSelected()
        {
            var picked = new List<string>();
            foreach (int i in listBox.SelectedIndices) picked.Add(files[i]);
            if (picked.Count == 0)
            {
                MessageBox.Show(this, "请先在列表里选中要移除的条目。", "Codex 图块库",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            RemoveEntries(picked, "选中的 " + picked.Count.ToString(CultureInfo.InvariantCulture) + " 个");
        }

        private void OnRemoveAll()
        {
            if (files.Count == 0) return;
            RemoveEntries(new List<string>(files), "全部 " + files.Count.ToString(CultureInfo.InvariantCulture) + " 个");
        }

        private void RemoveEntries(List<string> picked, string what)
        {
            if (MessageBox.Show(this, "确定移除" + what + "失效条目？\r\n\r\n"
                    + "只会从插件的登记列表里删除，不会删除磁盘上的任何文件。",
                    "Codex 图块库", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            {
                return;
            }

            try
            {
                foreach (string file in picked)
                {
                    LibraryStore.RemoveSourceFile(file);
                    files.Remove(file);
                }
                LibraryStore.Save();
            }
            catch (Exception ex)
            {
                Log.Error("移除失效源图纸失败", ex);
                MessageBox.Show(this, "移除失败：" + ex.Message, "Codex 图块库",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Changed = true;
            SetMessage("已移除 " + picked.Count.ToString(CultureInfo.InvariantCulture) + " 个失效条目。");
        }
    }
}