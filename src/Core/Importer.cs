using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace CodexBlockLib.Core
{
    /// <summary>
    /// 图块搬运：把源图纸中的块定义 / 块参照复制到当前图纸。
    /// 全部通过 WblockCloneObjects 深克隆完成，嵌套块、图层、文字样式、动态块参数值都会一起带过来。
    /// </summary>
    public static class Importer
    {
        public static bool HasDefinition(Database db, string name)
        {
            if (db == null || string.IsNullOrEmpty(name)) return false;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                bool has = table.Has(name);
                tr.Commit();
                return has;
            }
        }

        /// <summary>列出源图纸中可导入的块名（排除布局、匿名块、外部参照内部块）。</summary>
        public static List<string> ListDefinitions(Database source, bool dynamicOnly)
        {
            var names = new List<string>();
            if (source == null) return names;
            using (Transaction tr = source.TransactionManager.StartTransaction())
            {
                var table = (BlockTable)tr.GetObject(source.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId id in table)
                {
                    if (id.IsErased) continue;
                    BlockTableRecord record = null;
                    try { record = tr.GetObject(id, OpenMode.ForRead, false) as BlockTableRecord; }
                    catch { }
                    if (record == null) continue;
                    bool isLayout = false, isAnonymous = false, isXref = false;
                    try { isLayout = record.IsLayout; } catch { }
                    try { isAnonymous = record.IsAnonymous; } catch { }
                    try { isXref = record.IsFromExternalReference || record.IsFromOverlayReference; } catch { }
                    if (isLayout || isAnonymous || isXref) continue;
                    string name = string.Empty;
                    try { name = record.Name; } catch { }
                    if (string.IsNullOrEmpty(name)) continue;
                    if (dynamicOnly && !DynamicBlockDetector.IsDynamicDefinition(record)) continue;
                    names.Add(name);
                }
                tr.Commit();
            }
            return names;
        }

        /// <summary>把源图纸中的块定义导入目标图纸，返回成功导入的数量。</summary>
        public static int ImportDefinitions(Database source, IEnumerable<string> names, Database target, out string error)
        {
            error = string.Empty;
            if (source == null || target == null) { error = "数据库无效"; return 0; }

            var ids = new ObjectIdCollection();
            using (Transaction tr = source.TransactionManager.StartTransaction())
            {
                var table = (BlockTable)tr.GetObject(source.BlockTableId, OpenMode.ForRead);
                foreach (string name in names)
                {
                    if (string.IsNullOrEmpty(name)) continue;
                    if (!table.Has(name)) continue;
                    ids.Add(table[name]);
                }
                tr.Commit();
            }

            if (ids.Count == 0) { error = "源图纸中没有同名块定义"; return 0; }

            try
            {
                var mapping = new IdMapping();
                source.WblockCloneObjects(ids, target.BlockTableId, mapping, DuplicateRecordCloning.Replace, false);
                return ids.Count;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Error("导入块定义失败", ex);
                return 0;
            }
        }

        /// <summary>把源图纸的块定义导入并在指定位置插入一个块参照。</summary>
        public static ObjectId InsertDefinition(Database source, string definitionName, Database target, Point3d position,
            double scale, double rotation, string layer, out string error)
        {
            error = string.Empty;
            if (!HasDefinition(target, definitionName))
            {
                int imported = ImportDefinitions(source, new string[] { definitionName }, target, out error);
                if (imported <= 0 && !HasDefinition(target, definitionName))
                {
                    if (string.IsNullOrEmpty(error)) error = "块定义未找到: " + definitionName;
                    return ObjectId.Null;
                }
            }

            try
            {
                using (Transaction tr = target.TransactionManager.StartTransaction())
                {
                    var table = (BlockTable)tr.GetObject(target.BlockTableId, OpenMode.ForRead);
                    if (!table.Has(definitionName)) { error = "目标图纸中不存在块 " + definitionName; tr.Abort(); return ObjectId.Null; }

                    var space = (BlockTableRecord)tr.GetObject(target.CurrentSpaceId, OpenMode.ForWrite);
                    var reference = new BlockReference(position, table[definitionName]);
                    double safeScale = Math.Abs(scale) > 1e-12 ? scale : 1.0;
                    reference.ScaleFactors = new Scale3d(safeScale);
                    reference.Rotation = rotation;

                    if (!string.IsNullOrEmpty(layer))
                    {
                        var layerTable = (LayerTable)tr.GetObject(target.LayerTableId, OpenMode.ForRead);
                        if (layerTable.Has(layer)) reference.Layer = layer;
                    }

                    space.AppendEntity(reference);
                    tr.AddNewlyCreatedDBObject(reference, true);
                    tr.Commit();
                    return reference.ObjectId;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Error("插入块失败: " + definitionName, ex);
                return ObjectId.Null;
            }
        }

        /// <summary>
        /// 原样复制一个块参照（含动态块当前参数值），可在目标图纸指定新位置。
        /// </summary>
        public static ObjectId CloneReference(Database source, string sourceHandle, Database target, ObjectId targetSpaceId,
            Point3d position, bool useSourcePosition, bool keepSourceTransform, out string error)
        {
            error = string.Empty;
            ObjectId sourceId = ObjectId.Null;
            try
            {
                long value = long.Parse(sourceHandle, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                sourceId = source.GetObjectId(false, new Handle(value), 0);
            }
            catch (Exception ex)
            {
                error = "无法定位源块参照: " + ex.Message;
                return ObjectId.Null;
            }
            if (sourceId.IsNull) { error = "无法定位源块参照"; return ObjectId.Null; }

            if (targetSpaceId.IsNull) targetSpaceId = target.CurrentSpaceId;

            try
            {
                var ids = new ObjectIdCollection();
                ids.Add(sourceId);
                var mapping = new IdMapping();
                source.WblockCloneObjects(ids, targetSpaceId, mapping, DuplicateRecordCloning.Replace, false);

                ObjectId newId = ObjectId.Null;
                try
                {
                    var pair = mapping[sourceId];
                    if (pair != null) newId = pair.Value;
                }
                catch { }

                if (newId.IsNull) { error = "克隆未返回新对象"; return ObjectId.Null; }

                using (Transaction tr = target.TransactionManager.StartTransaction())
                {
                    var reference = tr.GetObject(newId, OpenMode.ForWrite, false) as BlockReference;
                    if (reference == null) { error = "克隆结果不是块参照"; tr.Abort(); return ObjectId.Null; }

                    if (!useSourcePosition) reference.Position = position;
                    if (!keepSourceTransform) reference.Rotation = 0.0;
                    tr.Commit();
                }
                return newId;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Error("复制块参照失败", ex);
                return ObjectId.Null;
            }
        }

        /// <summary>统计目标图纸中某块定义的参照数量（用于验证复制/插入结果）。</summary>
        public static int CountReferences(Database db, string definitionName)
        {
            int total = 0;
            if (db == null) return 0;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId ownerId in table)
                {
                    BlockTableRecord owner = null;
                    try { owner = tr.GetObject(ownerId, OpenMode.ForRead, false) as BlockTableRecord; }
                    catch { }
                    if (owner == null) continue;
                    bool isLayout = false;
                    try { isLayout = owner.IsLayout; } catch { }
                    if (!isLayout) continue;

                    foreach (ObjectId entityId in owner)
                    {
                        var reference = tr.GetObject(entityId, OpenMode.ForRead, false) as BlockReference;
                        if (reference == null) continue;
                        string name = string.Empty;
                        try
                        {
                            bool isDynamic = reference.IsDynamicBlock;
                            if (isDynamic)
                            {
                                ObjectId definitionId = reference.DynamicBlockTableRecord;
                                var definition = tr.GetObject(definitionId, OpenMode.ForRead, false) as BlockTableRecord;
                                if (definition != null) name = definition.Name;
                            }
                            else
                            {
                                name = reference.Name;
                            }
                        }
                        catch { }
                        if (string.Equals(name, definitionName, StringComparison.OrdinalIgnoreCase)) total++;
                    }
                }
                tr.Commit();
            }
            return total;
        }
    }

}
