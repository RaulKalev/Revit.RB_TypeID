using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RB_TypeName.Models;
using RB_TypeName.Services;
using System;
using System.Collections.Generic;

namespace RB_TypeName.Handlers
{
    /// <summary>
    /// Exports current Type Number preview rows to an Excel mapping workbook.
    /// Reads stored mapping records from Extensible Storage so the exported file
    /// includes any previously saved Notes.
    ///
    /// No Revit Transaction is required — this only reads from Extensible Storage
    /// and writes to an external Excel file.
    /// </summary>
    public class ExportTypeNumberMappingsHandler : IExternalEventHandler
    {
        public List<TypeNumberPreviewRow>   Rows        { get; set; }
        public TypeNumberExcelExportMode    ExportMode  { get; set; } = TypeNumberExcelExportMode.All;
        public string                        OutputPath  { get; set; }
        public Action<bool, string>          OnCompleted { get; set; }

        public void Execute(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null)
            {
                OnCompleted?.Invoke(false, "No active document.");
                return;
            }

            try
            {
                var stored = TypeNumberMappingExtensibleStorageService.Load(doc);

                var (success, error) = TypeNumberMappingExcelService.Export(
                    Rows, ExportMode, OutputPath, stored);

                OnCompleted?.Invoke(success, error);
            }
            catch (Exception ex)
            {
                OnCompleted?.Invoke(false, ex.Message);
            }
        }

        public string GetName() => "Export RBR Type Number Mappings";
    }
}
