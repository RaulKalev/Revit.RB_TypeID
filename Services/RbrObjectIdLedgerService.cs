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
    /// Stores and retrieves the RBR Object ID ownership ledger from Revit Extensible Storage.
    ///
    /// Load()  — read-only; no transaction needed.
    /// Save()  — MUST be called inside an active Revit Transaction.
    /// </summary>
    public static class RbrObjectIdLedgerService
    {
        // Fixed GUID — never change after first release.
        private static readonly Guid SchemaGuid =
            new Guid("a8e3c2b1-f4d5-4e6a-8c7b-9d0e1f2a3b4c");

        private const string SchemaName      = "RKTools_RbrObjectIdLedger";
        private const string FieldLedgerJson = "LedgerJson";
        private const string FieldVersion    = "SchemaVersion";
        private const string FieldUpdatedUtc = "UpdatedUtc";

        // ── Load (no transaction) ─────────────────────────────────────────────

        public static ObjectIdLedgerData Load(Document doc)
        {
            try
            {
                var schema = GetOrRegisterSchema();
                var ds     = FindDataStorage(doc, schema);
                if (ds == null) return Empty();

                var entity = ds.GetEntity(schema);
                if (!entity.IsValid()) return Empty();

                string json = entity.Get<string>(FieldLedgerJson) ?? string.Empty;
                return ParseLedger(json);
            }
            catch
            {
                return Empty();
            }
        }

        // ── Save (must be inside an active Transaction) ───────────────────────

        public static void Save(Document doc, ObjectIdLedgerData ledger)
        {
            if (ledger == null) ledger = Empty();

            var schema = GetOrRegisterSchema();
            string json = SerializeLedger(ledger);

            DataStorage ds = FindDataStorage(doc, schema) ?? DataStorage.Create(doc);

            var entity = new Entity(schema);
            entity.Set<string>(FieldLedgerJson, json ?? string.Empty);
            entity.Set<int>(FieldVersion, 1);
            entity.Set<string>(FieldUpdatedUtc, DateTime.UtcNow.ToString("o"));
            ds.SetEntity(entity);
        }

        // ── Query helpers ─────────────────────────────────────────────────────

        public static ObjectIdLedgerRecord FindByObjectId(ObjectIdLedgerData ledger, string objectId)
        {
            if (ledger == null || string.IsNullOrWhiteSpace(objectId)) return null;
            return ledger.Records.FirstOrDefault(r =>
                string.Equals(r.ObjectId, objectId, StringComparison.OrdinalIgnoreCase));
        }

        public static ObjectIdLedgerRecord FindByUniqueId(ObjectIdLedgerData ledger, string uniqueId)
        {
            if (ledger == null || string.IsNullOrWhiteSpace(uniqueId)) return null;
            return ledger.Records.FirstOrDefault(r =>
                string.Equals(r.ElementUniqueId, uniqueId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Adds or updates the record for <paramref name="record"/>.ElementUniqueId.
        /// Does not save to Extensible Storage — call Save() separately inside a transaction.
        /// </summary>
        public static void UpsertRecord(ObjectIdLedgerData ledger, ObjectIdLedgerRecord record)
        {
            if (ledger == null || record == null) return;

            var existing = FindByUniqueId(ledger, record.ElementUniqueId);
            if (existing != null)
            {
                ledger.Records.Remove(existing);
            }
            ledger.Records.Add(record);
        }

        /// <summary>
        /// Marks an Object ID as retired (never reuse).
        /// Does not save to Extensible Storage — call Save() separately inside a transaction.
        /// </summary>
        public static void RetireObjectId(ObjectIdLedgerData ledger, string objectId)
        {
            if (ledger == null || string.IsNullOrWhiteSpace(objectId)) return;
            if (!ledger.RetiredObjectIds.Contains(objectId, StringComparer.OrdinalIgnoreCase))
                ledger.RetiredObjectIds.Add(objectId);
        }

        /// <summary>Returns all Object IDs currently known to the ledger (active + retired).</summary>
        public static HashSet<string> GetReservedObjectIds(ObjectIdLedgerData ledger)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (ledger == null) return set;
            foreach (var r in ledger.Records)
                if (!string.IsNullOrWhiteSpace(r.ObjectId))
                    set.Add(r.ObjectId);
            foreach (var id in ledger.RetiredObjectIds)
                if (!string.IsNullOrWhiteSpace(id))
                    set.Add(id);
            return set;
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
            builder.AddSimpleField(FieldLedgerJson, typeof(string));
            builder.AddSimpleField(FieldVersion,    typeof(int));
            builder.AddSimpleField(FieldUpdatedUtc, typeof(string));
            return builder.Finish();
        }

        private static DataStorage FindDataStorage(Document doc, Schema schema)
            => new FilteredElementCollector(doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .FirstOrDefault(ds => ds.GetEntity(schema).IsValid());

        // ── JSON serialisation ────────────────────────────────────────────────
        // Uses manual string building to avoid adding external JSON library dependency.
        // PreviousObjectIds and RetiredObjectIds are stored as pipe-separated strings.

        private static string SerializeLedger(ObjectIdLedgerData ledger)
        {
            var sb = new StringBuilder();
            sb.Append("{\"Version\":1,");
            sb.Append($"\"RetiredObjectIds\":{Js(string.Join("|", ledger.RetiredObjectIds ?? new List<string>()))},");
            sb.Append("\"Records\":[");
            bool first = true;
            foreach (var r in ledger.Records ?? Enumerable.Empty<ObjectIdLedgerRecord>())
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('{');
                sb.Append($"\"ObjectId\":{Js(r.ObjectId)},");
                sb.Append($"\"ElementUniqueId\":{Js(r.ElementUniqueId)},");
                sb.Append($"\"ElementId\":{r.ElementId},");
                sb.Append($"\"RbrPrCode\":{Js(r.RbrPrCode)},");
                sb.Append($"\"PbsPartR\":{Js(r.PbsPartR)},");
                sb.Append($"\"PbsPartS\":{Js(r.PbsPartS)},");
                sb.Append($"\"LevelCode\":{Js(r.LevelCode)},");
                sb.Append($"\"Prefix\":{Js(r.Prefix)},");
                sb.Append($"\"Category\":{Js(r.Category)},");
                sb.Append($"\"FamilyName\":{Js(r.FamilyName)},");
                sb.Append($"\"TypeId\":{r.TypeId},");
                sb.Append($"\"TypeName\":{Js(r.TypeName)},");
                sb.Append($"\"Fingerprint\":{Js(r.Fingerprint)},");
                sb.Append($"\"PreviousObjectIds\":{Js(string.Join("|", r.PreviousObjectIds ?? new List<string>()))},");
                sb.Append($"\"AssignedUtc\":{Js(r.AssignedUtc)},");
                sb.Append($"\"UpdatedUtc\":{Js(r.UpdatedUtc)}");
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static ObjectIdLedgerData ParseLedger(string json)
        {
            var data = Empty();
            if (string.IsNullOrWhiteSpace(json)) return data;

            // Parse RetiredObjectIds
            string retiredRaw = ReadField(json, "RetiredObjectIds");
            if (!string.IsNullOrWhiteSpace(retiredRaw))
            {
                foreach (var id in retiredRaw.Split('|'))
                    if (!string.IsNullOrWhiteSpace(id))
                        data.RetiredObjectIds.Add(id.Trim());
            }

            // Parse Records array: find outer [...] and split on flat {…} objects
            var arrMatch = Regex.Match(json, @"""Records""\s*:\s*\[(.+)\]",
                RegexOptions.Singleline);
            if (!arrMatch.Success) return data;

            string content = arrMatch.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(content)) return data;

            var objects = Regex.Matches(content, @"\{[^{}]*\}");
            foreach (Match m in objects)
            {
                var r = ParseRecord(m.Value);
                if (r != null && !string.IsNullOrWhiteSpace(r.ElementUniqueId))
                    data.Records.Add(r);
            }

            return data;
        }

        private static ObjectIdLedgerRecord ParseRecord(string obj)
        {
            var r = new ObjectIdLedgerRecord
            {
                ObjectId        = ReadField(obj, "ObjectId")        ?? string.Empty,
                ElementUniqueId = ReadField(obj, "ElementUniqueId") ?? string.Empty,
                ElementId       = ReadLong(obj,  "ElementId"),
                RbrPrCode       = ReadField(obj, "RbrPrCode")       ?? string.Empty,
                PbsPartR        = ReadField(obj, "PbsPartR")        ?? string.Empty,
                PbsPartS        = ReadField(obj, "PbsPartS")        ?? string.Empty,
                LevelCode       = ReadField(obj, "LevelCode")       ?? string.Empty,
                Prefix          = ReadField(obj, "Prefix")          ?? string.Empty,
                Category        = ReadField(obj, "Category")        ?? string.Empty,
                FamilyName      = ReadField(obj, "FamilyName")      ?? string.Empty,
                TypeId          = ReadLong(obj,  "TypeId"),
                TypeName        = ReadField(obj, "TypeName")        ?? string.Empty,
                Fingerprint     = ReadField(obj, "Fingerprint")     ?? string.Empty,
                AssignedUtc     = ReadField(obj, "AssignedUtc")     ?? string.Empty,
                UpdatedUtc      = ReadField(obj, "UpdatedUtc")      ?? string.Empty,
            };

            string prevRaw = ReadField(obj, "PreviousObjectIds");
            if (!string.IsNullOrWhiteSpace(prevRaw))
            {
                foreach (var id in prevRaw.Split('|'))
                    if (!string.IsNullOrWhiteSpace(id))
                        r.PreviousObjectIds.Add(id.Trim());
            }

            return r;
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

        private static ObjectIdLedgerData Empty() => new ObjectIdLedgerData();
    }
}
