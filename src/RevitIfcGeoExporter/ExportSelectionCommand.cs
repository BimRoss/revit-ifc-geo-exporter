using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            var doc = uidoc.Document;

            var ids = uidoc.Selection.GetElementIds();
            if (ids == null || ids.Count == 0)
            {
                TaskDialog.Show("Export Selection to IFC",
                    "Select one or more elements before running this command.");
                return Result.Cancelled;
            }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save IFC",
                Filter = "IFC files (*.ifc)|*.ifc",
                FileName = SafeName(doc.Title) + "_selection.ifc",
                DefaultExt = ".ifc",
            };
            if (dlg.ShowDialog() != true) return Result.Cancelled;
            var path = dlg.FileName;

            var walker = new GeometryWalker(doc);
            var exported = new List<ExportedElement>();
            var skipped = 0;

            foreach (var id in ids)
            {
                var el = doc.GetElement(id);
                if (el == null) { skipped++; continue; }
                var mesh = walker.Tessellate(el);
                if (mesh == null || mesh.Triangles.Count == 0) { skipped++; continue; }

                exported.Add(new ExportedElement
                {
                    UniqueId = el.UniqueId,
                    Name = el.Name ?? "Element",
                    Category = el.Category?.Name ?? "Generic",
                    Mesh = mesh,
                });
            }

            if (exported.Count == 0)
            {
                TaskDialog.Show("Export Selection to IFC",
                    "Nothing exportable in selection (no solids/meshes).");
                return Result.Cancelled;
            }

            try
            {
                using (var sw = new StreamWriter(path))
                {
                    IfcWriter.Write(sw, doc, exported);
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }

            TaskDialog.Show("Export Selection to IFC",
                $"Wrote {exported.Count} element(s) to:\n{path}" +
                (skipped > 0 ? $"\n\nSkipped {skipped} with no exportable geometry." : ""));
            return Result.Succeeded;
        }

        private static string SafeName(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return string.IsNullOrWhiteSpace(s) ? "model" : s;
        }
    }

    internal class ExportedElement
    {
        public string UniqueId;
        public string Name;
        public string Category;
        public TriMesh Mesh;
    }
}
