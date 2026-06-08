using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Autodesk.Revit.DB;

namespace BimRoss.RevitIfcGeoExporter
{
    /// <summary>
    /// Minimal IFC4 STEP P21 writer.
    ///
    /// Schema: IFC4. One IfcProject → IfcSite → IfcBuilding → IfcBuildingStorey,
    /// with all exported elements as IfcBuildingElementProxy instances contained
    /// in the storey. Geometry is a single IfcTriangulatedFaceSet per element
    /// referencing one IfcCartesianPointList3D.
    ///
    /// This is hand-rolled rather than Xbim-based to keep the add-in dependency-free.
    /// </summary>
    internal static class IfcWriter
    {
        public static void Write(TextWriter w, Document doc, List<ExportedElement> elements)
        {
            var ctx = new WriteCtx(w);
            var now = DateTimeOffset.UtcNow;
            var stamp = now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            var epoch = now.ToUnixTimeSeconds();

            // ----- Header -----
            w.WriteLine("ISO-10303-21;");
            w.WriteLine("HEADER;");
            w.WriteLine($"FILE_DESCRIPTION(('ViewDefinition [ReferenceView_V1.2]'),'2;1');");
            w.WriteLine($"FILE_NAME('{Esc(Path.GetFileName((w as StreamWriter)?.BaseStream is FileStream fs ? fs.Name : "selection.ifc"))}','{stamp}',('BimRoss'),('BimRoss'),'RevitIfcGeoExporter 0.1.0','Revit {Esc(doc.Application.VersionNumber)}','');");
            w.WriteLine("FILE_SCHEMA(('IFC4'));");
            w.WriteLine("ENDSEC;");
            w.WriteLine("DATA;");

            // ----- Common roots -----
            var person = ctx.Add("IFCPERSON($,$,'BimRoss',$,$,$,$,$)");
            var org = ctx.Add("IFCORGANIZATION($,'BimRoss','BimRoss',$,$)");
            var po = ctx.Add($"IFCPERSONANDORGANIZATION(#{person},#{org},$)");
            var app = ctx.Add($"IFCAPPLICATION(#{org},'0.1.0','RevitIfcGeoExporter','BIMROSS.REVITIFCGEOEXPORTER')");
            var owner = ctx.Add($"IFCOWNERHISTORY(#{po},#{app},$,.ADDED.,{epoch},#{po},#{app},{epoch})");

            // Units (SI meter / sq m / cu m / radian).
            var unitM = ctx.Add("IFCSIUNIT(*,.LENGTHUNIT.,$,.METRE.)");
            var unitM2 = ctx.Add("IFCSIUNIT(*,.AREAUNIT.,$,.SQUARE_METRE.)");
            var unitM3 = ctx.Add("IFCSIUNIT(*,.VOLUMEUNIT.,$,.CUBIC_METRE.)");
            var unitRad = ctx.Add("IFCSIUNIT(*,.PLANEANGLEUNIT.,$,.RADIAN.)");
            var units = ctx.Add($"IFCUNITASSIGNMENT((#{unitM},#{unitM2},#{unitM3},#{unitRad}))");

            // Geometric context.
            var origin = ctx.Add("IFCCARTESIANPOINT((0.,0.,0.))");
            var dirZ = ctx.Add("IFCDIRECTION((0.,0.,1.))");
            var dirX = ctx.Add("IFCDIRECTION((1.,0.,0.))");
            var axisWorld = ctx.Add($"IFCAXIS2PLACEMENT3D(#{origin},#{dirZ},#{dirX})");
            var geomCtx = ctx.Add($"IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,1.E-05,#{axisWorld},$)");
            var bodyCtx = ctx.Add($"IFCGEOMETRICREPRESENTATIONSUBCONTEXT('Body','Model',*,*,*,*,#{geomCtx},$,.MODEL_VIEW.,$)");

            // Project + spatial structure.
            var projectGuid = IfcGuid.ToIfcGuid(Guid.NewGuid());
            var project = ctx.Add($"IFCPROJECT('{projectGuid}',#{owner},'{Esc(SafeProjectName(doc))}',$,$,$,$,(#{geomCtx}),#{units})");

            var placementSite = ctx.Add($"IFCLOCALPLACEMENT($,#{axisWorld})");
            var site = ctx.Add($"IFCSITE('{IfcGuid.ToIfcGuid(Guid.NewGuid())}',#{owner},'Default Site',$,$,#{placementSite},$,$,.ELEMENT.,$,$,$,$,$)");

            var placementBuilding = ctx.Add($"IFCLOCALPLACEMENT(#{placementSite},#{axisWorld})");
            var building = ctx.Add($"IFCBUILDING('{IfcGuid.ToIfcGuid(Guid.NewGuid())}',#{owner},'Default Building',$,$,#{placementBuilding},$,$,.ELEMENT.,$,$,$)");

            var placementStorey = ctx.Add($"IFCLOCALPLACEMENT(#{placementBuilding},#{axisWorld})");
            var storey = ctx.Add($"IFCBUILDINGSTOREY('{IfcGuid.ToIfcGuid(Guid.NewGuid())}',#{owner},'Default Storey',$,$,#{placementStorey},$,$,.ELEMENT.,0.)");

            ctx.Add($"IFCRELAGGREGATES('{IfcGuid.ToIfcGuid(Guid.NewGuid())}',#{owner},$,$,#{project},(#{site}))");
            ctx.Add($"IFCRELAGGREGATES('{IfcGuid.ToIfcGuid(Guid.NewGuid())}',#{owner},$,$,#{site},(#{building}))");
            ctx.Add($"IFCRELAGGREGATES('{IfcGuid.ToIfcGuid(Guid.NewGuid())}',#{owner},$,$,#{building},(#{storey}))");

            // ----- Elements -----
            var elemRefs = new List<int>(elements.Count);
            foreach (var e in elements)
            {
                // Vertices → IfcCartesianPointList3D
                var sbPts = new StringBuilder();
                sbPts.Append("(");
                for (int i = 0; i < e.Mesh.Vertices.Count; i++)
                {
                    if (i > 0) sbPts.Append(",");
                    var v = e.Mesh.Vertices[i];
                    sbPts.Append("(").Append(F(v[0])).Append(",").Append(F(v[1])).Append(",").Append(F(v[2])).Append(")");
                }
                sbPts.Append(")");
                var ptList = ctx.Add($"IFCCARTESIANPOINTLIST3D({sbPts})");

                // Triangle indices (1-based in IFC).
                var sbTri = new StringBuilder();
                sbTri.Append("(");
                for (int i = 0; i < e.Mesh.Triangles.Count; i++)
                {
                    if (i > 0) sbTri.Append(",");
                    var t = e.Mesh.Triangles[i];
                    sbTri.Append("(").Append(t[0] + 1).Append(",").Append(t[1] + 1).Append(",").Append(t[2] + 1).Append(")");
                }
                sbTri.Append(")");
                var faceSet = ctx.Add($"IFCTRIANGULATEDFACESET(#{ptList},$,$,{sbTri},$)");

                var shapeRep = ctx.Add($"IFCSHAPEREPRESENTATION(#{bodyCtx},'Body','Tessellation',(#{faceSet}))");
                var prodDef = ctx.Add($"IFCPRODUCTDEFINITIONSHAPE($,$,(#{shapeRep}))");
                var elemPlacement = ctx.Add($"IFCLOCALPLACEMENT(#{placementStorey},#{axisWorld})");

                var guid = IfcGuid.FromRevitUniqueId(e.UniqueId);
                var proxy = ctx.Add(
                    $"IFCBUILDINGELEMENTPROXY('{guid}',#{owner},'{Esc(e.Name)}','{Esc(e.Category)}',$,#{elemPlacement},#{prodDef},$,$)");
                elemRefs.Add(proxy);
            }

            if (elemRefs.Count > 0)
            {
                var sb = new StringBuilder("(");
                for (int i = 0; i < elemRefs.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append("#").Append(elemRefs[i]);
                }
                sb.Append(")");
                ctx.Add($"IFCRELCONTAINEDINSPATIALSTRUCTURE('{IfcGuid.ToIfcGuid(Guid.NewGuid())}',#{owner},$,$,{sb},#{storey})");
            }

            w.WriteLine("ENDSEC;");
            w.WriteLine("END-ISO-10303-21;");
        }

        private static string SafeProjectName(Document doc)
        {
            try { return doc.ProjectInformation?.Name ?? doc.Title ?? "Project"; }
            catch { return "Project"; }
        }

        private static string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            // STEP P21 strings: escape single quote as '' and backslash as \\.
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (c == '\'') sb.Append("''");
                else if (c == '\\') sb.Append(@"\\");
                else if (c < 0x20 || c > 0x7E) sb.Append('?'); // ASCII-safe fallback
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private class WriteCtx
        {
            private readonly TextWriter _w;
            private int _id;
            public WriteCtx(TextWriter w) { _w = w; }
            public int Add(string body)
            {
                _id++;
                _w.WriteLine($"#{_id}={body};");
                return _id;
            }
        }
    }
}
