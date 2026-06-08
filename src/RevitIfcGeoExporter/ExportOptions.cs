using Autodesk.Revit.DB;

namespace BimRoss.RevitIfcGeoExporter
{
    /// <summary>
    /// User-tunable knobs for an export run. Defaults reproduce v0.1.0 behavior
    /// (Fine detail, full tessellation, all features on).
    /// </summary>
    internal class ExportOptions
    {
        public ViewDetailLevel DetailLevel { get; set; } = ViewDetailLevel.Fine;

        /// <summary>0..1 — passed to Face.Triangulate(level). 1 = finest.</summary>
        public double TriangulationLevel { get; set; } = 1.0;

        /// <summary>Emit IfcMappedItem reuse for repeated family instances.</summary>
        public bool UseInstanceReuse { get; set; } = true;

        /// <summary>Emit element materials (IfcStyledItem + IfcSurfaceStyleRendering).</summary>
        public bool EmitMaterials { get; set; } = true;

        /// <summary>Emit standard Pset_* property sets.</summary>
        public bool EmitPropertySets { get; set; } = true;

        /// <summary>Emit IfcMapConversion from Revit project location.</summary>
        public bool EmitGeoreferencing { get; set; } = true;

        /// <summary>Resolve and include selected elements that live in linked models.</summary>
        public bool IncludeLinkedModels { get; set; } = true;

        /// <summary>Warn when output is projected to exceed this size in megabytes.</summary>
        public int LargeFileWarningMb { get; set; } = 100;
    }
}
