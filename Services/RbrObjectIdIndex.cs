using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace RB_TypeName.Services
{
    /// <summary>
    /// Builds and maintains an in-memory index of all existing RBR-Object_ID values
    /// in the document. Used to find the next available running number per prefix
    /// and to detect duplicates / malformed IDs before generation begins.
    /// </summary>
    public class RbrObjectIdIndex
    {
        private readonly Dictionary<string, int> _maxNumbers
            = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> _allIds
            = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        public List<string> Duplicates { get; } = new List<string>();
        public List<string> Malformed  { get; } = new List<string>();

        // ── Factory ─────────────────────────────────────────────────────────

        public static RbrObjectIdIndex BuildFromDocument(Document doc)
        {
            var index = new RbrObjectIdIndex();

            var collector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType();

            foreach (var element in collector)
            {
                var param = RevitParameterResolver.FindObjectIdParameter(element);
                if (param == null) continue;

                string val = param.AsString();
                if (string.IsNullOrWhiteSpace(val)) continue;

                index.Process(val.Trim());
            }

            return index;
        }

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Returns the next available running number for the given prefix.
        /// If no IDs exist yet for this prefix, returns 1.
        /// </summary>
        public int GetNextNumber(string prefix)
            => _maxNumbers.TryGetValue(prefix, out int max) ? max + 1 : 1;

        /// <summary>Returns true if the exact ID value is already tracked.</summary>
        public bool Contains(string id)
            => !string.IsNullOrWhiteSpace(id) && _allIds.Contains(id);

        /// <summary>
        /// Registers a newly assigned ID so that subsequent elements in the same
        /// batch get incrementing numbers (prevents duplicates within a single run).
        /// </summary>
        public void Register(string id)
        {
            if (!RbrObjectIdParser.TryParse(id, out var parsed)) return;
            _allIds.Add(id);
            if (!_maxNumbers.TryGetValue(parsed.Prefix, out int current)
                || parsed.RunningNumber > current)
            {
                _maxNumbers[parsed.Prefix] = parsed.RunningNumber;
            }
        }

        // ── Private ──────────────────────────────────────────────────────────

        private void Process(string id)
        {
            if (_allIds.Contains(id))
            {
                if (!Duplicates.Contains(id))
                    Duplicates.Add(id);
                return;
            }

            _allIds.Add(id);

            if (!RbrObjectIdParser.TryParse(id, out var parsed))
            {
                Malformed.Add(id);
                return;
            }

            if (!_maxNumbers.TryGetValue(parsed.Prefix, out int current)
                || parsed.RunningNumber > current)
            {
                _maxNumbers[parsed.Prefix] = parsed.RunningNumber;
            }
        }
    }
}
