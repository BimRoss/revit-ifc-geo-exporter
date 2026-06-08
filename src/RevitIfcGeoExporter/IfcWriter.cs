using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace BimRoss.RevitIfcGeoExporter
{
    /// <summary>
    /// IFC4 STEP P21 writer. Inputs an ExportContext (pure data, no Revit refs)
    /// and emits a complete file. Supports:
    ///   - Multi-storey spatial structure (one IfcBuildingStorey per source Level).
    ///   - Per-element IFC type from CategoryMap (IfcWall / IfcSlab / IfcDoor / …).
    ///   - Materials via IfcStyledItem + IfcSurfaceStyleRendering + IfcColourRgb.
    ///   - Instance reuse via IfcRepresentationMap + IfcMappedItem.
    ///   - Georeferencing via IfcMapConversion + IfcProjectedCRS on the model ctx.
    ///   - Property sets via IfcPropertySet + IfcRelDefinesByProperties.
    ///   - Source-link annotation (custom Pset_BimRoss_Source) per linked element.
    /// </summary>
    internal static class IfcWriter
    {
        public static void Write(TextWriter w, ExportContext ctx, string outFileName)
        {
            var em = new Emitter(w);
            var now = DateTimeOffset.UtcNow;
            var stamp = now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            var epoch = now.ToUnixTimeSeconds();

            // ----- Header -----
            w.WriteLine("ISO-10303-21;");
            w.WriteLine("HEADER;");
            w.WriteLine("FILE_DESCRIPTION(('ViewDefinition [ReferenceView_V1.2]'),'2;1');");
            w.WriteLine($"FILE_NAME('{Step.Esc(outFileName ?? "selection.ifc")}','{stamp}',('BimRoss'),('BimRoss'),'RevitIfcGeoExporter 0.2.0','Revit {Step.Esc(ctx.RevitVersion)}','');");
            w.WriteLine("FILE_SCHEMA(('IFC4'));");
            w.WriteLine("ENDSEC;");
            w.WriteLine("DATA;");

            // ----- Roots: owner history -----
            var person = em.Add("IFCPERSON($,$,'BimRoss',$,$,$,$,$)");
            var org = em.Add("IFCORGANIZATION($,'BimRoss','BimRoss',$,$)");
            var po = em.Add($"IFCPERSONANDORGANIZATION(#{person},#{org},$)");
            var app = em.Add($"IFCAPPLICATION(#{org},'0.2.0','RevitIfcGeoExporter','BIMROSS.REVITIFCGEOEXPORTER')");
            var owner = em.Add($"IFCOWNERHISTORY(#{po},#{app},$,.ADDED.,{epoch},#{po},#{app},{epoch})");

            // ----- Units -----
            var unitM = em.Add("IFCSIUNIT(*,.LENGTHUNIT.,$,.METRE.)");
            var unitM2 = em.Add("IFCSIUNIT(*,.AREAUNIT.,$,.SQUARE_METRE.)");
            var unitM3 = em.Add("IFCSIUNIT(*,.VOLUMEUNIT.,$,.CUBIC_METRE.)");
            var unitRad = em.Add("IFCSIUNIT(*,.PLANEANGLEUNIT.,$,.RADIAN.)");
            var units = em.Add($"IFCUNITASSIGNMENT((#{unitM},#{unitM2},#{unitM3},#{unitRad}))");

            // ----- World axes + contexts -----
            var origin = em.Add("IFCCARTESIANPOINT((0.,0.,0.))");
            var dirZ = em.Add("IFCDIRECTION((0.,0.,1.))");
            var dirX = em.Add("IFCDIRECTION((1.,0.,0.))");
            var axisWorld = em.Add($"IFCAXIS2PLACEMENT3D(#{origin},#{dirZ},#{dirX})");
            var geomCtx = em.Add($"IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,1.E-05,#{axisWorld},$)");

            // ----- Georeferencing (optional) -----
            if (ctx.GeoReference != null)
            {
                var g = ctx.GeoReference;
                var crs = em.Add($"IFCPROJECTEDCRS('{Step.Esc(g.ProjectedCrsName ?? "Local")}',$,$,$,$,$,$)");
                var xAbs = Math.Cos(g.TrueNorthAngleRad);
                var xOrd = Math.Sin(g.TrueNorthAngleRad);
                em.Add(
                    $"IFCMAPCONVERSION(#{geomCtx},#{crs}," +
                    $"{F(g.Eastings)},{F(g.Northings)},{F(g.OrthogonalHeight)}," +
                    $"{F(xAbs)},{F(xOrd)},1.)");
            }

            var bodyCtx = em.Add($"IFCGEOMETRICREPRESENTATIONSUBCONTEXT('Body','Model',*,*,*,*,#{geomCtx},$,.MODEL_VIEW.,$)");

            // ----- Project -----
            var projectGuid = IfcGuid.ToIfcGuid(Guid.NewGuid());
            var project = em.Add(
                $"IFCPROJECT('{projectGuid}',#{owner},'{Step.Esc(ctx.ProjectName)}',$,$,$,$,(#{geomCtx}),#{units})");

            // ----- Site, Building, Storeys -----
            var placementSite = em.Add($"IFCLOCALPLACEMENT($,#{axisWorld})");
            var site = em.Add(
                $"IFCSITE('{IfcGuid.ToIfcGuid(Guid.NewGuid())}',#{owner},'Default Site',$,$,#{placementSite},$,$,.ELEMENT.,$,$,$,$,$)");

            var placementBuilding = em.Add($"IFCLOCALPLACEMENT(#{placementSite},#{axisWorld})");
            var building = em.Add(
                $"IFCBUILDING('{IfcGuid.ToIfcGuid(Guid.NewGuid())}',#{owner},'Default Building',$,$,#{placementBuilding},$,$,.ELEMENT.,$,$,$)");

            // One IfcBuildingStorey per StoreyInfo (incl. the synthetic "" default).
            var storeyById = new Dictionary<string, (int storeyId, int placementId)>();
            foreach (var s in ctx.Storeys)
            {
                var elevAxis = em.Add($"IFCCARTESIANPOINT((0.,0.,{F(s.ElevationMeters)}))");
                var elevPlacementAxis = em.Add($"IFCAXIS2PLACEMENT3D(#{elevAxis},#{dirZ},#{dirX})");
                var sp = em.Add($"IFCLOCALPLACEMENT(#{placementBuilding},#{elevPlacementAxis})");
                var sguid = string.IsNullOrEmpty(s.IfcGuidSeed)
                    ? IfcGuid.ToIfcGuid(Guid.NewGuid())
                    : IfcGuid.FromComposite("storey", s.IfcGuidSeed);
                var storey = em.Add(
                    $"IFCBUILDINGSTOREY('{sguid}',#{owner},'{Step.Esc(s.Name)}',$,$,#{sp},$,$,.ELEMENT.,{F(s.ElevationMeters)})");
                storeyById[s.Key] = (storey, sp);
            }

            em.Add($"IFCRELAGGREGATES('{IfcGuid.ToIfcGuid(Guid.NewGuid())}',#{owner},$,$,#{project},(#{site}))");
            em.Add($"IFCRELAGGREGATES('{IfcGuid.ToIfcGuid(Guid.NewGuid())}',#{owner},$,$,#{site},(#{building}))");
            if (storeyById.Count > 0)
            {
                var sb = new StringBuilder("(");
                bool first = true;
                foreach (var kv in storeyById)
                {
                    if (!first) sb.Append(",");
                    sb.Append("#").Append(kv.Value.storeyId);
                    first = false;
                }
                sb.Append(")");
                em.Add($"IFCRELAGGREGATES('{IfcGuid.ToIfcGuid(Guid.NewGuid())}',#{owner},$,$,#{building},{sb})");
            }

            // ----- Materials → IfcSurfaceStyle -----
            var styleByMatKey = new Dictionary<string, int>();
            foreach (var m in ctx.Materials)
            {
                var colour = em.Add($"IFCCOLOURRGB('{Step.Esc(m.Name)}',{F(m.R)},{F(m.G)},{F(m.B)})");
                var rendering = em.Add(
                    $"IFCSURFACESTYLERENDERING(#{colour},{F(m.Transparency)},$,$,$,$,$,$,.NOTDEFINED.)");
                var surfaceStyle = em.Add(
                    $"IFCSURFACESTYLE('{Step.Esc(m.Name)}',.BOTH.,(#{rendering}))");
                styleByMatKey[m.Key] = surfaceStyle;
            }

            // ----- Symbol meshes → IfcRepresentationMap (for instance reuse) -----
            var repMapByKey = new Dictionary<string, int>();
            foreach (var s in ctx.Symbols)
            {
                if (s.Mesh == null || s.Mesh.TriangleCount == 0) continue;
                var (ptList, faceSet) = EmitMesh(em, s.Mesh);
                if (s.MaterialKey != null && styleByMatKey.TryGetValue(s.MaterialKey, out var style))
                {
                    em.Add($"IFCSTYLEDITEM(#{faceSet},(#{style}),$)");
                }
                var shape = em.Add(
                    $"IFCSHAPEREPRESENTATION(#{bodyCtx},'Body','Tessellation',(#{faceSet}))");
                var mapOrigin = em.Add($"IFCAXIS2PLACEMENT3D(#{origin},#{dirZ},#{dirX})");
                var repMap = em.Add($"IFCREPRESENTATIONMAP(#{mapOrigin},#{shape})");
                repMapByKey[s.Key] = repMap;
            }

            // ----- Per element: geometry + entity -----
            // Group element refs per storey for the trailing IfcRelContainedInSpatialStructure block.
            var elementsByStorey = new Dictionary<string, List<int>>();
            // Group element refs per (Pset name, Pset signature) for IfcRelDefinesByProperties.
            // Signature = name + sorted property names + values, so identical psets dedup.
            var psetGroups = new Dictionary<string, (int psetId, List<int> elemIds)>();

            foreach (var e in ctx.Elements)
            {
                int representationItem;
                string reprType;

                if (e.SymbolKey != null && repMapByKey.TryGetValue(e.SymbolKey, out var repMap))
                {
                    representationItem = EmitMappedItem(em, repMap, e.InstanceMatrix, origin, dirZ, dirX);
                    reprType = "MappedRepresentation";
                }
                else
                {
                    var (ptList, faceSet) = EmitMesh(em, e.Mesh);
                    if (e.MaterialKey != null && styleByMatKey.TryGetValue(e.MaterialKey, out var style))
                    {
                        em.Add($"IFCSTYLEDITEM(#{faceSet},(#{style}),$)");
                    }
                    representationItem = faceSet;
                    reprType = "Tessellation";
                }

                var shape = em.Add(
                    $"IFCSHAPEREPRESENTATION(#{bodyCtx},'Body','{reprType}',(#{representationItem}))");
                var prodDef = em.Add($"IFCPRODUCTDEFINITIONSHAPE($,$,(#{shape}))");

                var storeyPlacement = storeyById.TryGetValue(e.LevelKey ?? "", out var sb2) ? sb2.placementId : placementBuilding;
                var elPlacement = em.Add($"IFCLOCALPLACEMENT(#{storeyPlacement},#{axisWorld})");

                var entityId = EmitElementEntity(em, e, owner, elPlacement, prodDef);

                var storeyKey = e.LevelKey ?? "";
                if (!elementsByStorey.TryGetValue(storeyKey, out var lst))
                {
                    lst = new List<int>();
                    elementsByStorey[storeyKey] = lst;
                }
                lst.Add(entityId);

                if (!string.IsNullOrEmpty(e.SourceLinkName))
                {
                    var prop = em.Add(
                        $"IFCPROPERTYSINGLEVALUE('SourceLink',$,IFCLABEL('{Step.Esc(e.SourceLinkName)}'),$)");
                    var pset = em.Add(
                        $"IFCPROPERTYSET('{IfcGuid.ToIfcGuid(Guid.NewGuid())}',#{owner},'Pset_BimRoss_Source',$,(#{prop}))");
                    em.Add(
                        $"IFCRELDEFINESBYPROPERTIES('{IfcGuid.ToIfcGuid(Guid.NewGuid())}',#{owner},$,$,(#{entityId}),#{pset})");
                }

                // Property-set grouping for IfcRelDefinesByProperties dedup.
                if (e.Properties != null && e.Properties.Count > 0)
                {
                    GroupPropertiesByPset(e.Properties, (psetName, props) =>
                    {
                        var sig = BuildPsetSignature(psetName, props);
                        if (!psetGroups.TryGetValue(sig, out var grp))
                        {
                            var propIds = new List<int>(props.Count);
                            foreach (var pv in props)
                            {
                                propIds.Add(em.Add(
                                    $"IFCPROPERTYSINGLEVALUE('{Step.Esc(pv.Name)}',$,{pv.Value},$)"));
                            }
                            var refs = JoinRefs(propIds);
                            var psetId = em.Add(
                                $"IFCPROPERTYSET('{IfcGuid.ToIfcGuid(Guid.NewGuid())}',#{owner},'{Step.Esc(psetName)}',$,{refs})");
                            grp = (psetId, new List<int>());
                            psetGroups[sig] = grp;
                        }
                        grp.elemIds.Add(entityId);
                        psetGroups[sig] = grp;
                    });
                }
            }

            // ----- Containment: IfcRelContainedInSpatialStructure per storey -----
            foreach (var kv in elementsByStorey)
            {
                if (kv.Value.Count == 0) continue;
                var (storeyId, _) = storeyById[kv.Key];
                em.Add(
                    $"IFCRELCONTAINEDINSPATIALSTRUCTURE('{IfcGuid.ToIfcGuid(Guid.NewGuid())}',#{owner},$,$,{JoinRefs(kv.Value)},#{storeyId})");
            }

            // ----- Property-set links -----
            foreach (var grp in psetGroups.Values)
            {
                em.Add(
                    $"IFCRELDEFINESBYPROPERTIES('{IfcGuid.ToIfcGuid(Guid.NewGuid())}',#{owner},$,$,{JoinRefs(grp.elemIds)},#{grp.psetId})");
            }

            w.WriteLine("ENDSEC;");
            w.WriteLine("END-ISO-10303-21;");
        }

        // ---------- helpers ----------

        private static (int ptListId, int faceSetId) EmitMesh(Emitter em, TriMesh m)
        {
            var sbPts = new StringBuilder("(");
            for (int i = 0; i < m.Vertices.Count; i++)
            {
                if (i > 0) sbPts.Append(",");
                var v = m.Vertices[i];
                sbPts.Append("(").Append(F(v[0])).Append(",").Append(F(v[1])).Append(",").Append(F(v[2])).Append(")");
            }
            sbPts.Append(")");
            var ptList = em.Add($"IFCCARTESIANPOINTLIST3D({sbPts})");

            var sbTri = new StringBuilder("(");
            for (int i = 0; i < m.Triangles.Count; i++)
            {
                if (i > 0) sbTri.Append(",");
                var t = m.Triangles[i];
                sbTri.Append("(").Append(t[0] + 1).Append(",").Append(t[1] + 1).Append(",").Append(t[2] + 1).Append(")");
            }
            sbTri.Append(")");
            var faceSet = em.Add($"IFCTRIANGULATEDFACESET(#{ptList},$,$,{sbTri},$)");
            return (ptList, faceSet);
        }

        private static int EmitMappedItem(Emitter em, int repMapId, double[] mtx,
                                          int worldOrigin, int dirZ, int dirX)
        {
            // mtx is row-major 3x4 (basisX/Y/Z + origin in meters); origin point
            // and axes go into an IfcCartesianTransformationOperator3D.
            if (mtx == null || mtx.Length != 12)
            {
                var idAxis = em.Add($"IFCAXIS2PLACEMENT3D(#{worldOrigin},#{dirZ},#{dirX})");
                var idOp = em.Add($"IFCCARTESIANTRANSFORMATIONOPERATOR3D(#{dirX},$,#{worldOrigin},1.,$)");
                return em.Add($"IFCMAPPEDITEM(#{repMapId},#{idOp})");
            }
            var bxPt = em.Add($"IFCDIRECTION(({F(mtx[0])},{F(mtx[4])},{F(mtx[8])}))");
            var byPt = em.Add($"IFCDIRECTION(({F(mtx[1])},{F(mtx[5])},{F(mtx[9])}))");
            var bzPt = em.Add($"IFCDIRECTION(({F(mtx[2])},{F(mtx[6])},{F(mtx[10])}))");
            var orPt = em.Add($"IFCCARTESIANPOINT(({F(mtx[3])},{F(mtx[7])},{F(mtx[11])}))");
            var op = em.Add(
                $"IFCCARTESIANTRANSFORMATIONOPERATOR3D(#{bxPt},#{byPt},#{orPt},1.,#{bzPt})");
            return em.Add($"IFCMAPPEDITEM(#{repMapId},#{op})");
        }

        private static int EmitElementEntity(Emitter em, ExportedElement e, int owner, int placement, int prodDef)
        {
            var head = $"{e.IfcType}('{e.IfcGuid}',#{owner},'{Step.Esc(e.Name)}',$,'{Step.Esc(e.ObjectType)}',#{placement},#{prodDef},$";
            string tail;
            switch (e.IfcType)
            {
                case "IFCDOOR":
                case "IFCWINDOW":
                    // IFC4 adds: OverallHeight, OverallWidth, PredefinedType, OperationType, UserDefinedOperationType
                    tail = $",$,$,{Pred(e.PredefinedType)},.NOTDEFINED.,$";
                    break;
                case "IFCSPACE":
                    // IFC4 adds: PredefinedType, ElevationWithFlooring
                    tail = $",{Pred(e.PredefinedType)},$";
                    break;
                case "IFCFURNISHINGELEMENT":
                    // No extra attrs beyond IfcElement base.
                    tail = "";
                    break;
                case "IFCDUCTSEGMENT":
                case "IFCPIPESEGMENT":
                case "IFCDUCTFITTING":
                case "IFCPIPEFITTING":
                case "IFCCABLECARRIERSEGMENT":
                case "IFCLIGHTFIXTURE":
                case "IFCELECTRICAPPLIANCE":
                case "IFCSANITARYTERMINAL":
                case "IFCGEOGRAPHICELEMENT":
                    // Distribution flow / terminal elements: one extra PredefinedType attr.
                    tail = $",{Pred(e.PredefinedType)}";
                    break;
                default:
                    // Standard IfcBuildingElement-derived: optional PredefinedType at end.
                    tail = string.IsNullOrEmpty(e.PredefinedType) ? "" : $",{Pred(e.PredefinedType)}";
                    break;
            }
            return em.Add(head + tail + ")");
        }

        private static string Pred(string p) => string.IsNullOrEmpty(p) ? "$" : "." + p + ".";

        private static string JoinRefs(IEnumerable<int> ids)
        {
            var sb = new StringBuilder("(");
            bool first = true;
            foreach (var i in ids)
            {
                if (!first) sb.Append(",");
                sb.Append("#").Append(i);
                first = false;
            }
            sb.Append(")");
            return sb.ToString();
        }

        private static void GroupPropertiesByPset(List<PropertyValue> props, Action<string, List<PropertyValue>> visit)
        {
            var byPset = new Dictionary<string, List<PropertyValue>>();
            foreach (var p in props)
            {
                if (!byPset.TryGetValue(p.Pset, out var list))
                {
                    list = new List<PropertyValue>();
                    byPset[p.Pset] = list;
                }
                list.Add(p);
            }
            foreach (var kv in byPset) visit(kv.Key, kv.Value);
        }

        private static string BuildPsetSignature(string psetName, List<PropertyValue> props)
        {
            // Identical (pset, properties) groups dedup into one shared IfcPropertySet,
            // referenced by multiple elements through one IfcRelDefinesByProperties.
            // Order-independent: sort by name.
            var sb = new StringBuilder(psetName).Append("|");
            var keys = new List<string>(props.Count);
            foreach (var p in props) keys.Add(p.Name + "=" + p.Value);
            keys.Sort(StringComparer.Ordinal);
            foreach (var k in keys) sb.Append(k).Append("|");
            return sb.ToString();
        }

        private static string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

        private class Emitter
        {
            private readonly TextWriter _w;
            private int _id;
            public Emitter(TextWriter w) { _w = w; }
            public int Add(string body)
            {
                _id++;
                _w.WriteLine($"#{_id}={body};");
                return _id;
            }
        }
    }
}
