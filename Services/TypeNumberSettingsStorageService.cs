using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace RB_TypeName.Services
{
    public class TypeNumberSettings
    {
        public string SelectedTypeSourceParameterName { get; set; } = "Revit Type Name";
        public string SelectedDisciplineCode          { get; set; } = string.Empty;
    }

    /// <summary>
    /// Persists Type Numbers tab UI settings (selected TypeSource parameter, selected
    /// discipline) in Revit Extensible Storage.
    ///
    /// Load()  — no transaction required.
    /// Save()  — MUST be called inside an active Revit Transaction.
    /// </summary>
    public static class TypeNumberSettingsStorageService
    {
        private static readonly Guid SchemaGuid =
            new Guid("b2c3d4e5-f6a7-4901-8cde-f12345678901");

        private const string SchemaName         = "RKTools_RbrTypeNumberSettings";
        private const string FieldTypeSrcParam  = "SelectedTypeSourceParameterName";
        private const string FieldDiscipline    = "SelectedDisciplineCode";
        private const string FieldVersion       = "SchemaVersion";
        private const string FieldUpdatedUtc    = "UpdatedUtc";

        // ── Load ──────────────────────────────────────────────────────────────

        public static TypeNumberSettings Load(Document doc)
        {
            try
            {
                var schema = GetOrRegisterSchema();
                var ds     = FindDataStorage(doc, schema);
                if (ds == null) return new TypeNumberSettings();

                var entity = ds.GetEntity(schema);
                if (!entity.IsValid()) return new TypeNumberSettings();

                string paramName = entity.Get<string>(FieldTypeSrcParam) ?? "Revit Type Name";
                string discipline = entity.Get<string>(FieldDiscipline)  ?? string.Empty;

                return new TypeNumberSettings
                {
                    SelectedTypeSourceParameterName = string.IsNullOrWhiteSpace(paramName)
                        ? "Revit Type Name" : paramName,
                    SelectedDisciplineCode = discipline,
                };
            }
            catch
            {
                return new TypeNumberSettings();
            }
        }

        // ── Save (inside active Transaction) ──────────────────────────────────

        public static void Save(Document doc, TypeNumberSettings settings)
        {
            var schema = GetOrRegisterSchema();
            DataStorage ds = FindDataStorage(doc, schema) ?? DataStorage.Create(doc);

            var entity = new Entity(schema);
            entity.Set<string>(FieldTypeSrcParam,
                settings?.SelectedTypeSourceParameterName ?? "Revit Type Name");
            entity.Set<string>(FieldDiscipline,
                settings?.SelectedDisciplineCode ?? string.Empty);
            entity.Set<int>(FieldVersion, 1);
            entity.Set<string>(FieldUpdatedUtc, DateTime.UtcNow.ToString("o"));
            ds.SetEntity(entity);
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
            builder.AddSimpleField(FieldTypeSrcParam, typeof(string));
            builder.AddSimpleField(FieldDiscipline,   typeof(string));
            builder.AddSimpleField(FieldVersion,      typeof(int));
            builder.AddSimpleField(FieldUpdatedUtc,   typeof(string));
            return builder.Finish();
        }

        private static DataStorage FindDataStorage(Document doc, Schema schema)
            => new FilteredElementCollector(doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .FirstOrDefault(ds => ds.GetEntity(schema).IsValid());
    }
}
