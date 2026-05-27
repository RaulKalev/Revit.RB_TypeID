using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using RB_TypeName.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RB_TypeName.Services
{
    /// <summary>
    /// Stores RBR type-number mapping records inside the Revit model using
    /// Extensible Storage (a single JSON blob in a DataStorage element).
    ///
    /// Load()  — read-only, no transaction needed.
    /// Save()  — MUST be called inside an active Revit Transaction.
    /// </summary>
    public static class TypeNumberMappingExtensibleStorageService
    {
        // Fixed GUID — never change after first release.
        private static readonly Guid SchemaGuid =
            new Guid("a1b2c3d4-e5f6-4890-abcd-ef1234567890");

        private const string SchemaName        = "RKTools_RbrTypeNumberMappings";
        private const string FieldMappingsJson = "MappingsJson";
        private const string FieldVersion      = "SchemaVersion";
        private const string FieldUpdatedUtc   = "UpdatedUtc";

        // ── Load (no transaction) ─────────────────────────────────────────────

        public static Dictionary<string, TypeNumberMappingRecord> Load(Document doc)
        {
            try
            {
                var schema = GetOrRegisterSchema();
                var ds     = FindDataStorage(doc, schema);
                if (ds == null)
                    return Empty();

                var entity = ds.GetEntity(schema);
                if (!entity.IsValid())
                    return Empty();

                string json = entity.Get<string>(FieldMappingsJson) ?? string.Empty;
                return ParseMappings(json);
            }
            catch
            {
                return Empty();
            }
        }

        // ── Save (must be inside an active Transaction) ───────────────────────

        public static void Save(Document doc, IEnumerable<TypeNumberMappingRecord> records)
        {
            var schema = GetOrRegisterSchema();
            string json = SerializeMappings(records);

            DataStorage ds = FindDataStorage(doc, schema) ?? DataStorage.Create(doc);

            var entity = new Entity(schema);
            entity.Set<string>(FieldMappingsJson, json ?? string.Empty);
            entity.Set<int>(FieldVersion, 1);
            entity.Set<string>(FieldUpdatedUtc, DateTime.UtcNow.ToString("o"));
            ds.SetEntity(entity);
        }

        // ── Upsert (must be inside an active Transaction) ─────────────────────

        /// <summary>
        /// Merges <paramref name="incomingRecords"/> into the existing Extensible Storage database.
        /// When <paramref name="overwrite"/> is <c>true</c>, existing records are replaced.
        /// When <c>false</c>, existing keys are preserved unchanged.
        ///
        /// Notes and CreatedUtc are preserved from the existing record when the incoming
        /// values are empty.
        ///
        /// MUST be called inside an active Revit <see cref="Transaction"/>.
        /// </summary>
        public static TypeNumberMappingUpsertResult Upsert(
            Document doc,
            IEnumerable<TypeNumberMappingRecord> incomingRecords,
            bool overwrite)
        {
            var result   = new TypeNumberMappingUpsertResult();
            var existing = Load(doc);

            foreach (var incoming in incomingRecords ?? Enumerable.Empty<TypeNumberMappingRecord>())
            {
                if (incoming == null
                    || string.IsNullOrWhiteSpace(incoming.MatchKey)
                    || string.IsNullOrWhiteSpace(incoming.L1Code))
                {
                    result.Invalid++;
                    continue;
                }

                incoming.UpdatedUtc = DateTime.UtcNow.ToString("o");

                if (!existing.TryGetValue(incoming.MatchKey, out var old))
                {
                    // New key — always add.
                    if (string.IsNullOrWhiteSpace(incoming.CreatedUtc))
                        incoming.CreatedUtc = incoming.UpdatedUtc;

                    existing[incoming.MatchKey] = incoming;
                    result.Added++;
                    continue;
                }

                if (!overwrite)
                {
                    result.SkippedExisting++;
                    continue;
                }

                // Overwrite — preserve CreatedUtc and Notes when incoming values are empty.
                if (string.IsNullOrWhiteSpace(incoming.CreatedUtc))
                    incoming.CreatedUtc = old.CreatedUtc;

                if (string.IsNullOrWhiteSpace(incoming.Notes))
                    incoming.Notes = old.Notes;

                existing[incoming.MatchKey] = incoming;
                result.Updated++;
            }

            Save(doc, existing.Values);
            return result;
        }

        // ── Apply stored mappings to preview rows ─────────────────────────────

        public static void ApplyToRows(IEnumerable<TypeNumberPreviewRow> rows,
                                       Dictionary<string, TypeNumberMappingRecord> mappings)
        {
            if (rows == null || mappings == null) return;
            foreach (var row in rows)
            {
                if (mappings.TryGetValue(row.MatchKey, out var rec)
                    && !string.IsNullOrWhiteSpace(rec.L1Code))
                {
                    row.ApplyL1CodeFromMapping(rec.L1Code, "Saved", hasSavedMapping: true);
                }
            }
        }

        // ── Migration helper ──────────────────────────────────────────────────

        /// <summary>
        /// Attempts to migrate records from the old per-document NDJSON file into the
        /// new format.  DisciplineCode will be empty; TypeSourceParameterName/Value
        /// are set to "Revit Type Name" / RevitTypeName from the old record.
        /// Returns an empty list if the file does not exist or cannot be read.
        /// </summary>
        public static List<TypeNumberMappingRecord> MigrateFromLocalNdjson(string oldFilePath)
        {
            var result = new List<TypeNumberMappingRecord>();
            if (!System.IO.File.Exists(oldFilePath)) return result;

            try
            {
                var old = TypeNumberMappingStorageService.Load(oldFilePath);
                foreach (var r in old.AllRecords)
                {
                    // Skip records that already have the new key format.
                    if (!string.IsNullOrWhiteSpace(r.TypeSourceParameterName))
                    {
                        result.Add(r);
                        continue;
                    }

                    result.Add(new TypeNumberMappingRecord
                    {
                        DisciplineCode          = string.Empty,
                        RbrPrCode               = r.RbrPrCode,
                        TypeSourceParameterName = "Revit Type Name",
                        TypeSourceValue         = r.RevitTypeName,
                        L1Code                  = r.L1Code,
                        Notes                   = r.Notes,
                        Category                = r.Category,
                        FamilyName              = r.FamilyName,
                        RevitTypeName           = r.RevitTypeName,
                        CreatedUtc              = r.UpdatedUtc,
                        UpdatedUtc              = r.UpdatedUtc,
                    });
                }
            }
            catch { /* return what we have */ }

            return result;
        }

        // ── Schema ────────────────────────────────────────────────────────────

        private static Schema GetOrRegisterSchema()
        {
            var schema = Schema.Lookup(SchemaGuid);
            if (schema != null) return schema;

            var builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName(SchemaName);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField(FieldMappingsJson, typeof(string));
            builder.AddSimpleField(FieldVersion,      typeof(int));
            builder.AddSimpleField(FieldUpdatedUtc,   typeof(string));
            return builder.Finish();
        }

        private static DataStorage FindDataStorage(Document doc, Schema schema)
            => new FilteredElementCollector(doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .FirstOrDefault(ds => ds.GetEntity(schema).IsValid());

        // ── JSON serialisation ────────────────────────────────────────────────

        private static string SerializeMappings(IEnumerable<TypeNumberMappingRecord> records)
        {
            var sb = new StringBuilder();
            sb.Append("{\"Version\":1,\"Mappings\":[");
            bool first = true;
            foreach (var r in records ?? Enumerable.Empty<TypeNumberMappingRecord>())
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('{');
                sb.Append($"\"DisciplineCode\":{Js(r.DisciplineCode)},");
                sb.Append($"\"RbrPrCode\":{Js(r.RbrPrCode)},");
                sb.Append($"\"TypeSourceParameterName\":{Js(r.TypeSourceParameterName)},");
                sb.Append($"\"TypeSourceValue\":{Js(r.TypeSourceValue)},");
                sb.Append($"\"L1Code\":{Js(r.L1Code)},");
                sb.Append($"\"Notes\":{Js(r.Notes)},");
                sb.Append($"\"Category\":{Js(r.Category)},");
                sb.Append($"\"FamilyName\":{Js(r.FamilyName)},");
                sb.Append($"\"RevitTypeName\":{Js(r.RevitTypeName)},");
                sb.Append($"\"LastSeenElementTypeId\":{r.LastSeenElementTypeId},");
                sb.Append($"\"CreatedUtc\":{Js(r.CreatedUtc)},");
                sb.Append($"\"UpdatedUtc\":{Js(r.UpdatedUtc)}");
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static Dictionary<string, TypeNumberMappingRecord> ParseMappings(string json)
        {
            var dict = new Dictionary<string, TypeNumberMappingRecord>(
                StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(json)) return dict;

            // Extract the Mappings array content.
            var mArr = Regex.Match(json, @"""Mappings""\s*:\s*\[(.+)\]",
                RegexOptions.Singleline);
            if (!mArr.Success) return dict;

            // Split on the boundary between JSON objects: },{ 
            string content = mArr.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(content)) return dict;

            // Find each {...} object (values don't contain { or }).
            var objects = Regex.Matches(content, @"\{[^{}]*\}");
            foreach (Match m in objects)
            {
                var r = ParseRecord(m.Value);
                if (r != null && !string.IsNullOrWhiteSpace(r.MatchKey))
                    dict[r.MatchKey] = r;
            }
            return dict;
        }

        private static TypeNumberMappingRecord ParseRecord(string obj)
        {
            return new TypeNumberMappingRecord
            {
                DisciplineCode          = ReadField(obj, "DisciplineCode")          ?? string.Empty,
                RbrPrCode               = ReadField(obj, "RbrPrCode")               ?? string.Empty,
                TypeSourceParameterName = ReadField(obj, "TypeSourceParameterName") ?? string.Empty,
                TypeSourceValue         = ReadField(obj, "TypeSourceValue")         ?? string.Empty,
                L1Code                  = ReadField(obj, "L1Code")                  ?? string.Empty,
                Notes                   = ReadField(obj, "Notes")                   ?? string.Empty,
                Category                = ReadField(obj, "Category")                ?? string.Empty,
                FamilyName              = ReadField(obj, "FamilyName")              ?? string.Empty,
                RevitTypeName           = ReadField(obj, "RevitTypeName")           ?? string.Empty,
                LastSeenElementTypeId   = ReadLong(obj,  "LastSeenElementTypeId"),
                CreatedUtc              = ReadField(obj, "CreatedUtc")              ?? string.Empty,
                UpdatedUtc              = ReadField(obj, "UpdatedUtc")              ?? string.Empty,
            };
        }

        private static string ReadField(string json, string key)
        {
            var m = Regex.Match(json,
                $"\"{Regex.Escape(key)}\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            return m.Success ? JsonUnescape(m.Groups[1].Value) : null;
        }

        private static long ReadLong(string json, string key)
        {
            var m = Regex.Match(json,
                $"\"{Regex.Escape(key)}\"\\s*:\\s*(-?\\d+)");
            return m.Success && long.TryParse(m.Groups[1].Value, out long v) ? v : 0;
        }

        private static string JsonUnescape(string s)
            => s.Replace("\\\"", "\"")
                .Replace("\\\\", "\\")
                .Replace("\\n",  "\n")
                .Replace("\\r",  "\r")
                .Replace("\\t",  "\t");

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

        private static Dictionary<string, TypeNumberMappingRecord> Empty()
            => new Dictionary<string, TypeNumberMappingRecord>(StringComparer.OrdinalIgnoreCase);
    }
}
