using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace BimRoss.RevitIfcGeoExporter
{
    /// <summary>
    /// Revit BuiltInCategory → IFC4 entity name + PredefinedType.
    /// Anything not listed falls back to IfcBuildingElementProxy.
    /// </summary>
    internal static class CategoryMap
    {
        // Names are uppercase to match the casing required by STEP P21 entity types.
        // PredefinedType is the enum name (without dots) — emitter wraps it in dots.
        // PredefinedType "" means: don't emit a trailing predefined-type attribute.
        public struct IfcMapping
        {
            public string EntityName;
            public string PredefinedType;
            public IfcMapping(string e, string p) { EntityName = e; PredefinedType = p; }
        }

        private static readonly IfcMapping Default = new IfcMapping("IFCBUILDINGELEMENTPROXY", "NOTDEFINED");

        private static readonly Dictionary<BuiltInCategory, IfcMapping> Map = new Dictionary<BuiltInCategory, IfcMapping>
        {
            { BuiltInCategory.OST_Walls,              new IfcMapping("IFCWALL",           "NOTDEFINED") },
            { BuiltInCategory.OST_StructuralFraming,  new IfcMapping("IFCBEAM",           "NOTDEFINED") },
            { BuiltInCategory.OST_Columns,            new IfcMapping("IFCCOLUMN",         "NOTDEFINED") },
            { BuiltInCategory.OST_StructuralColumns,  new IfcMapping("IFCCOLUMN",         "NOTDEFINED") },
            { BuiltInCategory.OST_Floors,             new IfcMapping("IFCSLAB",           "FLOOR") },
            { BuiltInCategory.OST_Roofs,              new IfcMapping("IFCROOF",           "NOTDEFINED") },
            { BuiltInCategory.OST_Ceilings,           new IfcMapping("IFCCOVERING",       "CEILING") },
            { BuiltInCategory.OST_Doors,              new IfcMapping("IFCDOOR",           "NOTDEFINED") },
            { BuiltInCategory.OST_Windows,            new IfcMapping("IFCWINDOW",         "NOTDEFINED") },
            { BuiltInCategory.OST_Stairs,             new IfcMapping("IFCSTAIR",          "NOTDEFINED") },
            { BuiltInCategory.OST_Ramps,              new IfcMapping("IFCRAMP",           "NOTDEFINED") },
            { BuiltInCategory.OST_CurtainWallPanels,  new IfcMapping("IFCPLATE",          "CURTAIN_PANEL") },
            { BuiltInCategory.OST_CurtainWallMullions,new IfcMapping("IFCMEMBER",         "MULLION") },
            { BuiltInCategory.OST_Furniture,          new IfcMapping("IFCFURNISHINGELEMENT", "") },
            { BuiltInCategory.OST_GenericModel,       new IfcMapping("IFCBUILDINGELEMENTPROXY", "NOTDEFINED") },
            { BuiltInCategory.OST_MEPSpaces,          new IfcMapping("IFCSPACE",          "SPACE") },
            { BuiltInCategory.OST_DuctCurves,         new IfcMapping("IFCDUCTSEGMENT",    "RIGIDSEGMENT") },
            { BuiltInCategory.OST_PipeCurves,         new IfcMapping("IFCPIPESEGMENT",    "RIGIDSEGMENT") },
            { BuiltInCategory.OST_DuctFitting,        new IfcMapping("IFCDUCTFITTING",    "NOTDEFINED") },
            { BuiltInCategory.OST_PipeFitting,        new IfcMapping("IFCPIPEFITTING",    "NOTDEFINED") },
            { BuiltInCategory.OST_Conduit,            new IfcMapping("IFCCABLECARRIERSEGMENT", "CONDUITSEGMENT") },
            { BuiltInCategory.OST_LightingFixtures,   new IfcMapping("IFCLIGHTFIXTURE",   "NOTDEFINED") },
            { BuiltInCategory.OST_ElectricalFixtures, new IfcMapping("IFCELECTRICAPPLIANCE", "NOTDEFINED") },
            { BuiltInCategory.OST_PlumbingFixtures,   new IfcMapping("IFCSANITARYTERMINAL","NOTDEFINED") },
            { BuiltInCategory.OST_MechanicalEquipment,new IfcMapping("IFCBUILDINGELEMENTPROXY", "NOTDEFINED") },
            { BuiltInCategory.OST_Railings,           new IfcMapping("IFCRAILING",        "NOTDEFINED") },
            { BuiltInCategory.OST_Site,               new IfcMapping("IFCGEOGRAPHICELEMENT","TERRAIN") },
            { BuiltInCategory.OST_StructuralFoundation, new IfcMapping("IFCFOOTING",      "NOTDEFINED") },
        };

        public static IfcMapping Resolve(Element el)
        {
            if (el?.Category == null) return Default;
            var bic = (BuiltInCategory)el.Category.Id.IntegerValue;
            return Map.TryGetValue(bic, out var m) ? m : Default;
        }
    }
}
