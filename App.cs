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

        public static LoadRbrTypeGroupsHandler       LoadTypesHandler              { get; private set; }
        public static ExternalEvent                  LoadTypesExternalEvent        { get; private set; }

        public static PreviewRbrTypeNumbersHandler   PreviewTypeNumbersHandler     { get; private set; }
        public static ExternalEvent                  PreviewTypeNumbersExternalEvent { get; private set; }

        public static ApplyRbrTypeNumbersHandler     ApplyTypeNumbersHandler       { get; private set; }
        public static ExternalEvent                  ApplyTypeNumbersExternalEvent { get; private set; }

        public static SaveTypeNumberMappingsHandler  SaveMappingsHandler           { get; private set; }
        public static ExternalEvent                  SaveMappingsExternalEvent     { get; private set; }

        public static ImportTypeNumberMappingsHandler ImportMappingsHandler         { get; private set; }
        public static ExternalEvent                   ImportMappingsExternalEvent   { get; private set; }

        public static ExportTypeNumberMappingsHandler ExportMappingsHandler         { get; private set; }
        public static ExternalEvent                   ExportMappingsExternalEvent   { get; private set; }

        public static ClearObjectIdsHandler          ClearObjectIdsHandler         { get; private set; }
        public static ExternalEvent                   ClearObjectIdsExternalEvent   { get; private set; }

        private RibbonPanel ribbonPanel;

        public Result OnStartup(UIControlledApplication application)
        {
            AssignHandler        = new AssignRbrObjectIdsHandler();
            AssignExternalEvent  = ExternalEvent.Create(AssignHandler);

            PreviewHandler       = new PreviewRbrObjectIdsHandler();
            PreviewExternalEvent = ExternalEvent.Create(PreviewHandler);

            ApplyHandler         = new ApplyRbrObjectIdsHandler();
            ApplyExternalEvent   = ExternalEvent.Create(ApplyHandler);

            LoadTypesHandler              = new LoadRbrTypeGroupsHandler();
            LoadTypesExternalEvent        = ExternalEvent.Create(LoadTypesHandler);

            PreviewTypeNumbersHandler     = new PreviewRbrTypeNumbersHandler();
            PreviewTypeNumbersExternalEvent = ExternalEvent.Create(PreviewTypeNumbersHandler);

            ApplyTypeNumbersHandler       = new ApplyRbrTypeNumbersHandler();
            ApplyTypeNumbersExternalEvent = ExternalEvent.Create(ApplyTypeNumbersHandler);

            SaveMappingsHandler           = new SaveTypeNumberMappingsHandler();
            SaveMappingsExternalEvent     = ExternalEvent.Create(SaveMappingsHandler);

            ImportMappingsHandler         = new ImportTypeNumberMappingsHandler();
            ImportMappingsExternalEvent   = ExternalEvent.Create(ImportMappingsHandler);

            ExportMappingsHandler         = new ExportTypeNumberMappingsHandler();
            ExportMappingsExternalEvent   = ExternalEvent.Create(ExportMappingsHandler);

            ClearObjectIdsHandler         = new ClearObjectIdsHandler();
            ClearObjectIdsExternalEvent   = ExternalEvent.Create(ClearObjectIdsHandler);

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
            LoadTypesExternalEvent?.Dispose();
            PreviewTypeNumbersExternalEvent?.Dispose();
            ApplyTypeNumbersExternalEvent?.Dispose();
            SaveMappingsExternalEvent?.Dispose();
            ImportMappingsExternalEvent?.Dispose();
            ExportMappingsExternalEvent?.Dispose();
            ClearObjectIdsExternalEvent?.Dispose();
            return Result.Succeeded;
        }
    }
}
