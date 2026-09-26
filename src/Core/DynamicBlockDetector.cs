using System;
using Autodesk.AutoCAD.DatabaseServices;
using AcadRuntime = Autodesk.AutoCAD.Runtime;

namespace CodexBlockLib.Core
{
    /// <summary>动态块识别：块定义中存在 AcDbBlockVisibilityParameter / AcDbBlockLinearParameter 等参数或动作即视为动态块。</summary>
    public static class DynamicBlockDetector
    {
        public static bool IsParameterOrActionClass(string className)
        {
            if (string.IsNullOrEmpty(className)) return false;
            if (!className.StartsWith("AcDbBlock", StringComparison.OrdinalIgnoreCase)) return false;
            if (className.IndexOf("Parameter", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (className.IndexOf("Action", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (className.IndexOf("PropertiesTable", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        public static bool IsDynamicDefinition(BlockTableRecord record)
        {
            if (record == null) return false;

            bool isDynamicRecord = false;
            try { isDynamicRecord = record.IsDynamicBlock; }
            catch { }
            if (isDynamicRecord) return true;

            bool isLayout = false, isAnonymous = false;
            try { isLayout = record.IsLayout; } catch { }
            try { isAnonymous = record.IsAnonymous; } catch { }
            if (isLayout || isAnonymous) return false;

            return ContainsParameterOrAction(record);
        }

        public static bool ContainsParameterOrAction(BlockTableRecord record)
        {
            int scanned = 0;
            try
            {
                foreach (ObjectId id in record)
                {
                    if (++scanned > 5000) break;
                    string name = ClassNameOf(id);
                    if (IsParameterOrActionClass(name)) return true;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("扫描块定义内容失败: " + ex.Message);
            }
            return false;
        }

        public static string ClassNameOf(ObjectId id)
        {
            try
            {
                AcadRuntime.RXClass cls = id.ObjectClass;
                if (cls == null) return null;
                return cls.Name;
            }
            catch
            {
                return null;
            }
        }
    }
}
