using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RB_TypeName.Models;
using RB_TypeName.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RB_TypeName.Handlers
{
    /// <summary>
    /// Saves the current L1Code mappings from the Type Numbers preview rows
    /// into Revit Extensible Storage.
    ///
    /// Inputs:  <see cref="Rows"/> — the current preview rows.
    /// Outputs: <see cref="OnCompleted"/> callback with (savedCount, errorMessage).
    ///          errorMessage is null on success.
    /// </summary>
    public class SaveTypeNumberMappingsHandler : IExternalEventHandler
    {
        public List<TypeNumberPreviewRow> Rows       { get; set; }
        public Action<int, string>        OnCompleted { get; set; }

        public void Execute(UIApplication app)
        {
            var doc     = app.ActiveUIDocument?.Document;
            if (doc == null)
            {
                OnCompleted?.Invoke(0, "No active document.");
                return;
            }

            var records = new List<TypeNumberMappingRecord>();
            foreach (var row in Rows ?? Enumerable.Empty<TypeNumberPreviewRow>())
            {
                if (string.IsNullOrWhiteSpace(row.L1Code)) continue;

                records.Add(new TypeNumberMappingRecord
                {
                    DisciplineCode          = row.DisciplineCode,
                    RbrPrCode               = row.RbrPrCode,
                    TypeSourceParameterName = row.TypeSourceParameterName,
                    TypeSourceValue         = row.TypeSourceValue,
                    L1Code                  = row.L1Code.Trim().ToUpperInvariant(),
                    Category                = row.Category,
                    FamilyName              = row.FamilyName,
                    RevitTypeName           = row.TypeName,
                    LastSeenElementTypeId   = row.ElementTypeIdValue,
                    UpdatedUtc              = DateTime.UtcNow.ToString("o"),
                });
            }

            try
            {
                using var t = new Transaction(doc, "Save RBR Type Number Mappings");
                t.Start();
                TypeNumberMappingExtensibleStorageService.Save(doc, records);
                t.Commit();
                OnCompleted?.Invoke(records.Count, null);
            }
            catch (Exception ex)
            {
                OnCompleted?.Invoke(0, ex.Message);
            }
        }

        public string GetName() => "Save RBR Type Number Mappings";
    }
}
