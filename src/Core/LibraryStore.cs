using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Serialization;

namespace CodexBlockLib.Core
{
    public enum CategoryMode { Prefix, File, Folder, Layer, Type, Visibility, Rule, None }

    /// <summary>自定义分类规则：块名包含（或匹配正则）指定内容时归入某分类。</summary>
    public sealed class CategoryRule
    {
        public string Pattern = string.Empty;
        public string Category = string.Empty;
        public bool UseRegex;
    }

    /// <summary>用户对单个块的自定义分类与标签。</summary>
    public sealed class BlockMeta
    {
        [XmlAttribute] public string Key = string.Empty;
        [XmlAttribute] public string Category = string.Empty;
        [XmlAttribute] public string Tags = string.Empty;
    }

    public sealed class LibrarySettings
    {
        public CategoryMode Mode = CategoryMode.Prefix;
        public int ThumbSize = 96;
        public int MaxThumbnailsPerFile = 400;
        public bool AutoScaleByUnits = true;
        public bool KeepSourceRotation;
        public bool CountNested = true;
        public bool IncludeXrefs = true;
        public int MaxPropertyReadsPerBlock = 60;
        public bool RibbonEnabled = true;
        public bool MenuBarEnabled = true;
        public bool ShowMenuBar = true;   // 创建菜单时自动把 MENUBAR 设为 1（AutoCAD 2024 默认隐藏经典菜单栏）
        public bool PanelCollapsed = true;   // 面板打开时默认折叠为窄条（只留工具条+状态栏），可在设置中关闭
        public bool AddToAddinsTab = true;
        public List<string> SourceFiles = new List<string>();
        public List<string> UserCategories = new List<string>();
        public List<CategoryRule> Rules = new List<CategoryRule>();

        public ScanOptions ToScanOptions()
        {
            var options = new ScanOptions();
            options.CountNested = CountNested;
            options.IncludeXrefs = IncludeXrefs;
            options.MaxPropertyReadsPerBlock = MaxPropertyReadsPerBlock > 0 ? MaxPropertyReadsPerBlock : 60;
            options.BuildThumbnails = true;
            options.MaxThumbnailsPerFile = MaxThumbnailsPerFile > 0 ? MaxThumbnailsPerFile : 400;
            options.ThumbSize = ThumbSize >= 32 ? ThumbSize : 96;
            options.ReadProperties = true;
            return options;
        }

        public string ModeText
        {
            get
            {
                switch (Mode)
                {
                    case CategoryMode.Prefix: return "按名称前缀";
                    case CategoryMode.File: return "按来源图纸";
                    case CategoryMode.Folder: return "按所在文件夹";
                    case CategoryMode.Layer: return "按主要图层";
                    case CategoryMode.Type: return "按块类型";
                    case CategoryMode.Visibility: return "按可见性状态";
                    case CategoryMode.Rule: return "按自定义规则";
                    default: return "不分类";
                }
            }
        }
    }

    [XmlRoot("CodexBlockLibrary")]
    public sealed class LibraryData
    {
        public LibrarySettings Settings = new LibrarySettings();
        public List<BlockMeta> Items = new List<BlockMeta>();
    }

    /// <summary>library.xml 读写：记录已添加的图纸、自定义分类、标签与设置。</summary>
    public static class LibraryStore
    {
        private static readonly object Sync = new object();
        private static LibraryData data;

        public static string FilePath { get { return AppPaths.LibraryFile; } }

        public static LibraryData Data
        {
            get { Load(); return data; }
        }

        public static LibrarySettings Settings
        {
            get { Load(); return data.Settings; }
        }

        public static void Load(bool force)
        {
            lock (Sync)
            {
                if (data != null && !force) return;
                try
                {
                    AppPaths.Ensure();
                    if (File.Exists(AppPaths.LibraryFile))
                    {
                        var serializer = new XmlSerializer(typeof(LibraryData));
                        using (var stream = new FileStream(AppPaths.LibraryFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            data = (LibraryData)serializer.Deserialize(stream);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("读取 library.xml 失败", ex);
                    data = null;
                }
                if (data == null) data = new LibraryData();
                if (data.Settings == null) data.Settings = new LibrarySettings();
                if (data.Items == null) data.Items = new List<BlockMeta>();
                if (data.Settings.SourceFiles == null) data.Settings.SourceFiles = new List<string>();
                if (data.Settings.UserCategories == null) data.Settings.UserCategories = new List<string>();
                if (data.Settings.Rules == null) data.Settings.Rules = new List<CategoryRule>();
            }
        }

        public static void Load() { Load(false); }

        public static void Save()
        {
            lock (Sync)
            {
                try
                {
                    AppPaths.Ensure();
                    if (data == null) return;
                    var serializer = new XmlSerializer(typeof(LibraryData));
                    using (var stream = new FileStream(AppPaths.LibraryFile, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        serializer.Serialize(stream, data);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("保存 library.xml 失败", ex);
                }
            }
        }

        public static string Key(string file, string block)
        {
            return (file ?? string.Empty) + "|" + (block ?? string.Empty);
        }

        public static BlockMeta Find(string key)
        {
            Load();
            if (string.IsNullOrEmpty(key)) return null;
            foreach (BlockMeta meta in data.Items)
            {
                if (meta != null && string.Equals(meta.Key, key, StringComparison.OrdinalIgnoreCase)) return meta;
            }
            return null;
        }

        public static BlockMeta GetOrCreate(string key)
        {
            Load();
            BlockMeta meta = Find(key);
            if (meta != null) return meta;
            meta = new BlockMeta();
            meta.Key = key;
            data.Items.Add(meta);
            return meta;
        }

        public static void SetCategory(string key, string category)
        {
            BlockMeta meta = GetOrCreate(key);
            meta.Category = category ?? string.Empty;
            if (!string.IsNullOrEmpty(category) && !data.Settings.UserCategories.Contains(category)) data.Settings.UserCategories.Add(category);
            Save();
        }

        public static void SetTags(string key, string tags)
        {
            BlockMeta meta = GetOrCreate(key);
            meta.Tags = tags ?? string.Empty;
            Save();
        }

        public static List<string> AllUserTags()
        {
            Load();
            var result = new List<string>();
            foreach (BlockMeta meta in data.Items)
            {
                if (meta == null || string.IsNullOrEmpty(meta.Tags)) continue;
                foreach (string raw in meta.Tags.Split(';'))
                {
                    string tag = raw.Trim();
                    if (tag.Length > 0 && !result.Contains(tag)) result.Add(tag);
                }
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        public static List<string> AllUserCategories()
        {
            Load();
            var result = new List<string>(data.Settings.UserCategories);
            foreach (BlockMeta meta in data.Items)
            {
                if (meta == null || string.IsNullOrEmpty(meta.Category)) continue;
                if (!result.Contains(meta.Category)) result.Add(meta.Category);
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        public static void AddSourceFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            Load();
            foreach (string existing in data.Settings.SourceFiles)
            {
                if (string.Equals(existing, path, StringComparison.OrdinalIgnoreCase)) return;
            }
            data.Settings.SourceFiles.Add(path);
            Save();
        }

        public static void RemoveSourceFile(string path)
        {
            Load();
            data.Settings.SourceFiles.RemoveAll(delegate(string item)
            {
                return string.Equals(item, path, StringComparison.OrdinalIgnoreCase);
            });
            Save();
        }
    }

    /// <summary>分类与标签规则：自动分类 + 用户自定义覆盖。</summary>
    public static class CategoryRules
    {
        public static void Apply(IEnumerable<BlockInfo> blocks, LibrarySettings settings)
        {
            if (blocks == null) return;
            if (settings == null) settings = LibraryStore.Settings;

            foreach (BlockInfo info in blocks)
            {
                if (info == null) continue;
                info.Tags.Clear();
                foreach (string tag in AutoTags(info, settings))
                {
                    if (tag.Length > 0 && !info.Tags.Contains(tag)) info.Tags.Add(tag);
                }

                info.Category = AutoCategory(info, settings);
                info.HasUserMeta = false;

                BlockMeta meta = LibraryStore.Find(info.Key);
                if (meta != null)
                {
                    bool used = false;
                    if (!string.IsNullOrEmpty(meta.Category)) { info.Category = meta.Category; used = true; }
                    if (!string.IsNullOrEmpty(meta.Tags))
                    {
                        foreach (string raw in meta.Tags.Split(';'))
                        {
                            string tag = raw.Trim();
                            if (tag.Length > 0 && !info.Tags.Contains(tag)) info.Tags.Add(tag);
                        }
                        used = true;
                    }
                    info.HasUserMeta = used;
                }

                if (string.IsNullOrEmpty(info.Category)) info.Category = "未分类";
            }
        }

        public static string AutoCategory(BlockInfo info, LibrarySettings settings)
        {
            if (info == null) return "未分类";
            switch (settings.Mode)
            {
                case CategoryMode.None:
                    return "全部块";
                case CategoryMode.File:
                    return SafeName(Path.GetFileNameWithoutExtension(info.SourceFile));
                case CategoryMode.Folder:
                    {
                        string directory = Path.GetDirectoryName(info.SourceFile);
                        if (string.IsNullOrEmpty(directory)) return "未知位置";
                        string name = Path.GetFileName(directory);
                        return SafeName(string.IsNullOrEmpty(name) ? directory : name);
                    }
                case CategoryMode.Layer:
                    {
                        string layer = TopKey(info.DefinitionLayers);
                        if (string.IsNullOrEmpty(layer)) layer = TopKey(info.LayerCounts);
                        return string.IsNullOrEmpty(layer) ? "未分类" : layer;
                    }
                case CategoryMode.Type:
                    return info.TypeText;
                case CategoryMode.Visibility:
                    {
                        string state = string.Empty;
                        DynamicPropertyInfo property = info.VisibilityProperty;
                        if (property != null)
                        {
                            foreach (KeyValuePair<string, int> pair in property.ValueUsage) { state = pair.Key; break; }
                        }
                        return state.Length == 0 ? "无可见性状态" : state;
                    }
                case CategoryMode.Rule:
                    {
                        string matched = MatchRule(info, settings);
                        if (!string.IsNullOrEmpty(matched)) return matched;
                        return TextUtil.Prefix(info.Name);
                    }
                default:
                    return TextUtil.Prefix(info.Name);
            }
        }

        private static string MatchRule(BlockInfo info, LibrarySettings settings)
        {
            foreach (CategoryRule rule in settings.Rules)
            {
                if (rule == null || string.IsNullOrEmpty(rule.Pattern) || string.IsNullOrEmpty(rule.Category)) continue;
                try
                {
                    if (rule.UseRegex)
                    {
                        if (Regex.IsMatch(info.Name, rule.Pattern, RegexOptions.IgnoreCase)) return rule.Category;
                    }
                    else
                    {
                        if (info.Name.IndexOf(rule.Pattern, StringComparison.OrdinalIgnoreCase) >= 0) return rule.Category;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("分类规则无效: " + rule.Pattern + " (" + ex.Message + ")");
                }
            }
            return string.Empty;
        }

        private static IEnumerable<string> AutoTags(BlockInfo info, LibrarySettings settings)
        {
            var tags = new List<string>();
            tags.Add(info.TypeText);
            string file = Path.GetFileNameWithoutExtension(info.SourceFile);
            if (!string.IsNullOrEmpty(file)) tags.Add(file);

            string directory = Path.GetDirectoryName(info.SourceFile);
            if (!string.IsNullOrEmpty(directory))
            {
                string folder = Path.GetFileName(directory);
                if (!string.IsNullOrEmpty(folder)) tags.Add(folder);
            }

            int layerCount = 0;
            foreach (string layer in TopKeys(info.DefinitionLayers, 3))
            {
                if (layerCount++ >= 3) break;
                tags.Add(layer);
            }

            foreach (string state in info.VisibilityStates) tags.Add(state);

            return tags;
        }

        private static string SafeName(string value)
        {
            return string.IsNullOrEmpty(value) ? "未分类" : value;
        }

        private static string TopKey(SortedDictionary<string, int> map)
        {
            string best = string.Empty;
            int bestCount = -1;
            foreach (KeyValuePair<string, int> pair in map)
            {
                if (pair.Value > bestCount) { bestCount = pair.Value; best = pair.Key; }
            }
            return best;
        }

        private static IEnumerable<string> TopKeys(SortedDictionary<string, int> map, int max)
        {
            var entries = new List<KeyValuePair<string, int>>(map);
            entries.Sort(delegate(KeyValuePair<string, int> a, KeyValuePair<string, int> b)
            {
                int compare = b.Value.CompareTo(a.Value);
                if (compare != 0) return compare;
                return string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
            });
            var result = new List<string>();
            for (int i = 0; i < entries.Count && i < max; i++) result.Add(entries[i].Key);
            return result;
        }
    }
}