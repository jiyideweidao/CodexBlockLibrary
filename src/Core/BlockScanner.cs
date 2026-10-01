using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AcadRuntime = Autodesk.AutoCAD.Runtime;

namespace CodexBlockLib.Core
{
    /// <summary>
    /// 图纸扫描器：统计图块数量，区分动态块 / 静态块 / 外部参照，
    /// 统计动态块的可见性状态与参数取值分布。
    /// </summary>
    public static class BlockScanner
    {
        private static AcadRuntime.RXClass blockReferenceClass;
        private static readonly string[] ModelLayoutNames = new string[] { "Model", "模型" };

        public static FileScanResult ScanFile(string path, ScanOptions options, Action<ScanProgress> progress)
        {
            if (options == null) options = new ScanOptions();
            var result = new FileScanResult();
            result.Path = path;
            result.Label = Path.GetFileName(path);
            result.Format = DrawingReader.FormatName(DrawingReader.Detect(path));

            var watch = Stopwatch.StartNew();
            AppPaths.Ensure();

            string error;
            Database db = DrawingReader.Open(path, out error);
            if (db == null)
            {
                result.Ok = false;
                result.Error = string.IsNullOrEmpty(error) ? "无法打开文件" : error;
                result.ScanSeconds = watch.Elapsed.TotalSeconds;
                Log.Warn("打开图纸失败: " + result.Label + " (" + result.Error + ")");
                return result;
            }

            try
            {
                try { result.DwgVersion = db.OriginalFileVersion.ToString(); }
                catch { }
                Analyze(db, result, options, progress);
                BuildThumbnails(db, result, options, progress);
                result.Ok = true;
            }
            catch (Exception ex)
            {
                result.Ok = false;
                result.Error = ex.Message;
                Log.Error("扫描图纸失败: " + path, ex);
            }
            finally
            {
                try { db.Dispose(); }
                catch { }
            }

            result.ScanSeconds = watch.Elapsed.TotalSeconds;
            result.ScanTime = DateTime.Now;
            Log.Write("已扫描 " + result.Label + ": 动态块定义 " + result.Blocks.Count.ToString(CultureInfo.InvariantCulture) + " 个, 用时 " + result.ScanSeconds.ToString("0.00", CultureInfo.InvariantCulture) + " 秒");
            return result;
        }

        /// <summary>扫描已在内存中的数据库（例如当前正在绘制的图纸，包含未保存的修改）。</summary>
        public static FileScanResult ScanDatabase(Database db, string label, ScanOptions options, Action<ScanProgress> progress)
        {
            if (options == null) options = new ScanOptions();
            var result = new FileScanResult();
            result.Path = label;
            result.Label = label;
            result.IsCurrentDrawing = true;
            result.Format = "当前图纸";

            var watch = Stopwatch.StartNew();
            AppPaths.Ensure();
            try
            {
                if (db == null) throw new InvalidOperationException("没有活动图纸");
                try { result.DwgVersion = db.OriginalFileVersion.ToString(); }
                catch { }
                Analyze(db, result, options, progress);
                BuildThumbnails(db, result, options, progress);
                result.Ok = true;
            }
            catch (Exception ex)
            {
                result.Ok = false;
                result.Error = ex.Message;
                Log.Error("扫描当前图纸失败", ex);
            }
            result.ScanSeconds = watch.Elapsed.TotalSeconds;
            result.ScanTime = DateTime.Now;
            Log.Write("已扫描 " + result.Label + ": 动态块定义 " + result.Blocks.Count.ToString(CultureInfo.InvariantCulture) + " 个, 用时 " + result.ScanSeconds.ToString("0.00", CultureInfo.InvariantCulture) + " 秒");
            return result;
        }

        private static void Analyze(Database db, FileScanResult result, ScanOptions options, Action<ScanProgress> progress)
        {
            result.InsUnits = (int)db.Insunits;
            result.InsUnitsName = UnitConvert.UnitName(db.Insunits);

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

                // 1) 布局名称映射（模型空间 / 各图纸空间）
                var layoutNameByBtr = new Dictionary<ObjectId, string>();
                try
                {
                    var layoutDictionary = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                    foreach (DBDictionaryEntry entry in layoutDictionary)
                    {
                        Layout layout = tr.GetObject(entry.Value, OpenMode.ForRead, false) as Layout;
                        if (layout == null) continue;
                        layoutNameByBtr[layout.BlockTableRecordId] = layout.LayoutName;
                        result.Layouts.Add(layout.LayoutName);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("读取布局失败: " + ex.Message);
                }

                // 2) 枚举块表，建立 定义 -> 统计对象 的映射
                var infoByKey = new Dictionary<ObjectId, BlockInfo>();
                // 只收录动态块：静态块 / 外部参照 / 匿名块既不进列表也不统计实例
                var skippedIds = new HashSet<ObjectId>();
                foreach (ObjectId id in blockTable)
                {
                    if (id.IsErased) continue;
                    BlockTableRecord record = null;
                    try { record = tr.GetObject(id, OpenMode.ForRead, false) as BlockTableRecord; }
                    catch { }
                    if (record == null) continue;

                    bool isLayout = false;
                    bool isXref = false;
                    bool isAnonymous = false;
                    bool isDynamicRecord = false;
                    XrefStatus xrefStatus = XrefStatus.NotAnXref;
                    try { isLayout = record.IsLayout; } catch { }
                    try { isXref = record.IsFromExternalReference || record.IsFromOverlayReference; } catch { }
                    try { isAnonymous = record.IsAnonymous; } catch { }
                    try { isDynamicRecord = record.IsDynamicBlock; } catch { }
                    try { xrefStatus = record.XrefStatus; } catch { }

                    if (isLayout) continue;
                    if (isXref) { /* 外部参照也列出，方便识别 */ }
                    else if (isAnonymous && isDynamicRecord)
                    {
                        // 动态块的匿名变体（*U 系列）不单独列出，实例计入其动态块定义
                        continue;
                    }
                    else if (isAnonymous)
                    {
                        result.AnonymousSkipped++;
                        continue;
                    }

                    string name = string.Empty;
                    try { name = record.Name; } catch { }
                    if (string.IsNullOrEmpty(name)) continue;

                    var info = new BlockInfo();
                    info.SourceFile = result.Path;
                    info.Name = name;
                    info.RawName = name;
                    info.IsXref = isXref;
                    info.IsUnresolved = isXref && xrefStatus != XrefStatus.Resolved && xrefStatus != XrefStatus.Unloaded;
                    try { info.HasAttributes = record.HasAttributeDefinitions; } catch { }
                    info.IsDynamic = !isXref && LooksDynamicDefinition(record);
                    info.Kind = isXref ? BlockKind.Xref : (info.IsDynamic ? BlockKind.Dynamic : BlockKind.Static);
                    if (options.DynamicOnly && !info.IsDynamic)
                    {
                        skippedIds.Add(id);
                        continue;
                    }

                    CountDefinitionContent(record, tr, info);
                    result.Blocks.Add(info);
                    infoByKey[id] = info;
                    if (!string.IsNullOrEmpty(name)) result.DefinitionIds[name] = id;
                }

                // 3) 遍历所有块表记录里的块参照，累计数量、图层、变体与参数分布
                foreach (ObjectId ownerId in blockTable)
                {
                    if (ownerId.IsErased) continue;
                    BlockTableRecord owner = null;
                    try { owner = tr.GetObject(ownerId, OpenMode.ForRead, false) as BlockTableRecord; }
                    catch { }
                    if (owner == null) continue;

                    bool ownerIsLayout = false;
                    string ownerLayoutName = string.Empty;
                    try { ownerIsLayout = owner.IsLayout; } catch { }
                    if (ownerIsLayout && !layoutNameByBtr.TryGetValue(ownerId, out ownerLayoutName)) ownerLayoutName = "布局";

                    ObjectId[] entityIds;
                    try
                    {
                        var list = new List<ObjectId>();
                        foreach (ObjectId entityId in owner) list.Add(entityId);
                        entityIds = list.ToArray();
                    }
                    catch
                    {
                        continue;
                    }

                    if (ownerIsLayout && IsModelSpace(ownerLayoutName, owner))
                    {
                        result.ModelEntityCount += entityIds.Length;
                    }

                    foreach (ObjectId entityId in entityIds)
                    {
                        if (!IsBlockReference(entityId)) continue;

                        BlockReference reference = null;
                        try { reference = tr.GetObject(entityId, OpenMode.ForRead, false) as BlockReference; }
                        catch { }
                        if (reference == null) continue;

                        ObjectId definitionId = ObjectId.Null;
                        bool isDynamic = false;
                        try { isDynamic = reference.IsDynamicBlock; }
                        catch { }
                        if (isDynamic)
                        {
                            try { definitionId = reference.DynamicBlockTableRecord; }
                            catch { }
                        }
                        if (definitionId.IsNull)
                        {
                            try { definitionId = reference.BlockTableRecord; }
                            catch { }
                        }
                        if (definitionId.IsNull) { result.UnknownRefs++; continue; }

                        BlockInfo info;
                        if (!infoByKey.TryGetValue(definitionId, out info))
                        {
                            // 非动态块在定义阶段已跳过，不计入未知参照
                            if (!skippedIds.Contains(definitionId)) result.UnknownRefs++;
                            continue;
                        }

                        if (ownerIsLayout)
                        {
                            info.InstanceCount++;
                            if (IsModelSpace(ownerLayoutName, owner)) info.ModelCount++;
                            else info.PaperCount++;
                            if (!string.IsNullOrEmpty(ownerLayoutName))
                            {
                                int count;
                                if (info.LayoutCounts.TryGetValue(ownerLayoutName, out count)) info.LayoutCounts[ownerLayoutName] = count + 1;
                                else info.LayoutCounts[ownerLayoutName] = 1;
                            }
                        }
                        else
                        {
                            info.NestedCount++;
                        }

                        try
                        {
                            string layer = reference.Layer;
                            if (!string.IsNullOrEmpty(layer))
                            {
                                int count;
                                if (info.LayerCounts.TryGetValue(layer, out count)) info.LayerCounts[layer] = count + 1;
                                else info.LayerCounts[layer] = 1;
                            }
                        }
                        catch { }

                        if (isDynamic)
                        {
                            try
                            {
                                ObjectId anonymousId = reference.AnonymousBlockTableRecord;
                                string variantName = string.Empty;
                                if (!anonymousId.IsNull)
                                {
                                    BlockTableRecord anonymous = tr.GetObject(anonymousId, OpenMode.ForRead, false) as BlockTableRecord;
                                    if (anonymous != null) variantName = anonymous.Name;
                                }
                                if (string.IsNullOrEmpty(variantName)) variantName = "*U(未知)";
                                int count;
                                if (info.Variants.TryGetValue(variantName, out count)) info.Variants[variantName] = count + 1;
                                else info.Variants[variantName] = 1;
                            }
                            catch { }

                            if (options.ReadProperties && info.PropertyReadCount < options.MaxPropertyReadsPerBlock)
                            {
                                info.PropertyReadCount++;
                                ReadDynamicProperties(reference, info);
                            }
                        }

                        if (info.SampleRefHandle.Length == 0)
                        {
                            try { info.SampleRefHandle = reference.Handle.ToString(); }
                            catch { }
                            try
                            {
                                Scale3d scale = reference.ScaleFactors;
                                info.SampleScale = Math.Abs(scale.X) > 1e-12 ? Math.Abs(scale.X) : 1.0;
                                info.SampleRotation = reference.Rotation;
                            }
                            catch { }
                        }
                    }
                }

                tr.Commit();
            }

            // 收尾：把定义内部的图层信息补进统计（用于按图层分类）
            foreach (BlockInfo info in result.Blocks)
            {
                if (info.DefinitionLayers.Count == 0 && info.LayerCounts.Count > 0)
                {
                    foreach (KeyValuePair<string, int> pair in info.LayerCounts) info.DefinitionLayers[pair.Key] = pair.Value;
                }
            }
        }

        private static bool IsModelSpace(string layoutName, BlockTableRecord owner)
        {
            if (!string.IsNullOrEmpty(layoutName))
            {
                foreach (string model in ModelLayoutNames)
                {
                    if (string.Equals(layoutName, model, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            try { return owner.Name == "*Model_Space" || owner.Name == "*MODEL_SPACE"; }
            catch { return false; }
        }

        /// <summary>块定义里是否存在动态块参数/动作（AcDbBlockVisibilityParameter 等）。</summary>

        private static bool LooksDynamicDefinition(BlockTableRecord record)
        {
            return DynamicBlockDetector.IsDynamicDefinition(record);
        }

        private static void CountDefinitionContent(BlockTableRecord record, Transaction tr, BlockInfo info)
        {
            int count = 0;
            try
            {
                foreach (ObjectId id in record)
                {
                    count++;
                    if (count > 4000) break;
                    string layer = null;
                    try
                    {
                        Entity entity = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                        if (entity != null) layer = entity.Layer;
                    }
                    catch { }
                    if (!string.IsNullOrEmpty(layer))
                    {
                        int value;
                        if (info.DefinitionLayers.TryGetValue(layer, out value)) info.DefinitionLayers[layer] = value + 1;
                        else info.DefinitionLayers[layer] = 1;
                    }
                }
            }
            catch { }
            info.DefinitionEntityCount = count;
        }

        private static void ReadDynamicProperties(BlockReference reference, BlockInfo info)
        {
            DynamicBlockReferencePropertyCollection properties = null;
            try { properties = reference.DynamicBlockReferencePropertyCollection; }
            catch { return; }
            if (properties == null) return;

            for (int i = 0; i < properties.Count; i++)
            {
                DynamicBlockReferenceProperty property = null;
                try { property = properties[i]; }
                catch { continue; }
                if (property == null) continue;

                string name = string.Empty;
                try { name = property.PropertyName; }
                catch { }
                if (string.IsNullOrEmpty(name)) name = "参数" + (i + 1).ToString(CultureInfo.InvariantCulture);

                DynamicPropertyInfo target = info.FindProperty(name);
                if (target == null)
                {
                    target = new DynamicPropertyInfo();
                    target.Name = name;
                    info.Properties.Add(target);
                }

                object value = null;
                string text = string.Empty;
                try
                {
                    value = property.Value;
                    text = TextUtil.FormatValue(value);
                }
                catch { }

                if (text.Length > 0)
                {
                    int count;
                    if (target.ValueUsage.TryGetValue(text, out count)) target.ValueUsage[text] = count + 1;
                    else target.ValueUsage[text] = 1;
                }

                if (target.AllowedValues.Count == 0)
                {
                    try
                    {
                        object[] allowed = property.GetAllowedValues();
                        if (allowed != null)
                        {
                            foreach (object item in allowed)
                            {
                                string textValue = TextUtil.FormatValue(item);
                                if (textValue.Length > 0 && !target.AllowedValues.Contains(textValue)) target.AllowedValues.Add(textValue);
                            }
                        }
                    }
                    catch { }
                }

                if (target.TypeName.Length == 0) target.TypeName = TypeNameOf(value);
                target.ReadCount++;
            }

            if (info.VisibilityPropertyName.Length == 0)
            {
                foreach (DynamicPropertyInfo property in info.Properties)
                {
                    if (property.Name.IndexOf("visib", StringComparison.OrdinalIgnoreCase) >= 0
                        || property.Name.IndexOf("可见", StringComparison.Ordinal) >= 0)
                    {
                        if (property.AllowedValues.Count > 0) { info.VisibilityPropertyName = property.Name; break; }
                    }
                }
                if (info.VisibilityPropertyName.Length == 0)
                {
                    foreach (DynamicPropertyInfo property in info.Properties)
                    {
                        DynamicPropertyInfo candidate = property;
                        if (candidate.AllowedValues.Count > 0 && candidate.ValueUsage.Count > 0)
                        {
                            bool stringValued = true;
                            foreach (string key in candidate.ValueUsage.Keys)
                            {
                                double parsed;
                                if (double.TryParse(key, NumberStyles.Any, CultureInfo.InvariantCulture, out parsed)) { stringValued = false; break; }
                            }
                            if (stringValued) { info.VisibilityPropertyName = candidate.Name; break; }
                        }
                    }
                }
            }
        }

        private static string TypeNameOf(object value)
        {
            if (value == null) return "未知";
            if (value is string) return "字符串";
            if (value is bool) return "开关";
            if (value is double || value is float) return "距离/角度";
            if (value is short || value is int || value is long) return "整数/查寻";
            return value.GetType().Name;
        }

        private static bool IsBlockReference(ObjectId id)
        {
            try
            {
                AcadRuntime.RXClass cls = id.ObjectClass;
                if (cls == null) return false;
                if (blockReferenceClass == null)
                {
                    blockReferenceClass = AcadRuntime.RXObject.GetClass(typeof(BlockReference));
                }
                if (blockReferenceClass == null) return false;
                return cls.IsDerivedFrom(blockReferenceClass);
            }
            catch
            {
                return false;
            }
        }

        private static void BuildThumbnails(Database db, FileScanResult result, ScanOptions options, Action<ScanProgress> progress)
        {
            if (!options.BuildThumbnails || options.ThumbSize <= 0) return;
            int limit = options.MaxThumbnailsPerFile > 0 ? options.MaxThumbnailsPerFile : 200;
            int done = 0;

            var targets = new List<BlockInfo>();
            foreach (BlockInfo info in result.BlocksByCount())
            {
                if (info.IsXref) continue;
                if (info.IsUnresolved) continue;
                if (!string.IsNullOrEmpty(info.SampleRefHandle)) { /* 有实例 */ }
                if (info.InstanceCount == 0 && info.NestedCount == 0 && !info.IsDynamic) continue;
                targets.Add(info);
                if (targets.Count >= limit) break;
            }
            if (targets.Count == 0) return;

            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (BlockInfo info in targets)
                    {
                        try
                        {
                            if (ThumbCache.IsFresh(result.Path, info.Name)) { done++; continue; }
                            ObjectId btrId;
                            if (!result.DefinitionIds.TryGetValue(info.Name, out btrId)) continue;
                            ShapeSet shapes = GeometryCapture.CaptureBlock(btrId, tr, db);
                            if (!shapes.HasData) continue;
                            using (System.Drawing.Bitmap bitmap = ThumbnailRenderer.Render(shapes, options.ThumbSize, options.ThumbSize))
                            {
                                ThumbCache.Save(result.Path, info.Name, bitmap);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warn("生成缩略图失败(" + info.Name + "): " + ex.Message);
                        }
                        done++;
                        if (progress != null && (done % 10) == 0)
                        {
                            progress(new ScanProgress
                            {
                                FilePath = result.Path,
                                Stage = "缩略图",
                                Current = done,
                                Total = targets.Count,
                                Message = "正在生成缩略图 " + done + "/" + targets.Count
                            });
                        }
                    }
                    tr.Commit();
                }
            }
            catch (Exception ex)
            {
                Log.Warn("生成缩略图阶段失败: " + ex.Message);
            }
        }
    }
}
