using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace BimRoss.RevitIfcGeoExporter
{
    /// <summary>Triangulated mesh in IFC units (meters), with deduplicated vertices.</summary>
    internal class TriMesh
    {
        public readonly List<double[]> Vertices = new List<double[]>();  // [x,y,z] meters
        public readonly List<int[]> Triangles = new List<int[]>();       // 0-based indices

        // Tuple-keyed dict is injective over the quantized grid (no hash collisions).
        private readonly Dictionary<(long, long, long), int> _index = new Dictionary<(long, long, long), int>();

        /// <summary>0.1 mm quantization for vertex-equality test.</summary>
        public const double VertexEpsilonMeters = 0.0001;
        private const double Inv = 1.0 / VertexEpsilonMeters;

        public int AddVertex(double x, double y, double z)
        {
            var key = (
                (long)System.Math.Round(x * Inv),
                (long)System.Math.Round(y * Inv),
                (long)System.Math.Round(z * Inv));
            if (_index.TryGetValue(key, out var existing)) return existing;
            var idx = Vertices.Count;
            Vertices.Add(new[] { x, y, z });
            _index[key] = idx;
            return idx;
        }

        public int VertexCount => Vertices.Count;
        public int TriangleCount => Triangles.Count;
    }

    internal class GeometryWalker
    {
        private readonly Document _doc;
        private readonly ExportOptions _opts;
        private readonly Options _revitOpts;

        public GeometryWalker(Document doc, ExportOptions opts)
        {
            _doc = doc;
            _opts = opts;
            _revitOpts = new Options
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = false,
                DetailLevel = opts.DetailLevel,
            };
        }

        /// <summary>
        /// Tessellate an element into a single mesh in IFC meters.
        /// <paramref name="preTransform"/> is composed in before any walking — used
        /// for linked-model elements (RevitLinkInstance.GetTotalTransform()).
        /// </summary>
        public TriMesh Tessellate(Element el, Transform preTransform = null)
        {
            var geom = el.get_Geometry(_revitOpts);
            if (geom == null) return null;
            var mesh = new TriMesh();
            var xform = preTransform ?? Transform.Identity;
            WalkElement(geom, xform, mesh);
            return mesh;
        }

        /// <summary>
        /// Tessellate a FamilySymbol in its own (symbol-local) coordinate frame.
        /// Used by SymbolCache for IfcMappedItem instance reuse.
        /// </summary>
        public TriMesh TessellateSymbol(FamilySymbol symbol)
        {
            if (symbol == null) return null;
            var geom = symbol.get_Geometry(_revitOpts);
            if (geom == null) return null;
            var mesh = new TriMesh();
            WalkElement(geom, Transform.Identity, mesh);
            return mesh;
        }

        private void WalkElement(GeometryElement geom, Transform xform, TriMesh mesh)
        {
            foreach (GeometryObject obj in geom)
            {
                switch (obj)
                {
                    case Solid s when s.Volume > 0:
                        AddSolid(s, xform, mesh);
                        break;
                    case Mesh m:
                        AddMesh(m, xform, mesh);
                        break;
                    case GeometryInstance gi:
                        var inst = gi.GetInstanceGeometry();
                        if (inst != null) WalkElement(inst, xform, mesh);
                        break;
                    case GeometryElement ge:
                        WalkElement(ge, xform, mesh);
                        break;
                }
            }
        }

        private void AddSolid(Solid solid, Transform xform, TriMesh mesh)
        {
            foreach (Face face in solid.Faces)
            {
                Mesh triMesh;
                try
                {
                    // Triangulate(double) accepts a 0..1 level-of-detail.
                    triMesh = face.Triangulate(_opts.TriangulationLevel);
                }
                catch
                {
                    triMesh = face.Triangulate();
                }
                if (triMesh == null) continue;
                AddMesh(triMesh, xform, mesh);
            }
        }

        private static void AddMesh(Mesh m, Transform xform, TriMesh acc)
        {
            int n = m.NumTriangles;
            for (int i = 0; i < n; i++)
            {
                var t = m.get_Triangle(i);
                var v0 = xform.OfPoint(t.get_Vertex(0));
                var v1 = xform.OfPoint(t.get_Vertex(1));
                var v2 = xform.OfPoint(t.get_Vertex(2));
                int i0 = acc.AddVertex(FeetToM(v0.X), FeetToM(v0.Y), FeetToM(v0.Z));
                int i1 = acc.AddVertex(FeetToM(v1.X), FeetToM(v1.Y), FeetToM(v1.Z));
                int i2 = acc.AddVertex(FeetToM(v2.X), FeetToM(v2.Y), FeetToM(v2.Z));
                if (i0 == i1 || i1 == i2 || i0 == i2) continue; // degenerate
                acc.Triangles.Add(new[] { i0, i1, i2 });
            }
        }

        // Revit internal length unit is feet.
        private static double FeetToM(double feet) => feet * 0.3048;
    }
}
