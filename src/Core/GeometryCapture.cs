using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace CodexBlockLib.Core
{
    /// <summary>缩略图里的文字标注（基点、字高、旋转角）。</summary>
    public sealed class ShapeLabel
    {
        public double X;
        public double Y;
        public double Height = 1.0;
        public double AngleRadians;
        public string Text = string.Empty;
    }

    /// <summary>落影到 XY 平面的二维形状集合，用于生成块缩略图。</summary>
    public sealed class ShapeSet
    {
        public List<Point2d[]> Loops = new List<Point2d[]>();
        public List<Point2d[]> Frames = new List<Point2d[]>();
        public List<ShapeLabel> Labels = new List<ShapeLabel>();

        public int PointCount;
        public double MinX = double.MaxValue;
        public double MinY = double.MaxValue;
        public double MaxX = double.MinValue;
        public double MaxY = double.MinValue;

        public bool HasBounds { get { return MinX <= MaxX && MinY <= MaxY; } }

        public bool HasData { get { return Loops.Count > 0 || Frames.Count > 0 || HasBounds; } }

        public void Include(double x, double y)
        {
            if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y)) return;
            if (x < MinX) MinX = x;
            if (x > MaxX) MaxX = x;
            if (y < MinY) MinY = y;
            if (y > MaxY) MaxY = y;
        }

        public void AddPolyline(IList<Point2d> points)
        {
            if (points == null || points.Count < 2) return;
            var copy = new Point2d[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                copy[i] = points[i];
                Include(points[i].X, points[i].Y);
            }
            PointCount += copy.Length;
            Loops.Add(copy);
        }

        public void AddRectangle(double x0, double y0, double x1, double y1)
        {
            Include(x0, y0);
            Include(x1, y1);
            PointCount += 5;
            Frames.Add(new Point2d[]
            {
                new Point2d(x0, y0), new Point2d(x1, y0), new Point2d(x1, y1), new Point2d(x0, y1), new Point2d(x0, y0)
            });
        }
    }

    internal sealed class CaptureContext
    {
        public Transaction Tr;
        public Database Db;
        public int MaxEntities = 40000;
        public int MaxPoints = 250000;
        public int EntityCount;
        public HashSet<ObjectId> Visited = new HashSet<ObjectId>();
    }

    /// <summary>把块定义（含嵌套块、属性、文字）展开成二维线段，供缩略图使用。</summary>
    public static class GeometryCapture
    {
        public static ShapeSet CaptureBlock(ObjectId btrId, Transaction tr, Database db)
        {
            var shapes = new ShapeSet();
            var ctx = new CaptureContext();
            ctx.Tr = tr;
            ctx.Db = db;
            Walk(btrId, tr, Matrix3d.Identity, shapes, ctx, 0);
            return shapes;
        }

        public static ShapeSet CaptureReference(BlockReference reference, Transaction tr)
        {
            var shapes = new ShapeSet();
            if (reference == null) return shapes;
            var ctx = new CaptureContext();
            ctx.Tr = tr;
            ctx.Db = reference.Database;
            ObjectId btrId = ObjectId.Null;
            try { btrId = reference.AnonymousBlockTableRecord; }
            catch { }
            if (btrId.IsNull)
            {
                try { btrId = reference.BlockTableRecord; }
                catch { }
            }
            if (!btrId.IsNull) Walk(btrId, tr, reference.BlockTransform, shapes, ctx, 0);
            return shapes;
        }

        private static void Walk(ObjectId btrId, Transaction tr, Matrix3d xform, ShapeSet shapes, CaptureContext ctx, int depth)
        {
            if (depth > 4 || btrId.IsNull) return;
            if (ctx.EntityCount > ctx.MaxEntities || shapes.PointCount > ctx.MaxPoints) return;
            if (!ctx.Visited.Add(btrId)) return;

            BlockTableRecord btr = null;
            try { btr = tr.GetObject(btrId, OpenMode.ForRead) as BlockTableRecord; }
            catch (Exception ex) { Log.Warn("读取块定义失败: " + ex.Message); }
            if (btr == null) return;

            try
            {
                if (btr.IsFromExternalReference && btr.XrefStatus != XrefStatus.Resolved) return;
            }
            catch { }

            foreach (ObjectId id in btr)
            {
                if (ctx.EntityCount++ > ctx.MaxEntities) break;
                if (shapes.PointCount > ctx.MaxPoints) break;

                Entity ent = null;
                try { ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity; }
                catch { ent = null; }
                if (ent == null) continue;

                try { if (!ent.Visible) continue; }
                catch { }

                try
                {
                    CaptureEntity(ent, tr, xform, shapes, ctx, depth);
                }
                catch (Exception ex)
                {
                    Log.Warn("展开图元失败(" + ent.GetType().Name + "): " + ex.Message);
                }
            }
        }

        private static void CaptureEntity(Entity ent, Transaction tr, Matrix3d xform, ShapeSet shapes, CaptureContext ctx, int depth)
        {
            BlockReference reference = ent as BlockReference;
            if (reference != null)
            {
                CaptureReferenceInternal(reference, tr, xform, shapes, ctx, depth);
                return;
            }

            Hatch hatch = ent as Hatch;
            if (hatch != null)
            {
                if (CaptureHatch(hatch, xform, shapes)) return;
                AddExtentsFrame(ent, xform, shapes);
                return;
            }

            Curve curve = ent as Curve;
            if (curve != null)
            {
                SampleCurve(curve, xform, shapes);
                return;
            }

            DBText text = ent as DBText;
            if (text != null)
            {
                AddLabel(shapes, xform, text.Position, text.Height, SafeRotation(text.Rotation), text.TextString);
                AddExtentsFrame(ent, xform, shapes);
                return;
            }

            AttributeDefinition attributeDefinition = ent as AttributeDefinition;
            if (attributeDefinition != null)
            {
                string tag = attributeDefinition.TextString;
                if (string.IsNullOrEmpty(tag)) tag = attributeDefinition.Tag;
                AddLabel(shapes, xform, attributeDefinition.Position, attributeDefinition.Height, SafeRotation(attributeDefinition.Rotation), tag);
                return;
            }

            MText mtext = ent as MText;
            if (mtext != null)
            {
                AddLabel(shapes, xform, mtext.Location, mtext.TextHeight, SafeRotation(mtext.Rotation), StripMText(mtext.Text));
                AddExtentsFrame(ent, xform, shapes);
                return;
            }

            AddExtentsFrame(ent, xform, shapes);
        }

        private static void CaptureReferenceInternal(BlockReference reference, Transaction tr, Matrix3d xform, ShapeSet shapes, CaptureContext ctx, int depth)
        {
            Matrix3d child = reference.BlockTransform * xform;

            ObjectId childBtrId = ObjectId.Null;
            try { childBtrId = reference.AnonymousBlockTableRecord; }
            catch { }
            if (childBtrId.IsNull)
            {
                try { childBtrId = reference.BlockTableRecord; }
                catch { }
            }
            if (!childBtrId.IsNull) Walk(childBtrId, tr, child, shapes, ctx, depth + 1);

            double scale = ScaleOf(xform);
            try
            {
                foreach (ObjectId attributeId in reference.AttributeCollection)
                {
                    AttributeReference attribute = tr.GetObject(attributeId, OpenMode.ForRead, false) as AttributeReference;
                    if (attribute == null) continue;
                    if (!string.IsNullOrEmpty(attribute.TextString))
                    {
                        AddLabel(shapes, xform, attribute.Position, attribute.Height, SafeRotation(attribute.Rotation), attribute.TextString);
                    }
                }
            }
            catch { }

            if (scale <= 0.0) scale = 1.0;
        }

        private static void SampleCurve(Curve curve, Matrix3d xform, ShapeSet shapes)
        {
            Line line = curve as Line;
            if (line != null)
            {
                shapes.AddPolyline(new Point2d[] { To2d(line.StartPoint, xform), To2d(line.EndPoint, xform) });
                return;
            }

            double start = 0.0;
            double end = 0.0;
            bool closed = false;
            try
            {
                start = curve.StartParam;
                end = curve.EndParam;
                closed = curve.Closed;
            }
            catch
            {
                return;
            }
            if (double.IsNaN(start) || double.IsNaN(end) || Math.Abs(end - start) < 1e-12) return;

            int steps = closed ? 48 : 24;
            Point2d first = new Point2d(0, 0);
            var points = new List<Point2d>(steps + 2);
            for (int i = 0; i <= steps; i++)
            {
                double t = start + (end - start) * ((double)i / steps);
                Point3d point;
                try { point = curve.GetPointAtParameter(t); }
                catch { return; }
                Point2d twoD = To2d(point, xform);
                if (i == 0) first = twoD;
                points.Add(twoD);
            }
            if (closed) points.Add(first);
            shapes.AddPolyline(points);
        }

        private static bool CaptureHatch(Hatch hatch, Matrix3d xform, ShapeSet shapes)
        {
            bool captured = false;
            try
            {
                int count = hatch.NumberOfLoops;
                for (int i = 0; i < count; i++)
                {
                    HatchLoop loop = hatch.GetLoopAt(i);
                    if (loop == null) continue;
                    if (!loop.IsPolyline) continue;
                    BulgeVertexCollection vertices = loop.Polyline;
                    if (vertices == null || vertices.Count < 2) continue;

                    var points = new List<Point2d>(vertices.Count + 8);
                    for (int k = 0; k < vertices.Count; k++)
                    {
                        BulgeVertex vertex = vertices[k];
                        Point2d startPoint = To2d(new Point3d(vertex.Vertex.X, vertex.Vertex.Y, 0.0), xform);
                        Point2d endPoint = (k + 1 < vertices.Count)
                            ? To2d(new Point3d(vertices[k + 1].Vertex.X, vertices[k + 1].Vertex.Y, 0.0), xform)
                            : To2d(new Point3d(vertices[0].Vertex.X, vertices[0].Vertex.Y, 0.0), xform);
                        points.Add(startPoint);
                        BulgeUtil.AppendArc(points, startPoint, endPoint, vertex.Bulge);
                    }
                    if (points.Count >= 2)
                    {
                        shapes.AddPolyline(points);
                        captured = true;
                    }
                }
            }
            catch
            {
                return false;
            }
            return captured;
        }

        private static void AddExtentsFrame(Entity ent, Matrix3d xform, ShapeSet shapes)
        {
            Extents3d extents;
            try { extents = ent.GeometricExtents; }
            catch { return; }
            Point3d p0 = extents.MinPoint;
            Point3d p1 = extents.MaxPoint;
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Point3d(
                    (i & 1) == 0 ? p0.X : p1.X,
                    (i & 2) == 0 ? p0.Y : p1.Y,
                    (i & 4) == 0 ? p0.Z : p1.Z);
                Point2d flat = To2d(corner, xform);
                if (flat.X < x0) x0 = flat.X;
                if (flat.Y < y0) y0 = flat.Y;
                if (flat.X > x1) x1 = flat.X;
                if (flat.Y > y1) y1 = flat.Y;
            }
            if (x0 > x1 || y0 > y1) return;
            shapes.AddRectangle(x0, y0, x1, y1);
        }

        private static void AddLabel(ShapeSet shapes, Matrix3d xform, Point3d position, double height, double rotation, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            text = text.Trim();
            if (text.Length == 0) return;
            if (text.Length > 40) text = text.Substring(0, 40);

            Point3d world = position.TransformBy(xform);
            Point3d origin = new Point3d(0.0, 0.0, 0.0).TransformBy(xform);
            Vector3d direction = new Point3d(Math.Cos(rotation), Math.Sin(rotation), 0.0).TransformBy(xform) - origin;
            double angle = Math.Atan2(direction.Y, direction.X);
            double scale = direction.Length;
            double worldHeight = height * (scale > 0.0 ? scale : 1.0);

            shapes.Labels.Add(new ShapeLabel
            {
                X = world.X,
                Y = world.Y,
                Height = worldHeight > 0.0 ? worldHeight : 1.0,
                AngleRadians = angle,
                Text = text
            });
            shapes.Include(world.X, world.Y);
        }

        private static double SafeRotation(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0.0;
            return value;
        }

        private static double ScaleOf(Matrix3d xform)
        {
            try
            {
                Point3d origin = new Point3d(0.0, 0.0, 0.0).TransformBy(xform);
                Point3d unit = new Point3d(1.0, 0.0, 0.0).TransformBy(xform);
                return unit.DistanceTo(origin);
            }
            catch
            {
                return 1.0;
            }
        }

        public static Point2d To2d(Point3d point, Matrix3d xform)
        {
            Point3d transformed = point.TransformBy(xform);
            return new Point2d(transformed.X, transformed.Y);
        }

        /// <summary>去掉多行文字的格式代码，便于缩略图显示。</summary>
        public static string StripMText(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\\' && i + 1 < value.Length)
                {
                    char next = value[i + 1];
                    if (next == 'P' || next == 'p') { sb.Append(' '); i++; continue; }
                    // 跳过 \f...; \H...; \C...; 之类的控制码
                    int semi = value.IndexOf(';', i);
                    if (semi > 0 && semi - i <= 12) { i = semi; continue; }
                    i++;
                    continue;
                }
                if (c == '{' || c == '}') continue;
                sb.Append(c);
            }
            string result = sb.ToString().Replace("  ", " ").Trim();
            return result;
        }
    }
}
