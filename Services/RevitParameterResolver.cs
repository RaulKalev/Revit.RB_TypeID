using Autodesk.Revit.DB;

namespace RB_TypeName.Services
{
    /// <summary>
    /// Resolves Revit element parameters by trying multiple supported name variants.
    /// Only candidates in the controlled lists are tried — no fuzzy matching.
    /// </summary>
    public static class RevitParameterResolver
    {
        // Supported parameter name variants (in priority order).
        public static readonly string[] PrCodeParameterNames =
        {
            "RBR_Pr_Code",
            "RBR-Pr_Code",
            "RBR_PrCode",
            "RBR-PrCode",
        };

        public static readonly string[] ObjectIdParameterNames =
        {
            "RBR-Object_ID",
            "RBR_Object_ID",
        };

        public static readonly string[] TypeNumberParameterNames =
        {
            "RBR-Type_number",
            "RBR_Type_number",
            "RBR-Type_Number",
            "RBR_Type_Number",
            "RBR-Type number",
            "RBR Type number",
        };

        public static Parameter FindPrCodeParameter(Element element)
            => FindParameter(element, PrCodeParameterNames);

        public static Parameter FindObjectIdParameter(Element element)
            => FindParameter(element, ObjectIdParameterNames);

        public static Parameter FindTypeNumberParameter(Element element)
            => FindParameter(element, TypeNumberParameterNames);

        public static string ReadPrCode(Element element)
            => FindPrCodeParameter(element)?.AsString();

        public static string ReadObjectId(Element element)
            => FindObjectIdParameter(element)?.AsString();

        public static string ReadTypeNumber(Element element)
            => FindTypeNumberParameter(element)?.AsString();

        private static Parameter FindParameter(Element element, string[] names)
        {
            foreach (var name in names)
            {
                var p = element.LookupParameter(name);
                if (p != null) return p;
            }
            return null;
        }
    }
}
