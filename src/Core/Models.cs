using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;

namespace CodexBlockLib.Core
{
    public enum SourceFormat { Unknown, Dwg, Dwt, Dws, Dxf }

    /// <summary>块类型：动态块 / 静态块 / 外部参照。</summary>
    public enum BlockKind { Unknown, Static, Dynamic, Xref }

    /// <summary>动态块的某个参数（可见性、查寻、拉伸、翻转、对齐、基点、旋转、阵列等）。</summary>
    public sealed class DynamicPropertyInfo
    {
        public string Name = string.Empty;
        public string TypeName = string.Empty;
        /// <summary>允许值（可见性状态、查寻表的候选项等）。</summary>
        public List<string> AllowedValues = new List<string>();
        /// <summary>图纸中实际出现的取值 -> 次数。</summary>
        public SortedDictionary<string, int> ValueUsage = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public int ReadCount;

        public bool IsVisibilityLike
        {
            get { return AllowedValues.Count > 0 && ValueUsage.Count > 0; }
        }

        public string CurrentSummary
        {
            get
            {
                if (ValueUsage.Count == 0) return string.Empty;
                var parts = new List<string>();
                foreach (KeyValuePair<string, int> kv in ValueUsage)
                {
                    parts.Add(kv.Key + " x" + kv.Value.ToString(CultureInfo.InvariantCulture));
                }
                return string.Join(", ", parts.ToArray());
            }
        }
    }

    /// <summary>图纸中一个块定义（动态/静态/外部参照）的统计结果。</summary>
    public sealed class BlockInfo
    {
        public string SourceFile = string.Empty;
        public string Name = string.Empty;
        public string RawName = string.Empty;
        public BlockKind Kind = BlockKind.Unknown;

        public bool IsDynamic;
        public bool IsXref;
        public bool IsUnresolved;
        public bool HasAttributes;
        public bool IsAnnotative;
        public bool HasUserMeta;

        public int InstanceCount;      // 模型空间 + 布局中的参照数
        public int ModelCount;
        public int PaperCount;
        public int NestedCount;        // 被其它块定义嵌套引用的次数
        public int DefinitionEntityCount;

        public SortedDictionary<string, int> LayoutCounts = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public SortedDictionary<string, int> LayerCounts = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public SortedDictionary<string, int> DefinitionLayers = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public SortedDictionary<string, int> Variants = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public List<DynamicPropertyInfo> Properties = new List<DynamicPropertyInfo>();
        public string VisibilityPropertyName = string.Empty;
        public int PropertyReadCount;

        public string SampleRefHandle = string.Empty;
        public double SampleScale = 1.0;
        public double SampleRotation;

        // 由分类/标签阶段填充
        public string Category = string.Empty;
        public List<string> Tags = new List<string>();

        public string Key { get { return LibraryStore.Key(SourceFile, Name); } }

        public string TypeText
        {
            get
            {
                if (IsXref) return IsUnresolved ? "外部参照(未解析)" : "外部参照";
                if (IsDynamic) return "动态块";
                return "静态块";
            }
        }

        public DynamicPropertyInfo FindProperty(string name)
        {
            foreach (DynamicPropertyInfo p in Properties)
            {
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
            }
            return null;
        }

        public DynamicPropertyInfo VisibilityProperty
        {
            get
            {
                if (!string.IsNullOrEmpty(VisibilityPropertyName))
                {
                    DynamicPropertyInfo p = FindProperty(VisibilityPropertyName);
                    if (p != null) return p;
                }
                foreach (DynamicPropertyInfo p in Properties)
                {
                    if (p.IsVisibilityLike) return p;
                }
                return null;
            }
        }

        public List<string> VisibilityStates
        {
            get
            {
                var list = new List<string>();
                DynamicPropertyInfo p = VisibilityProperty;
                if (p == null) return list;
                foreach (string s in p.AllowedValues) list.Add(s);
                return list;
            }
        }

        public string VisibilityStatesText
        {
            get
            {
                List<string> states = VisibilityStates;
                return states.Count == 0 ? string.Empty : string.Join(" / ", states.ToArray());
            }
        }

        public string CategoryOrFallback
        {
            get { return string.IsNullOrEmpty(Category) ? "未分类" : Category; }
        }

        public string DisplayName
        {
            get
            {
                if (InstanceCount > 0 || NestedCount > 0)
                {
                    return Name + "  (" + InstanceCount.ToString(CultureInfo.InvariantCulture) + ")";
                }
                return Name;
            }
        }

        public string TooltipText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("块名: " + Name);
            sb.AppendLine("类型: " + TypeText);
            sb.AppendLine("来源: " + Path.GetFileName(SourceFile));
            sb.AppendLine("分类: " + CategoryOrFallback);
            sb.AppendLine("实例: 合计 " + InstanceCount.ToString(CultureInfo.InvariantCulture)
                + "（模型 " + ModelCount.ToString(CultureInfo.InvariantCulture)
                + " / 布局 " + PaperCount.ToString(CultureInfo.InvariantCulture) + "）");
            if (NestedCount > 0) sb.AppendLine("嵌套引用: " + NestedCount.ToString(CultureInfo.InvariantCulture));
            if (Variants.Count > 0) sb.AppendLine("动态变体: " + Variants.Count.ToString(CultureInfo.InvariantCulture) + " 个");
            string states = VisibilityStatesText;
            if (states.Length > 0) sb.AppendLine("可见性状态: " + states);
            if (Properties.Count > 0)
            {
                var names = new List<string>();
                foreach (DynamicPropertyInfo p in Properties) names.Add(p.Name + "(" + p.TypeName + ")");
                sb.AppendLine("参数: " + string.Join(", ", names.ToArray()));
            }
            if (DefinitionLayers.Count > 0)
            {
                var layers = new List<string>();
                int n = 0;
                foreach (KeyValuePair<string, int> kv in DefinitionLayers)
                {
                    layers.Add(kv.Key);
                    if (++n >= 6) break;
                }
                sb.AppendLine("图层: " + string.Join(", ", layers.ToArray()));
            }
            sb.AppendLine("定义内图元: " + DefinitionEntityCount.ToString(CultureInfo.InvariantCulture));
            if (Tags.Count > 0) sb.Append("标签: " + string.Join(", ", Tags.ToArray()));
            return sb.ToString();
        }
    }

    public sealed class ScanOptions
    {
        public bool DynamicOnly = true;   // 只收录并统计动态块，其它块不列表也不统计
        public bool CountNested = true;
        public bool ReadProperties = true;
        public int MaxPropertyReadsPerBlock = 60;
        public bool BuildThumbnails = true;
        public int MaxThumbnailsPerFile = 400;
        public int ThumbSize = 96;

        public ScanOptions Clone()
        {
            return (ScanOptions)MemberwiseClone();
        }
    }

    public sealed class ScanProgress
    {
        public string FilePath = string.Empty;
        public string Stage = string.Empty;
        public int Current;
        public int Total;
        public string Message = string.Empty;

        public override string ToString()
        {
            return Message;
        }
    }

    /// <summary>一张图纸的扫描结果。</summary>
    public sealed class FileScanResult
    {
        public string Path = string.Empty;
        public string Label = string.Empty;
        public bool Ok;
        public string Error = string.Empty;
        public bool IsCurrentDrawing;
        public bool IsFolder;

        public string DwgVersion = string.Empty;
        public int InsUnits;
        public string InsUnitsName = string.Empty;
        public string Format = string.Empty;

        public int ModelEntityCount;
        public int LayoutCount;
        public int AnonymousSkipped;
        public int UnknownRefs;

        public DateTime ScanTime = DateTime.Now;
        public double ScanSeconds;

        public List<string> Layouts = new List<string>();
        public List<BlockInfo> Blocks = new List<BlockInfo>();

        /// <summary>仅在扫描期间有效，用于生成缩略图。</summary>
        internal Dictionary<string, ObjectId> DefinitionIds = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);

        public int DynamicDefinitionCount
        {
            get { int n = 0; foreach (BlockInfo b in Blocks) if (b.IsDynamic) n++; return n; }
        }

        public int StaticDefinitionCount
        {
            get { int n = 0; foreach (BlockInfo b in Blocks) if (!b.IsDynamic && !b.IsXref) n++; return n; }
        }

        public int XrefCount
        {
            get { int n = 0; foreach (BlockInfo b in Blocks) if (b.IsXref) n++; return n; }
        }

        public int DynamicInstanceCount
        {
            get { int n = 0; foreach (BlockInfo b in Blocks) if (b.IsDynamic) n += b.InstanceCount; return n; }
        }

        public int StaticInstanceCount
        {
            get { int n = 0; foreach (BlockInfo b in Blocks) if (!b.IsDynamic && !b.IsXref) n += b.InstanceCount; return n; }
        }

        public int TotalInstanceCount
        {
            get { int n = 0; foreach (BlockInfo b in Blocks) n += b.InstanceCount; return n; }
        }

        public List<BlockInfo> DynamicBlocks
        {
            get
            {
                var list = new List<BlockInfo>();
                foreach (BlockInfo b in Blocks) if (b.IsDynamic) list.Add(b);
                return list;
            }
        }

        /// <summary>按实例数排序的块列表。</summary>
        public List<BlockInfo> BlocksByCount()
        {
            var list = new List<BlockInfo>(Blocks);
            list.Sort(delegate(BlockInfo a, BlockInfo b)
            {
                int c = b.InstanceCount.CompareTo(a.InstanceCount);
                if (c != 0) return c;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        public string DisplayName
        {
            get
            {
                if (IsCurrentDrawing) return Label.Length > 0 ? Label : "当前图纸";
                return System.IO.Path.GetFileName(Path);
            }
        }

        public string SummaryLine()
        {
            if (!Ok) return DisplayName + " => 打开失败: " + Error;
            return DisplayName
                + " | 动态块定义 " + DynamicDefinitionCount.ToString(CultureInfo.InvariantCulture)
                + " 个 / 实例 " + DynamicInstanceCount.ToString(CultureInfo.InvariantCulture) + " 个"
                + " | 单位 " + InsUnitsName
                + " | 耗时 " + ScanSeconds.ToString("0.00", CultureInfo.InvariantCulture) + "s";
        }
    }

    /// <summary>当前会话中已扫描的图纸集合，供面板与命令共享。</summary>
    public static class ScanSession
    {
        private static readonly List<FileScanResult> Items = new List<FileScanResult>();

        public static event EventHandler Changed;

        public static List<FileScanResult> Results
        {
            get { lock (Items) { return new List<FileScanResult>(Items); } }
        }

        public static int Count { get { lock (Items) { return Items.Count; } } }

        public static void AddOrReplace(FileScanResult result)
        {
            if (result == null) return;
            lock (Items)
            {
                int index = Items.FindIndex(delegate(FileScanResult r)
                {
                    return string.Equals(r.Path, result.Path, StringComparison.OrdinalIgnoreCase);
                });
                if (index >= 0) Items[index] = result; else Items.Add(result);
            }
            Raise();
        }

        public static void Replace(IEnumerable<FileScanResult> results)
        {
            lock (Items)
            {
                Items.Clear();
                if (results != null) Items.AddRange(results);
            }
            Raise();
        }

        public static void Remove(string path)
        {
            lock (Items)
            {
                Items.RemoveAll(delegate(FileScanResult r)
                {
                    return string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase);
                });
            }
            Raise();
        }

        public static void Clear()
        {
            lock (Items) { Items.Clear(); }
            Raise();
        }

        public static FileScanResult Find(string path)
        {
            lock (Items)
            {
                return Items.Find(delegate(FileScanResult r)
                {
                    return string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase);
                });
            }
        }

        public static List<BlockInfo> AllBlocks()
        {
            var list = new List<BlockInfo>();
            lock (Items)
            {
                foreach (FileScanResult r in Items)
                {
                    if (r == null || !r.Ok || r.Blocks == null) continue;
                    foreach (BlockInfo info in r.Blocks)
                    {
                        if (info != null && info.IsDynamic) list.Add(info);
                    }
                }
            }
            return list;
        }

        public static void Raise()
        {
            EventHandler handler = Changed;
            if (handler != null) handler(null, EventArgs.Empty);
        }
    }
}
