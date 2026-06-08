using System.Collections.Generic;
using System.Globalization;
using Autodesk.Revit.DB;

namespace BimRoss.RevitIfcGeoExporter
{
    /// <summary>
    /// Populates ExportedElement.Properties with a starter set of standard
    /// Pset_*Common entries plus a custom Pset for shared parameters.
    ///
    /// This is intentionally tiny vs. the full buildingSMART Pset library —
    /// covers the highest-value properties for the v2 demo (Mark, Comments,
    /// FireRating, IsExternal, LoadBearing). The standard-Pset table is what
    /// you'd expand against the bSI XML library; the custom-Pset bucket is
    /// the catch-all for parameters that don't map.
    /// </summary>
    internal static class PsetMapping
    {
        public static void Populate(Element el, ExportedElement ee, string ifcType)
        {
            var psetName = StandardCommonPset(ifcType);
            if (psetName != null)
            {
                // v2 pass-1 ships the high-confidence cross-category subset.
                // Category-specific properties (IsExternal, LoadBearing, ThermalTransmittance,
                // AcousticRating, …) are deferred — they need per-category parameter resolution
                // that's its own follow-up. See issue #5.
                TryAddString(el, ee, psetName, "Reference", BuiltInParameter.ALL_MODEL_MARK);
                TryAddString(el, ee, psetName, "FireRating", BuiltInParameter.FIRE_RATING);
                TryAddString(el, ee, psetName, "Description", BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            }

            // Custom Pset for non-builtin shared parameters with values.
            const string custom = "Pset_BimRoss_RevitParameters";
            foreach (Parameter p in el.Parameters)
            {
                if (p == null || !p.HasValue) continue;
                if (p.Definition == null) continue;
                if (!(p.Definition is ExternalDefinition)) continue;     // shared params only
                var name = p.Definition.Name;
                if (string.IsNullOrEmpty(name)) continue;
                switch (p.StorageType)
                {
                    case StorageType.String:
                        Add(ee, custom, name, $"IFCLABEL('{Step.Esc(p.AsString())}')");
                        break;
                    case StorageType.Integer:
                        Add(ee, custom, name, $"IFCINTEGER({p.AsInteger().ToString(CultureInfo.InvariantCulture)})");
                        break;
                    case StorageType.Double:
                        Add(ee, custom, name,
                            $"IFCREAL({p.AsDouble().ToString("0.######", CultureInfo.InvariantCulture)})");
                        break;
                }
            }
        }

        private static string StandardCommonPset(string ifcType)
        {
            switch (ifcType)
            {
                case "IFCWALL": return "Pset_WallCommon";
                case "IFCSLAB": return "Pset_SlabCommon";
                case "IFCROOF": return "Pset_RoofCommon";
                case "IFCCOLUMN": return "Pset_ColumnCommon";
                case "IFCBEAM": return "Pset_BeamCommon";
                case "IFCDOOR": return "Pset_DoorCommon";
                case "IFCWINDOW": return "Pset_WindowCommon";
                case "IFCSTAIR": return "Pset_StairCommon";
                case "IFCRAMP": return "Pset_RampCommon";
                case "IFCCOVERING": return "Pset_CoveringCommon";
                case "IFCRAILING": return "Pset_RailingCommon";
                case "IFCFOOTING": return "Pset_FootingCommon";
                case "IFCBUILDINGELEMENTPROXY": return "Pset_BuildingElementProxyCommon";
                case "IFCSPACE": return "Pset_SpaceCommon";
                case "IFCMEMBER": return "Pset_MemberCommon";
                case "IFCPLATE": return "Pset_PlateCommon";
                case "IFCFURNISHINGELEMENT": return "Pset_FurnitureCommon";
                default: return null;
            }
        }

        private static void TryAddString(Element el, ExportedElement ee, string pset, string name, BuiltInParameter bip)
        {
            var p = el.get_Parameter(bip);
            if (p == null || !p.HasValue) return;
            var s = p.AsString();
            if (string.IsNullOrEmpty(s)) s = p.AsValueString();
            if (string.IsNullOrEmpty(s)) return;
            Add(ee, pset, name, $"IFCLABEL('{Step.Esc(s)}')");
        }

        private static void Add(ExportedElement ee, string pset, string name, string ifcLiteral)
        {
            ee.Properties.Add(new PropertyValue { Pset = pset, Name = name, Value = ifcLiteral });
        }
    }

    internal static class Step
    {
        /// <summary>Escape STEP P21 string literal (already wrapped in single quotes by caller).</summary>
        public static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (c == '\'') sb.Append("''");
                else if (c == '\\') sb.Append(@"\\");
                else if (c < 0x20 || c > 0x7E) sb.Append('?');
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
