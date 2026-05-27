using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RB_TypeName.Models;
using RB_TypeName.Services;
using System;
using System.Collections.Generic;

namespace RB_TypeName.Handlers
{
    /// <summary>
    /// Imports a list of <see cref="TypeNumberMappingRecord"/> directly into Revit
    /// Extensible Storage, independent of which rows are currently visible in the UI grid.
    /// Also persists the current Type Source + Discipline settings.
    ///
    /// Inputs:
    ///   <see cref="RecordsToImport"/>         — parsed records from Excel.
    ///   <see cref="OverwriteExisting"/>        — true = override, false = add-only.
    ///   <see cref="SelectedTypeSourceParameterName"/> — current UI TypeSource setting.
    ///   <see cref="SelectedDisciplineCode"/>   — current UI Discipline setting.
    ///
    /// Outputs:
    ///   <see cref="OnCompleted"/> with (result, errorMessage); errorMessage is null on success.
    /// </summary>
    public class ImportTypeNumberMappingsHandler : IExternalEventHandler
    {
        public List<TypeNumberMappingRecord>                       RecordsToImport  { get; set; }
        public bool                                                OverwriteExisting { get; set; }
        public string                                              SelectedTypeSourceParameterName { get; set; } = "Revit Type Name";
        public string                                              SelectedDisciplineCode          { get; set; } = string.Empty;
        public Action<TypeNumberMappingUpsertResult, string>       OnCompleted      { get; set; }

        public void Execute(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null)
            {
                OnCompleted?.Invoke(null, "No active document.");
                return;
            }

            try
            {
                using var t = new Transaction(doc, "Import RBR Type Number Mappings");
                t.Start();

                var upsertResult = TypeNumberMappingExtensibleStorageService.Upsert(
                    doc, RecordsToImport, OverwriteExisting);

                // Persist current UI settings alongside the import.
                TypeNumberSettingsStorageService.Save(doc, new TypeNumberSettings
                {
                    SelectedTypeSourceParameterName = SelectedTypeSourceParameterName,
                    SelectedDisciplineCode          = SelectedDisciplineCode,
                });

                t.Commit();
                OnCompleted?.Invoke(upsertResult, null);
            }
            catch (Exception ex)
            {
                OnCompleted?.Invoke(null, ex.Message);
            }
        }

        public string GetName() => "Import RBR Type Number Mappings";
    }
}
