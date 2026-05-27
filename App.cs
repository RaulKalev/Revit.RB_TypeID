using Autodesk.Revit.UI;
using RB_TypeName.Commands;
using RB_TypeName.Handlers;
using ricaun.Revit.UI;

namespace RB_TypeName
{
    [AppLoader]
    public class App : IExternalApplication
    {
        public static AssignRbrObjectIdsHandler  AssignHandler        { get; private set; }
        public static ExternalEvent              AssignExternalEvent  { get; private set; }

        public static PreviewRbrObjectIdsHandler PreviewHandler       { get; private set; }
        public static ExternalEvent              PreviewExternalEvent { get; private set; }

        public static ApplyRbrObjectIdsHandler   ApplyHandler         { get; private set; }
        public static ExternalEvent              ApplyExternalEvent   { get; private set; }

        private RibbonPanel ribbonPanel;

        public Result OnStartup(UIControlledApplication application)
        {
            AssignHandler        = new AssignRbrObjectIdsHandler();
            AssignExternalEvent  = ExternalEvent.Create(AssignHandler);

            PreviewHandler       = new PreviewRbrObjectIdsHandler();
            PreviewExternalEvent = ExternalEvent.Create(PreviewHandler);

            ApplyHandler         = new ApplyRbrObjectIdsHandler();
            ApplyExternalEvent   = ExternalEvent.Create(ApplyHandler);

            const string tabName = "RK Tools";

            try { application.CreateRibbonTab(tabName); }
            catch { /* Tab already exists */ }

            ribbonPanel = application.CreateOrSelectPanel(tabName, "Tools");

            ribbonPanel.CreatePushButton<TypeNameCommand>()
                .SetText("Type Name")
                .SetToolTip("Assign RBR-Object_ID parameter values.");

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            AssignExternalEvent?.Dispose();
            PreviewExternalEvent?.Dispose();
            ApplyExternalEvent?.Dispose();
            return Result.Succeeded;
        }
    }
}
