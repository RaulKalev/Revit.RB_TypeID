using Autodesk.Revit.DB;
using System.Linq;

namespace RB_TypeName.Services
{
    public static class LevelCodeService
    {
        /// <summary>
        /// Tries to determine the level code for an element (e.g. "B01" from "B01_Basement").
        /// Returns false if no level can be resolved, indicating the element should be skipped.
        /// </summary>
        public static bool TryGetLevelCode(Element element, Document doc, out string levelCode)
        {
            levelCode = null;
            var level = ResolveLevel(element, doc, depth: 0);
            if (level == null) return false;

            levelCode = ExtractLevelCode(level.Name);
            return !string.IsNullOrEmpty(levelCode);
        }

        // ── Level resolution (priority order from plan) ─────────────────────

        private static Level ResolveLevel(Element element, Document doc, int depth)
        {
            if (depth > 3) return null; // Guard against deep host chains

            // Priority 1: FamilyInstance.LevelId
            if (element is FamilyInstance fi)
            {
                if (fi.LevelId != null && fi.LevelId != ElementId.InvalidElementId)
                {
                    var l = doc.GetElement(fi.LevelId) as Level;
                    if (l != null) return l;
                }

                // Priority 3: Host element's level
                if (fi.Host != null)
                {
                    var hostLevel = ResolveLevel(fi.Host, doc, depth + 1);
                    if (hostLevel != null) return hostLevel;
                }
            }

            // Priority 2: Built-in level parameters (covers MEP curves, walls, etc.)
            BuiltInParameter[] levelParams =
            {
                BuiltInParameter.RBS_START_LEVEL_PARAM,        // Pipes, ducts, cable trays
                BuiltInParameter.FAMILY_LEVEL_PARAM,           // Generic families
                BuiltInParameter.SCHEDULE_LEVEL_PARAM,         // Walls, floors
                BuiltInParameter.MULTISTORY_STAIRS_REF_LEVEL,  // Stairs
            };

            foreach (var bip in levelParams)
            {
                var param = element.get_Parameter(bip);
                if (param?.StorageType == StorageType.ElementId)
                {
                    var id = param.AsElementId();
                    if (id != null && id != ElementId.InvalidElementId)
                    {
                        var l = doc.GetElement(id) as Level;
                        if (l != null) return l;
                    }
                }
            }

            // Priority 4: Nearest level at or below element bounding box Z
            return GetNearestLevelBelow(element, doc);
        }

        private static Level GetNearestLevelBelow(Element element, Document doc)
        {
            try
            {
                var bbox = element.get_BoundingBox(null);
                if (bbox == null) return null;

                double z = bbox.Min.Z;

                return new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .Where(l => l.Elevation <= z + 1e-6)
                    .OrderByDescending(l => l.Elevation)
                    .FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        // ── Public helper — also used by PbsMappingService tests ────────────

        /// <summary>Returns the part of the level name before the first '_'.</summary>
        public static string ExtractLevelCode(string levelName)
        {
            if (string.IsNullOrWhiteSpace(levelName))
                return string.Empty;

            int idx = levelName.IndexOf('_');
            if (idx <= 0)
                return levelName.Trim();

            return levelName.Substring(0, idx).Trim();
        }
    }
}
