using System;
using System.Globalization;
using System.IO;
using System.Text;
using Autodesk.Revit.DB;

namespace BimRoss.RevitIfcGeoExporter
{
    /// <summary>
    /// Loads/saves ExportOptions to a per-user JSON file under
    /// %APPDATA%\BimRoss\RevitIfcGeoExporter\options.json so the dialog
    /// preloads the previous run's choices.
    ///
    /// Hand-rolled mini JSON to avoid adding a Json.NET dependency for ~6 fields.
    /// </summary>
    internal static class ExportOptionsStore
    {
        public static string FilePath
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "BimRoss", "RevitIfcGeoExporter");
                return Path.Combine(dir, "options.json");
            }
        }

        public static ExportOptions Load()
        {
            var opts = new ExportOptions();
            try
            {
                if (!File.Exists(FilePath)) return opts;
                var txt = File.ReadAllText(FilePath);
                opts.DetailLevel = ParseDetail(GetVal(txt, "DetailLevel"), opts.DetailLevel);
                opts.TriangulationLevel = ParseDouble(GetVal(txt, "TriangulationLevel"), opts.TriangulationLevel);
                opts.UseInstanceReuse = ParseBool(GetVal(txt, "UseInstanceReuse"), opts.UseInstanceReuse);
                opts.EmitMaterials = ParseBool(GetVal(txt, "EmitMaterials"), opts.EmitMaterials);
                opts.EmitPropertySets = ParseBool(GetVal(txt, "EmitPropertySets"), opts.EmitPropertySets);
                opts.EmitGeoreferencing = ParseBool(GetVal(txt, "EmitGeoreferencing"), opts.EmitGeoreferencing);
                opts.IncludeLinkedModels = ParseBool(GetVal(txt, "IncludeLinkedModels"), opts.IncludeLinkedModels);
                opts.LargeFileWarningMb = (int)ParseDouble(GetVal(txt, "LargeFileWarningMb"), opts.LargeFileWarningMb);
            }
            catch
            {
                // Corrupt file should never block the user — silently fall back to defaults.
            }
            return opts;
        }

        public static void Save(ExportOptions o)
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var sb = new StringBuilder();
                sb.AppendLine("{");
                Field(sb, "DetailLevel", $"\"{o.DetailLevel}\"", true);
                Field(sb, "TriangulationLevel", D(o.TriangulationLevel), true);
                Field(sb, "UseInstanceReuse", B(o.UseInstanceReuse), true);
                Field(sb, "EmitMaterials", B(o.EmitMaterials), true);
                Field(sb, "EmitPropertySets", B(o.EmitPropertySets), true);
                Field(sb, "EmitGeoreferencing", B(o.EmitGeoreferencing), true);
                Field(sb, "IncludeLinkedModels", B(o.IncludeLinkedModels), true);
                Field(sb, "LargeFileWarningMb", o.LargeFileWarningMb.ToString(CultureInfo.InvariantCulture), false);
                sb.AppendLine("}");
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch
            {
                // Persistence is best-effort — never throw out of an export.
            }
        }

        private static void Field(StringBuilder sb, string name, string val, bool trailingComma)
            => sb.AppendLine($"  \"{name}\": {val}{(trailingComma ? "," : "")}");
        private static string B(bool v) => v ? "true" : "false";
        private static string D(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

        private static string GetVal(string json, string key)
        {
            // Tiny single-line value extractor. Tolerates quoted/unquoted scalars.
            var needle = "\"" + key + "\"";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return null;
            i = json.IndexOf(':', i + needle.Length);
            if (i < 0) return null;
            i++;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t')) i++;
            if (i >= json.Length) return null;
            int start = i;
            if (json[i] == '"')
            {
                start = ++i;
                while (i < json.Length && json[i] != '"') i++;
                return json.Substring(start, i - start);
            }
            while (i < json.Length && json[i] != ',' && json[i] != '\n' && json[i] != '\r' && json[i] != '}') i++;
            return json.Substring(start, i - start).Trim();
        }

        private static bool ParseBool(string s, bool dflt)
            => string.IsNullOrEmpty(s) ? dflt : (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1");

        private static double ParseDouble(string s, double dflt)
            => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : dflt;

        private static ViewDetailLevel ParseDetail(string s, ViewDetailLevel dflt)
        {
            if (string.IsNullOrEmpty(s)) return dflt;
            return Enum.TryParse<ViewDetailLevel>(s, true, out var v) ? v : dflt;
        }
    }
}
