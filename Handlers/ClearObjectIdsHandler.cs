using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RB_TypeName.Services;
using System;
using System.Collections.Generic;

namespace RB_TypeName.Handlers
{
    /// <summary>
    /// Clears the RBR-Object_ID parameter value from every element in the document
    /// that currently has a non-empty value.
    /// </summary>
    public class ClearObjectIdsHandler : IExternalEventHandler
    {
        // ── Output callback — invoked on Revit API thread; dispatch to UI ───
        public Action<int, string> OnCompleted { get; set; }   // (clearedCount, errorMessage)

        // ── IExternalEventHandler ─────────────────────────────────────────────

        public void Execute(UIApplication app)
        {
            int    cleared = 0;
            string error   = null;

            try
            {
                var doc = app.ActiveUIDocument.Document;

                var collector = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType();

                var toClear = new List<(Element elem, Parameter param)>();

                foreach (var elem in collector)
                {
                    var p = RevitParameterResolver.FindObjectIdParameter(elem);
                    if (p == null || p.IsReadOnly) continue;

                    string val = p.AsString()?.Trim();
                    if (string.IsNullOrEmpty(val)) continue;

                    toClear.Add((elem, p));
                }

                if (toClear.Count > 0)
                {
                    using var tx = new Transaction(doc, "Clear RBR Object IDs");
                    tx.Start();

                    foreach (var (elem, param) in toClear)
                    {
                        param.Set(string.Empty);
                        cleared++;
                    }

                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            OnCompleted?.Invoke(cleared, error);
        }

        public string GetName() => "Clear RBR Object IDs";
    }
}
