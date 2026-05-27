using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace RB_TypeName.Services
{
    /// <summary>
    /// Persists the last-issued running number per type-number prefix.
    /// Stored as a simple JSON file keyed by document path/title.
    /// Prevents number reuse after element types are deleted (append-only numbering).
    /// </summary>
    public class RbrTypeNumberLedgerService
    {
        private readonly string _filePath;
        private readonly Dictionary<string, int> _data;

        public RbrTypeNumberLedgerService(string filePath)
        {
            _filePath = filePath;
            _data     = LoadFromFile(filePath);
        }

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>Returns the last number issued for this prefix. 0 if never issued.</summary>
        public int GetLastIssuedNumber(string prefix)
            => _data.TryGetValue(prefix, out int v) ? v : 0;

        /// <summary>Records that <paramref name="number"/> was issued for <paramref name="prefix"/>.</summary>
        public void UpdateLastIssuedNumber(string prefix, int number)
        {
            if (string.IsNullOrWhiteSpace(prefix)) return;
            if (!_data.TryGetValue(prefix, out int cur) || number > cur)
                _data[prefix] = number;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath));
                var sb = new StringBuilder();
                sb.AppendLine("{");
                var entries = new List<string>();
                foreach (var kv in _data)
                    entries.Add($"  {Js(kv.Key)}: {kv.Value}");
                sb.AppendLine(string.Join(",\n", entries));
                sb.AppendLine("}");
                File.WriteAllText(_filePath, sb.ToString(), Encoding.UTF8);
            }
            catch { /* non-critical */ }
        }

        // ── Static helpers ───────────────────────────────────────────────────

        public static string GetLedgerFilePath(string settingsFolder, string documentPath)
        {
            string key = SanitizeKey(documentPath ?? "default");
            return Path.Combine(settingsFolder, $"TypeNumberLedger_{key}.json");
        }

        // ── Private ──────────────────────────────────────────────────────────

        private static Dictionary<string, int> LoadFromFile(string path)
        {
            var dict = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path)) return dict;
            try
            {
                string json    = File.ReadAllText(path, Encoding.UTF8);
                var    matches = Regex.Matches(json, @"""((?:[^""\\]|\\.)*)""\s*:\s*(\d+)");
                foreach (Match m in matches)
                    if (int.TryParse(m.Groups[2].Value, out int v))
                        dict[m.Groups[1].Value] = v;
            }
            catch { /* return empty on any parse error */ }
            return dict;
        }

        private static string Js(string v)
            => "\"" + (v ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        private static string SanitizeKey(string input)
        {
            string safe = Regex.Replace(input, @"[^A-Za-z0-9_\-]", "_");
            if (safe.Length > 80) safe = safe.Substring(safe.Length - 80);
            return safe;
        }
    }
}
