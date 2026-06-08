using System.Collections.Generic;

namespace BimRoss.RevitIfcGeoExporter
{
    /// <summary>
    /// Pure data DTO consumed by IfcWriter — no Revit API references so the writer
    /// stays testable and the resolution side (Revit-aware) is isolated.
    /// </summary>
    internal class ExportedElement
    {
        public string IfcGuid;             // 22-char IFC GUID (precomputed)
        public string Name;
        public string IfcType;             // e.g. "IFCWALL"
        public string PredefinedType;      // e.g. "FLOOR" — empty string = omit
        public string ObjectType;          // Revit Category name, surfaced as ObjectType

        public string LevelKey;            // "" for default storey
        public TriMesh Mesh;               // null when SymbolKey is set (mapped item)

        public string SymbolKey;           // FamilySymbol.UniqueId — null means inline
        public double[] InstanceMatrix;    // 12-double row-major (M11..M34) world placement for mapped item

        public string MaterialKey;         // Material.UniqueId — null/empty = no style

        public List<PropertyValue> Properties = new List<PropertyValue>();

        public string SourceLinkName;      // null for host doc
    }

    internal struct PropertyValue
    {
        public string Pset;     // e.g. "Pset_WallCommon"
        public string Name;     // e.g. "FireRating"
        public string Value;    // already-escaped STEP P21 literal incl. type wrapper
                                // (e.g. "IFCLABEL('60min')" or "IFCBOOLEAN(.T.)")
    }

    internal class StoreyInfo
    {
        public string Key;             // matches ExportedElement.LevelKey
        public string Name;
        public double ElevationMeters;
        public string IfcGuidSeed;     // Revit Level.UniqueId, or "" for synthetic default
    }

    internal class MaterialInfo
    {
        public string Key;             // Material.UniqueId
        public string Name;
        public double R, G, B;         // 0..1
        public double Transparency;    // 0..1 (IFC convention: 0 = opaque, 1 = fully transparent)
    }

    internal class SymbolInfo
    {
        public string Key;             // FamilySymbol.UniqueId
        public string Name;
        public TriMesh Mesh;           // symbol-local geometry
        public string MaterialKey;     // dominant material of the symbol, if any
    }

    internal class GeoReference
    {
        public double Eastings;
        public double Northings;
        public double OrthogonalHeight;
        public double TrueNorthAngleRad;    // rotation from project to project-N to true-N
        public string ProjectedCrsName;     // "Local" if unknown
    }

    internal class ExportContext
    {
        public string ProjectName;
        public string RevitVersion;
        public List<StoreyInfo> Storeys = new List<StoreyInfo>();
        public List<MaterialInfo> Materials = new List<MaterialInfo>();
        public List<SymbolInfo> Symbols = new List<SymbolInfo>();
        public List<ExportedElement> Elements = new List<ExportedElement>();
        public GeoReference GeoReference; // null = identity placement
    }
}
