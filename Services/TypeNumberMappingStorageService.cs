using RB_TypeName.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RB_TypeName.Services
{
    /// <summary>
    /// Persists type-number mapping records (Category|FamilyName|TypeName → L1Code + Notes)
    /// in a per-document NDJSON file in the plugin settings folder.
    /// Does NOT interact with Revit API — safe to call from the UI thread.
    /// </summary>
    public class TypeNumberMappingStorageService
    {
        private readonly string _filePath;
        private readonly Dictionary<string, TypeNumberMappingRecord> _records;

        public TypeNumberMappingStorageService(string filePath)
        {
            _filePath = filePath;
            _records  = LoadFromFile(filePath);
        }

        // ── Public API ───────────────────────────────────────────────────────

        public TypeNumberMappingRecord TryGet(string matchKey)
            => _records.TryGetValue(matchKey, out var r) ? r : null;

        /// <summary>
        /// Fills <see cref="TypeNumberPreviewRow.L1Code"/> from stored mappings for any row
        /// whose L1Code is currently empty (stored values never overwrite user edits in-session).
        /// </summary>
        public void ApplyToRows(IEnumerable<TypeNumberPreviewRow> rows)
        {
            if (rows == null) return;
            foreach (var row in rows)
            {
                if (!string.IsNullOrWhiteSpace(row.L1Code)) continue;
                if (_records.TryGetValue(row.MatchKey, out var rec)
                    && !string.IsNullOrWhiteSpace(rec.L1Code))
                {
                    row.ApplyL1CodeFromMapping(rec.L1Code, "Saved", hasSavedMapping: true);
                }
            }
        }

        /// <summary>
        /// Upserts a record. If <paramref name="overwrite"/> is false and a record already
        /// exists for the same key, the existing record is left unchanged and false is returned.
        /// </summary>
        public bool Upsert(TypeNumberMappingRecord record, bool overwrite)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.MatchKey)) return false;

            if (!overwrite && _records.ContainsKey(record.MatchKey))
                return false;

            record.UpdatedUtc = DateTime.UtcNow.ToString("o");
            _records[record.MatchKey] = record;
            return true;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath));
                var sb = new StringBuilder();
                foreach (var r in _records.Values)
                    sb.AppendLine(SerialiseRecord(r));
                File.WriteAllText(_filePath, sb.ToString(), Encoding.UTF8);
            }
            catch { /* non-critical */ }
        }

        public IReadOnlyCollection<TypeNumberMappingRecord> AllRecords
            => _records.Values;

        // ── Static helpers ───────────────────────────────────────────────────

        public static string GetStorageFilePath(string settingsFolder, string documentPath)
        {
            string key = SanitizeKey(documentPath ?? "default");
            return Path.Combine(settingsFolder, $"TypeNumberMappings_{key}.json");
        }

        public static TypeNumberMappingStorageService Load(string filePath)
            => new TypeNumberMappingStorageService(filePath);

        // ── Private ──────────────────────────────────────────────────────────

        private static Dictionary<string, TypeNumberMappingRecord> LoadFromFile(string path)
        {
            var dict = new Dictionary<string, TypeNumberMappingRecord>(
                StringComparer.OrdinalIgnoreCase);

            if (!File.Exists(path)) return dict;

            try
            {
                foreach (var line in File.ReadLines(path, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var r = ParseRecord(line);
                    if (r != null && !string.IsNullOrWhiteSpace(r.MatchKey))
                        dict[r.MatchKey] = r;
                }
            }
            catch { /* return empty on parse error */ }

            return dict;
        }

        private static TypeNumberMappingRecord ParseRecord(string json)
        {
            var r = new TypeNumberMappingRecord
            {
                // New key fields
                DisciplineCode          = ReadField(json, "DisciplineCode")          ?? string.Empty,
                RbrPrCode               = ReadField(json, "RbrPrCode")               ?? string.Empty,
                TypeSourceParameterName = ReadField(json, "TypeSourceParameterName") ?? string.Empty,
                TypeSourceValue         = ReadField(json, "TypeSourceValue")         ?? string.Empty,
                // Display context
                Category                = ReadField(json, "Category")                ?? string.Empty,
                FamilyName              = ReadField(json, "FamilyName")              ?? string.Empty,
                RevitTypeName           = ReadField(json, "RevitTypeName")           ?? string.Empty,
                // Mapping value
                L1Code                  = ReadField(json, "L1Code")                  ?? string.Empty,
                Notes                   = ReadField(json, "Notes")                   ?? string.Empty,
                // Timestamps
                CreatedUtc              = ReadField(json, "CreatedUtc")              ?? string.Empty,
                UpdatedUtc              = ReadField(json, "UpdatedUtc")              ?? string.Empty,
            };

            // Auto-migrate old records that only have RevitTypeName but not the new key fields.
            if (string.IsNullOrEmpty(r.TypeSourceParameterName)
                && !string.IsNullOrEmpty(r.RevitTypeName))
            {
                r.TypeSourceParameterName = "Revit Type Name";
                r.TypeSourceValue         = r.RevitTypeName;
            }

            return r;
        }

        private static string ReadField(string json, string key)
        {
            var m = Regex.Match(json,
                $"\"{Regex.Escape(key)}\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (!m.Success) return null;
            return JsonUnescape(m.Groups[1].Value);
        }

        private static string JsonUnescape(string s)
            => s.Replace("\\\"", "\"")
                .Replace("\\\\", "\\")
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\t", "\t");

        private static string SerialiseRecord(TypeNumberMappingRecord r)
            => "{"
               + $"\"DisciplineCode\":{Js(r.DisciplineCode)},"
               + $"\"RbrPrCode\":{Js(r.RbrPrCode)},"
               + $"\"TypeSourceParameterName\":{Js(r.TypeSourceParameterName)},"
               + $"\"TypeSourceValue\":{Js(r.TypeSourceValue)},"
               + $"\"L1Code\":{Js(r.L1Code)},"
               + $"\"Notes\":{Js(r.Notes)},"
               + $"\"Category\":{Js(r.Category)},"
               + $"\"FamilyName\":{Js(r.FamilyName)},"
               + $"\"RevitTypeName\":{Js(r.RevitTypeName)},"
               + $"\"CreatedUtc\":{Js(r.CreatedUtc)},"
               + $"\"UpdatedUtc\":{Js(r.UpdatedUtc)}"
               + "}";

        private static string Js(string v)
        {
            var s = (v ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
            return "\"" + s + "\"";
        }

        private static string SanitizeKey(string input)
        {
            string safe = Regex.Replace(input, @"[^A-Za-z0-9_\-]", "_");
            if (safe.Length > 80) safe = safe.Substring(safe.Length - 80);
            return safe;
        }
    }
}
