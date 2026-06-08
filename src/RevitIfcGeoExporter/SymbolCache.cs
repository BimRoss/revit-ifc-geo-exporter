using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace BimRoss.RevitIfcGeoExporter
{
    /// <summary>
    /// Tessellates each FamilySymbol once and caches the resulting symbol-local
    /// mesh so all instances of that symbol can share an IfcRepresentationMap.
    /// Mirrored-instance check is deferred to the writer (negative-determinant
    /// transforms still flow through this cache fine; CartesianTransformationOperator
    /// handles the reflection).
    /// </summary>
    internal class SymbolCache
    {
        private readonly GeometryWalker _walker;
        private readonly MaterialCache _materials;
        private readonly Dictionary<string, SymbolInfo> _byKey = new Dictionary<string, SymbolInfo>();

        public SymbolCache(GeometryWalker walker, MaterialCache materials)
        {
            _walker = walker;
            _materials = materials;
        }

        public IReadOnlyList<SymbolInfo> Symbols
        {
            get
            {
                var list = new List<SymbolInfo>(_byKey.Count);
                foreach (var v in _byKey.Values) list.Add(v);
                return list;
            }
        }

        /// <summary>
        /// Returns the symbol key for reuse, or null if reuse isn't viable
        /// (parameter-driven geometry variant, empty mesh, etc.).
        /// </summary>
        public string Register(FamilyInstance fi)
        {
            if (fi?.Symbol == null) return null;
            var key = fi.Symbol.UniqueId;
            if (_byKey.TryGetValue(key, out var existing))
            {
                return existing.Mesh != null && existing.Mesh.TriangleCount > 0 ? key : null;
            }

            var mesh = _walker.TessellateSymbol(fi.Symbol);
            string materialKey = null;
            if (mesh != null && mesh.TriangleCount > 0)
            {
                materialKey = _materials.Resolve(fi.Symbol);
            }

            _byKey[key] = new SymbolInfo
            {
                Key = key,
                Name = fi.Symbol.Name ?? "Symbol",
                Mesh = mesh,
                MaterialKey = materialKey,
            };
            return mesh != null && mesh.TriangleCount > 0 ? key : null;
        }
    }
}
