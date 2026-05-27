using Autodesk.Revit.DB;
using RB_TypeName.Services;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RB_TypeName.Services
{
    /// <summary>
    /// In-memory index of all existing RBR-Type_number values on ElementTypes
    /// in the current document. Tracks the highest running number per prefix
    /// so that new assignments are always append-only.
    /// </summary>
    public class RbrTypeNumberIndex
    {
        private readonly Dictionary<string, int> _maxNumbers
            = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> _allValues
            = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        public List<string> Duplicates { get; } = new List<string>();

        // ── Factory ──────────────────────────────────────────────────────────

        public static RbrTypeNumberIndex BuildFromDocument(Document doc)
        {
            var index     = new RbrTypeNumberIndex();
            var collector = new FilteredElementCollector(doc).WhereElementIsElementType();

            foreach (var et in collector)
            {
                var param = RevitParameterResolver.FindTypeNumberParameter(et);
                if (param == null) continue;

                string val = param.AsString();
                if (string.IsNullOrWhiteSpace(val)) continue;

                index.Process(val.Trim());
            }

            return index;
        }

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Returns the highest running number currently indexed under <paramref name="prefix"/>.
        /// Returns 0 when no values exist for this prefix.
        /// </summary>
        public int GetMaxNumber(string prefix)
            => _maxNumbers.TryGetValue(prefix, out int max) ? max : 0;

        public bool Contains(string value)
            => !string.IsNullOrWhiteSpace(value) && _allValues.Contains(value);

        /// <summary>
        /// Registers a newly proposed value so subsequent rows in the same preview
        /// batch receive correctly incrementing numbers.
        /// </summary>
        public void Register(string value, string prefix, int number)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            _allValues.Add(value);

            if (string.IsNullOrWhiteSpace(prefix)) return;
            if (!_maxNumbers.TryGetValue(prefix, out int cur) || number > cur)
                _maxNumbers[prefix] = number;
        }

        // ── Private ──────────────────────────────────────────────────────────

        private void Process(string value)
        {
            if (_allValues.Contains(value))
            {
                if (!Duplicates.Contains(value))
                    Duplicates.Add(value);
                return;
            }

            _allValues.Add(value);

            // Extract trailing 4-digit number and everything before it as the prefix.
            var m = Regex.Match(value, @"^(.+?)(\d{4})$");
            if (!m.Success) return;

            string prefix = m.Groups[1].Value;
            if (!int.TryParse(m.Groups[2].Value, out int num)) return;

            if (!_maxNumbers.TryGetValue(prefix, out int cur) || num > cur)
                _maxNumbers[prefix] = num;
        }
    }
}
