using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace RB_TypeName.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class TypeNameCommand : IExternalCommand
    {
        private static UI.RbrObjectIdWindow _window;
        private static bool _pendingShow;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // If window already exists, surface it
                if (_window != null && _window.IsLoaded)
                {
                    _window.Activate();
                    _window.Focus();
                    return Result.Succeeded;
                }

                // REVIT 2026 FIX: Revit suspends the WPF Dispatcher during Execute().
                // Subscribe to Idling — fires when Revit is truly idle and WPF is safe.
                if (!_pendingShow)
                {
                    _pendingShow = true;
                    commandData.Application.Idling += OnRevitIdling;
                }

                return Result.Succeeded;
            }
            catch (System.Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static void OnRevitIdling(object sender, IdlingEventArgs e)
        {
            if (sender is not UIApplication uiApp)
                return;

            uiApp.Idling -= OnRevitIdling;
            _pendingShow = false;

            _window = new UI.RbrObjectIdWindow(App.AssignHandler, App.AssignExternalEvent);
            _window.Closed += (s, args) => _window = null;
            _window.Show();
        }
    }
}
