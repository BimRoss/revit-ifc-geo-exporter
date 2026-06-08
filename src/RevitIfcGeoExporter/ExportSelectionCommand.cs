using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BimRoss.RevitIfcGeoExporter
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportSelectionCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var host = uidoc.Document;

            var refs = uidoc.Selection.GetReferences();
            var idsHost = uidoc.Selection.GetElementIds();
            if ((refs == null || refs.Count == 0) && (idsHost == null || idsHost.Count == 0))
            {
                TaskDialog.Show("Export Selection to IFC",
                    "Select one or more elements before running this command.");
                return Result.Cancelled;
            }

            // TODO(#11 follow-up): replace defaults with a WinForms/WPF options
            // dialog and persist to %APPDATA%\BimRoss\RevitIfcGeoExporter\options.json.
            var opts = new ExportOptions();

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save IFC",
                Filter = "IFC files (*.ifc)|*.ifc",
                FileName = SafeName(host.Title) + "_selection.ifc",
                DefaultExt = ".ifc",
            };
            if (dlg.ShowDialog() != true) return Result.Cancelled;
            var outPath = dlg.FileName;

            try
            {
                var ctx = Build(host, refs, idsHost, opts);
                using (var sw = new StreamWriter(outPath))
                {
                    IfcWriter.Write(sw, ctx, Path.GetFileName(outPath));
                }
                TaskDialog.Show("Export Selection to IFC",
                    $"Wrote {ctx.Elements.Count} element(s) across {ctx.Storeys.Count} storey/storeys to:\n{outPath}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static ExportContext Build(
            Document host,
            IList<Reference> refs,
            ICollection<ElementId> idsHost,
            ExportOptions opts)
        {
            var ctx = new ExportContext
            {
                ProjectName = host.ProjectInformation?.Name ?? host.Title ?? "Project",
                RevitVersion = host.Application.VersionNumber ?? "Unknown",
                GeoReference = opts.EmitGeoreferencing ? BuildGeoRef(host) : null,
            };

            // Per-document resolution state (host + each link doc has its own).
            var perDoc = new Dictionary<string, DocBundle>();
            DocBundle GetBundle(Document d, string sourceLinkName)
            {
                var key = d.PathName ?? d.Title ?? "host";
                if (!perDoc.TryGetValue(key, out var b))
                {
                    var walker = new GeometryWalker(d, opts);
                    var mats = new MaterialCache(d);
                    var syms = new SymbolCache(walker, mats);
                    b = new DocBundle { Doc = d, Walker = walker, Materials = mats, Symbols = syms,
                                         DocKey = key, SourceLinkName = sourceLinkName };
                    perDoc[key] = b;
                }
                return b;
            }

            // 1) Resolve references into (doc, elementId, linkTransform) triples.
            var jobs = new List<ResolvedRef>();

            if (refs != null && refs.Count > 0)
            {
                foreach (var r in refs)
                {
                    if (r == null) continue;
                    if (r.LinkedElementId != ElementId.InvalidElementId && opts.IncludeLinkedModels)
                    {
                        var linkInst = host.GetElement(r.ElementId) as RevitLinkInstance;
                        var linkDoc = linkInst?.GetLinkDocument();
                        if (linkInst == null || linkDoc == null) continue;
                        jobs.Add(new ResolvedRef
                        {
                            Doc = linkDoc,
                            ElementId = r.LinkedElementId,
                            PreTransform = linkInst.GetTotalTransform(),
                            SourceLinkName = linkInst.Name,
                            CompositeIdSeed = linkInst.UniqueId,
                        });
                    }
                    else
                    {
                        jobs.Add(new ResolvedRef { Doc = host, ElementId = r.ElementId });
                    }
                }
            }
            else
            {
                foreach (var id in idsHost)
                {
                    jobs.Add(new ResolvedRef { Doc = host, ElementId = id });
                }
            }

            // 2) Storey resolution — pool across host + links so we get one IfcBuildingStorey
            //    per (doc, level). Default-storey bucket carries any element without a level.
            var storeyByKey = new Dictionary<string, StoreyInfo>();
            var defaultStorey = new StoreyInfo
            {
                Key = "",
                Name = "Default Storey",
                ElevationMeters = 0.0,
                IfcGuidSeed = "",
            };
            storeyByKey[""] = defaultStorey;

            // 3) Process each element.
            foreach (var job in jobs)
            {
                var el = job.Doc.GetElement(job.ElementId);
                if (el == null) continue;

                var bundle = GetBundle(job.Doc, job.SourceLinkName);
                var mapping = CategoryMap.Resolve(el);

                // Storey key.
                string storeyKey = "";
                var levelId = el.LevelId;
                if (levelId == null || levelId == ElementId.InvalidElementId)
                {
                    // Fall back to nearest-below-by-Z using the doc's Levels.
                    levelId = NearestLevelByZ(job.Doc, el);
                }
                if (levelId != null && levelId != ElementId.InvalidElementId)
                {
                    var lvl = job.Doc.GetElement(levelId) as Level;
                    if (lvl != null)
                    {
                        storeyKey = bundle.DocKey + "|" + lvl.UniqueId;
                        if (!storeyByKey.ContainsKey(storeyKey))
                        {
                            storeyByKey[storeyKey] = new StoreyInfo
                            {
                                Key = storeyKey,
                                Name = lvl.Name ?? "Storey",
                                ElevationMeters = lvl.Elevation * 0.3048,
                                IfcGuidSeed = lvl.UniqueId,
                            };
                        }
                    }
                }

                // Material.
                string materialKey = opts.EmitMaterials ? bundle.Materials.Resolve(el) : null;

                // Decide: mapped-item reuse for FamilyInstance, or inline tessellation.
                string symbolKey = null;
                TriMesh inlineMesh = null;
                double[] instanceMatrix = null;

                if (opts.UseInstanceReuse && el is FamilyInstance fi && job.PreTransform == null)
                {
                    var t = fi.GetTransform();
                    // Mirrored instances flow through IfcCartesianTransformationOperator3D
                    // poorly in many viewers. Fall back to flat tessellation for those.
                    if (t != null && !t.HasReflection)
                    {
                        var key = bundle.Symbols.Register(fi);
                        if (key != null)
                        {
                            symbolKey = bundle.DocKey + "|" + key;
                            instanceMatrix = FlattenTransform(t);
                        }
                    }
                }

                if (symbolKey == null)
                {
                    inlineMesh = bundle.Walker.Tessellate(el, job.PreTransform);
                    if (inlineMesh == null || inlineMesh.TriangleCount == 0) continue;
                }

                // IFC GUID — federated elements get a composite identity so linked
                // re-exports diff against the host export cleanly.
                string ifcGuid = string.IsNullOrEmpty(job.CompositeIdSeed)
                    ? IfcGuid.FromRevitUniqueId(el.UniqueId)
                    : IfcGuid.FromComposite(job.CompositeIdSeed, el.UniqueId);

                var ee = new ExportedElement
                {
                    IfcGuid = ifcGuid,
                    Name = el.Name ?? mapping.EntityName,
                    IfcType = mapping.EntityName,
                    PredefinedType = mapping.PredefinedType,
                    ObjectType = el.Category?.Name ?? "",
                    LevelKey = storeyKey,
                    Mesh = inlineMesh,
                    SymbolKey = symbolKey,
                    InstanceMatrix = instanceMatrix,
                    MaterialKey = materialKey == null ? null : bundle.DocKey + "|" + materialKey,
                    SourceLinkName = job.SourceLinkName,
                };

                if (opts.EmitPropertySets) PsetMapping.Populate(el, ee, mapping.EntityName);
                ctx.Elements.Add(ee);
            }

            if (ctx.Elements.Count == 0)
                throw new InvalidOperationException("Nothing exportable in selection (no solids/meshes).");

            // Merge per-doc materials and symbols into context, keyed by docKey-prefixed id.
            foreach (var b in perDoc.Values)
            {
                foreach (var m in b.Materials.Materials)
                {
                    ctx.Materials.Add(new MaterialInfo
                    {
                        Key = b.DocKey + "|" + m.Key,
                        Name = m.Name, R = m.R, G = m.G, B = m.B, Transparency = m.Transparency,
                    });
                }
                foreach (var s in b.Symbols.Symbols)
                {
                    if (s.Mesh == null || s.Mesh.TriangleCount == 0) continue;
                    ctx.Symbols.Add(new SymbolInfo
                    {
                        Key = b.DocKey + "|" + s.Key,
                        Name = s.Name,
                        Mesh = s.Mesh,
                        MaterialKey = s.MaterialKey == null ? null : b.DocKey + "|" + s.MaterialKey,
                    });
                }
            }

            foreach (var s in storeyByKey.Values) ctx.Storeys.Add(s);
            return ctx;
        }

        private static GeoReference BuildGeoRef(Document doc)
        {
            try
            {
                var pl = doc.ActiveProjectLocation;
                if (pl == null) return null;
                var pos = pl.GetProjectPosition(XYZ.Zero);
                if (pos == null) return null;
                return new GeoReference
                {
                    Eastings = pos.EastWest * 0.3048,
                    Northings = pos.NorthSouth * 0.3048,
                    OrthogonalHeight = pos.Elevation * 0.3048,
                    TrueNorthAngleRad = pos.Angle,
                    ProjectedCrsName = "Local",
                };
            }
            catch { return null; }
        }

        private static ElementId NearestLevelByZ(Document doc, Element el)
        {
            BoundingBoxXYZ bbox = null;
            try { bbox = el.get_BoundingBox(null); } catch { /* ignore */ }
            if (bbox == null) return ElementId.InvalidElementId;
            double z = bbox.Min.Z;
            var coll = new FilteredElementCollector(doc).OfClass(typeof(Level));
            ElementId best = ElementId.InvalidElementId;
            double bestDiff = double.MaxValue;
            foreach (Level lvl in coll)
            {
                if (lvl.Elevation > z + 1e-6) continue;
                var d = z - lvl.Elevation;
                if (d < bestDiff) { bestDiff = d; best = lvl.Id; }
            }
            return best;
        }

        private static double[] FlattenTransform(Transform t)
        {
            // Row-major 3x4: basisX, basisY, basisZ, origin — meters in the placement frame.
            return new[]
            {
                t.BasisX.X, t.BasisY.X, t.BasisZ.X, t.Origin.X * 0.3048,
                t.BasisX.Y, t.BasisY.Y, t.BasisZ.Y, t.Origin.Y * 0.3048,
                t.BasisX.Z, t.BasisY.Z, t.BasisZ.Z, t.Origin.Z * 0.3048,
            };
        }

        private static string SafeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "model";
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        private class DocBundle
        {
            public Document Doc;
            public GeometryWalker Walker;
            public MaterialCache Materials;
            public SymbolCache Symbols;
            public string DocKey;
            public string SourceLinkName;
        }

        private class ResolvedRef
        {
            public Document Doc;
            public ElementId ElementId;
            public Transform PreTransform;      // null = identity (host doc)
            public string SourceLinkName;
            public string CompositeIdSeed;      // RevitLinkInstance.UniqueId — null for host
        }
    }
}
