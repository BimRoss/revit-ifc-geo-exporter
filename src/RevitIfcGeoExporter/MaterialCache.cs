using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace BimRoss.RevitIfcGeoExporter
{
    /// <summary>
    /// Resolves a "dominant" Revit Material per element and de-duplicates the
    /// resulting MaterialInfo records by Material.UniqueId. v2 is element-level
    /// (one material per element, not per face) — see issue #4 for the per-face
    /// follow-up.
    /// </summary>
    internal class MaterialCache
    {
        private readonly Document _doc;
        private readonly Dictionary<string, MaterialInfo> _byKey = new Dictionary<string, MaterialInfo>();

        public MaterialCache(Document doc) { _doc = doc; }

        public IReadOnlyList<MaterialInfo> Materials
        {
            get
            {
                var list = new List<MaterialInfo>(_byKey.Count);
                foreach (var v in _byKey.Values) list.Add(v);
                return list;
            }
        }

        /// <summary>
        /// Pick a dominant material for the element. Strategy:
        /// 1. First non-null `Material` from the element's `GetMaterialIds(false)`.
        /// 2. Fallback to `Element.Category.Material`.
        /// Returns the cache key (Material.UniqueId), or null if nothing resolved.
        /// </summary>
        public string Resolve(Element el)
        {
            Material mat = null;

            var ids = el.GetMaterialIds(false);
            if (ids != null)
            {
                foreach (var id in ids)
                {
                    var m = _doc.GetElement(id) as Material;
                    if (m != null) { mat = m; break; }
                }
            }

            if (mat == null && el.Category != null) mat = el.Category.Material;
            if (mat == null) return null;

            var key = mat.UniqueId;
            if (!_byKey.ContainsKey(key))
            {
                var c = mat.Color;
                _byKey[key] = new MaterialInfo
                {
                    Key = key,
                    Name = mat.Name ?? "Material",
                    R = (c?.IsValid == true ? c.Red   : (byte)200) / 255.0,
                    G = (c?.IsValid == true ? c.Green : (byte)200) / 255.0,
                    B = (c?.IsValid == true ? c.Blue  : (byte)200) / 255.0,
                    Transparency = System.Math.Min(1.0, System.Math.Max(0.0, mat.Transparency / 100.0)),
                };
            }
            return key;
        }
    }
}
