using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace RB_TypeName.Models
{
    /// <summary>
    /// Configurable source settings for the PBS Excel file.
    /// Persisted as JSON to %LocalAppData%\RK Tools\RB_TypeName\settings.json.
    /// All column fields store Excel column letters (e.g. "R", "S", "K").
    /// </summary>
    public class PbsExcelSourceSettings
    {
        public string ExcelPath                  { get; set; } = string.Empty;
        public string SheetName                  { get; set; } = "PBS";
        public int    HeaderRow                  { get; set; } = 2;    // rows 1-N are headers; data starts at N+1
        public string DisciplineCodeColumn       { get; set; } = "R";
        public string ObjectCodeColumn           { get; set; } = "S";
        public string SecondaryObjectCodeColumn  { get; set; } = "T";
        public string DescriptionColumn          { get; set; } = "K";

        // ── JSON persistence ─────────────────────────────────────────────────

        public static PbsExcelSourceSettings LoadFromFile(string path)
        {
            var s = new PbsExcelSourceSettings();
            if (!File.Exists(path)) return s;

            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                s.ExcelPath                 = ReadString(json, "ExcelPath")                 ?? s.ExcelPath;
                s.SheetName                 = ReadString(json, "SheetName")                 ?? s.SheetName;
                s.HeaderRow                 = ReadInt   (json, "HeaderRow",    s.HeaderRow);
                s.DisciplineCodeColumn      = ReadString(json, "DisciplineCodeColumn")      ?? s.DisciplineCodeColumn;
                s.ObjectCodeColumn          = ReadString(json, "ObjectCodeColumn")          ?? s.ObjectCodeColumn;
                s.SecondaryObjectCodeColumn = ReadString(json, "SecondaryObjectCodeColumn") ?? s.SecondaryObjectCodeColumn;
                s.DescriptionColumn         = ReadString(json, "DescriptionColumn")         ?? s.DescriptionColumn;
            }
            catch { /* return defaults on any parse error */ }

            return s;
        }

        public void SaveToFile(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"ExcelPath\": {Js(ExcelPath)},");
                sb.AppendLine($"  \"SheetName\": {Js(SheetName)},");
                sb.AppendLine($"  \"HeaderRow\": {HeaderRow},");
                sb.AppendLine($"  \"DisciplineCodeColumn\": {Js(DisciplineCodeColumn)},");
                sb.AppendLine($"  \"ObjectCodeColumn\": {Js(ObjectCodeColumn)},");
                sb.AppendLine($"  \"SecondaryObjectCodeColumn\": {Js(SecondaryObjectCodeColumn)},");
                sb.AppendLine($"  \"DescriptionColumn\": {Js(DescriptionColumn)}");
                sb.AppendLine("}");
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch { /* non-critical */ }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// Serialise a string value as a JSON string literal, handling escapes and null.
        private static string Js(string v)
        {
            if (v == null) return "null";
            return "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static string ReadString(string json, string key)
        {
            var m = Regex.Match(json,
                $@"""{Regex.Escape(key)}""\s*:\s*""((?:[^""\\]|\\.)*)""");
            if (!m.Success) return null;
            return m.Groups[1].Value
                .Replace("\\\\", "\\")
                .Replace("\\\"", "\"")
                .Replace("\\/",  "/");
        }

        private static int ReadInt(string json, string key, int fallback)
        {
            var m = Regex.Match(json, $@"""{Regex.Escape(key)}""\s*:\s*(-?\d+)");
            return m.Success && int.TryParse(m.Groups[1].Value, out int v) ? v : fallback;
        }
    }
}
