using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace BimRoss.RevitIfcGeoExporter
{
    /// <summary>
    /// Triangulated mesh in IFC units (meters). Vertices are de-duplicated.
    /// </summary>
    internal class TriMesh
    {
        public List<double[]> Vertices = new List<double[]>();   // [x,y,z] in meters
        public List<int[]> Triangles = new List<int[]>();        // 0-based indices into Vertices
        private readonly Dictionary<long, int> _index = new Dictionary<long, int>();

        public int AddVertex(double x, double y, double z)
        {
            // Quantize to 0.1mm to dedupe near-duplicate vertices across faces.
            long kx = (long)System.Math.Round(x * 10000.0);
            long ky = (long)System.Math.Round(y * 10000.0);
            long kz = (long)System.Math.Round(z * 10000.0);
            long key = (kx * 73856093L) ^ (ky * 19349663L) ^ (kz * 83492791L);
            if (_index.TryGetValue(key, out var existing)) return existing;
            var idx = Vertices.Count;
            Vertices.Add(new[] { x, y, z });
            _index[key] = idx;
            return idx;
        }
    }

    internal class GeometryWalker
    {
        private readonly Document _doc;
        private static readonly Options Opts = new Options
        {
            ComputeReferences = false,
            IncludeNonVisibleObjects = false,
            DetailLevel = ViewDetailLevel.Fine,
        };

        public GeometryWalker(Document doc) { _doc = doc; }

        public TriMesh Tessellate(Element el)
        {
            var geom = el.get_Geometry(Opts);
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
                var triMesh = face.Triangulate();
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

        // Revit internal units are feet; IFC defaults to meters.
        private static double FeetToM(double feet) => feet * 0.3048;
    }
}
