using RB_TypeName.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace RB_TypeName.Services
{
    /// <summary>
    /// Reads the PBS Excel file (.xlsx) using the OpenXML ZIP format directly -
    /// no external NuGet dependencies required.
    /// </summary>
    public static class PbsMappingService
    {
        // ── PrCode header candidates (auto-detection) ────────────────────────

        private static readonly string[] PrCodeHeaderCandidates =
        {
            "RBR_Pr_Code", "RBR-Pr_Code", "RBR_PrCode", "RBR-PrCode", "Pr_Code",
        };

        // ── PrCode rows load entry point ─────────────────────────────────────

        /// <summary>
        /// Loads all PBS rows that contain a PrCode value, building a list suitable
        /// for <see cref="PbsPrCodeLookupService"/>.
        /// </summary>
        public static (bool Success, string Error, List<PbsPrCodeRow> Rows)
            LoadPrCodeRows(PbsExcelSourceSettings settings)
        {
            if (settings == null)
                return (false, "PBS Excel settings are missing.", null);

            if (string.IsNullOrWhiteSpace(settings.ExcelPath))
                return (false, "PBS Excel file path has not been selected.", null);

            if (!File.Exists(settings.ExcelPath))
                return (false, "PBS Excel file was not found:\n" + settings.ExcelPath, null);

            try
            {
                using var zip = ZipFile.OpenRead(settings.ExcelPath);

                var sharedStrings = ReadSharedStrings(zip);
                var worksheetPath = FindWorksheetPath(zip, settings.SheetName);

                if (worksheetPath == null)
                    return (false,
                        "Worksheet '" + settings.SheetName + "' was not found in the selected Excel file.",
                        null);

                var (rows, _, error) = ReadPrCodeRows(zip, worksheetPath, sharedStrings, settings);

                if (error != null)
                    return (false, error, null);

                return (true, null, rows);
            }
            catch (IOException ex)
            {
                return (false,
                    "PBS Excel file could not be read. It may be locked or unavailable.\n" + ex.Message,
                    null);
            }
            catch (Exception ex)
            {
                return (false, "Failed to load PBS Excel file.\n" + ex.Message, null);
            }
        }

        // ── Primary entry point (configurable)

        /// <summary>
        /// Loads PBS mappings using the provided settings.
        /// Returns a PbsLoadResult with Success/ErrorMessage/Mappings.
        /// </summary>
        public static PbsLoadResult Load(PbsExcelSourceSettings settings)
        {
            if (settings == null)
                return PbsLoadResult.Fail("PBS Excel settings are missing.");

            if (string.IsNullOrWhiteSpace(settings.ExcelPath))
                return PbsLoadResult.Fail("PBS Excel file path has not been selected.");

            if (!File.Exists(settings.ExcelPath))
                return PbsLoadResult.Fail(
                    "PBS Excel file was not found:\n" + settings.ExcelPath);

            try
            {
                using var zip = ZipFile.OpenRead(settings.ExcelPath);

                var sharedStrings = ReadSharedStrings(zip);
                var worksheetPath = FindWorksheetPath(zip, settings.SheetName);

                if (worksheetPath == null)
                    return PbsLoadResult.Fail(
                        "Worksheet '" + settings.SheetName + "' was not found in the selected Excel file.");

                var mappings = ReadMappings(zip, worksheetPath, sharedStrings, settings);

                if (mappings.Count == 0)
                    return PbsLoadResult.Fail(
                        "No valid PBS mappings were found. " +
                        "Columns " + settings.DisciplineCodeColumn + " and " + settings.ObjectCodeColumn + " must contain values.");

                return PbsLoadResult.Ok(mappings);
            }
            catch (IOException ex)
            {
                return PbsLoadResult.Fail(
                    "PBS Excel file could not be read. It may be locked or unavailable.\n" + ex.Message);
            }
            catch (Exception ex)
            {
                return PbsLoadResult.Fail("Failed to load PBS Excel file.\n" + ex.Message);
            }
        }

        // Private helpers

        private static string[] ReadSharedStrings(ZipArchive zip)
        {
            var entry = zip.GetEntry("xl/sharedStrings.xml");
            if (entry == null)
                return Array.Empty<string>();

            using var stream = entry.Open();
            var doc = XDocument.Load(stream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            return doc.Root
                .Elements(ns + "si")
                .Select(si => string.Concat(si.Descendants(ns + "t").Select(t => t.Value)))
                .ToArray();
        }

        private static string FindWorksheetPath(ZipArchive zip, string sheetName)
        {
            var wbEntry = zip.GetEntry("xl/workbook.xml");
            if (wbEntry == null) return null;

            using var wbStream = wbEntry.Open();
            var wbDoc = XDocument.Load(wbStream);

            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace r  = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

            var sheet = wbDoc.Root
                .Element(ns + "sheets")?
                .Elements(ns + "sheet")
                .FirstOrDefault(s => string.Equals(
                    s.Attribute("name")?.Value, sheetName, StringComparison.OrdinalIgnoreCase));

            if (sheet == null) return null;

            string relId = sheet.Attribute(r + "id")?.Value;
            if (relId == null) return null;

            var relsEntry = zip.GetEntry("xl/_rels/workbook.xml.rels");
            if (relsEntry == null) return null;

            using var relsStream = relsEntry.Open();
            var relsDoc = XDocument.Load(relsStream);
            XNamespace relsNs = "http://schemas.openxmlformats.org/package/2006/relationships";

            var rel = relsDoc.Root
                .Elements(relsNs + "Relationship")
                .FirstOrDefault(x => x.Attribute("Id")?.Value == relId);

            string target = rel?.Attribute("Target")?.Value;
            if (target == null) return null;

            return target.StartsWith("/")
                ? target.TrimStart('/')
                : "xl/" + target;
        }

        private static List<PbsMapping> ReadMappings(
            ZipArchive zip, string worksheetPath, string[] sharedStrings,
            PbsExcelSourceSettings settings)
        {
            var entry = zip.GetEntry(worksheetPath);
            if (entry == null) return new List<PbsMapping>();

            int colR = LetterToIndex(settings.DisciplineCodeColumn);
            int colS = LetterToIndex(settings.ObjectCodeColumn);
            int colT = LetterToIndex(settings.SecondaryObjectCodeColumn);
            int colK = LetterToIndex(settings.DescriptionColumn);

            using var stream = entry.Open();
            var doc = XDocument.Load(stream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var results   = new List<PbsMapping>();
            var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var rows = doc.Root
                .Element(ns + "sheetData")?
                .Elements(ns + "row")
                ?? Enumerable.Empty<XElement>();

            foreach (var row in rows)
            {
                int rowNum = 0;
                int.TryParse(row.Attribute("r")?.Value, out rowNum);
                if (rowNum > 0 && rowNum <= settings.HeaderRow)
                    continue;

                var cells = row.Elements(ns + "c")
                    .ToDictionary(c => ParseColumnIndex(c.Attribute("r")?.Value));

                string rVal = GetCellValue(cells, colR, sharedStrings);
                string sVal = GetCellValue(cells, colS, sharedStrings);

                if (string.IsNullOrWhiteSpace(rVal) || string.IsNullOrWhiteSpace(sVal))
                    continue;

                string disciplineCode = rVal.Trim().ToUpperInvariant();
                string objectCode     = sVal.Trim().ToUpperInvariant();
                string pbsCode        = disciplineCode + "-" + objectCode;

                if (seenCodes.Contains(pbsCode))
                    continue;
                seenCodes.Add(pbsCode);

                string tVal = GetCellValue(cells, colT, sharedStrings);
                string kVal = GetCellValue(cells, colK, sharedStrings);

                results.Add(new PbsMapping
                {
                    SourceRowNumber     = rowNum,
                    DisciplineCode      = disciplineCode,
                    ObjectCode          = objectCode,
                    SecondaryObjectCode = tVal?.Trim() ?? string.Empty,
                    Description         = kVal?.Trim() ?? string.Empty,
                });
            }

            return results;
        }

        private static string GetCellValue(
            Dictionary<int, XElement> cells, int colIndex, string[] sharedStrings)
        {
            if (!cells.TryGetValue(colIndex, out var cell)) return null;

            XNamespace ns   = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            string type     = cell.Attribute("t")?.Value;
            string rawValue = cell.Element(ns + "v")?.Value;

            if (rawValue == null) return null;

            if (type == "s" && int.TryParse(rawValue, out int idx))
                return idx < sharedStrings.Length ? sharedStrings[idx] : rawValue;

            return rawValue;
        }

        private static int ParseColumnIndex(string cellRef)
        {
            if (string.IsNullOrEmpty(cellRef)) return 0;
            int col = 0;
            foreach (char c in cellRef)
            {
                if (!char.IsLetter(c)) break;
                col = col * 26 + (c - 'A' + 1);
            }
            return col;
        }

        private static int LetterToIndex(string columnLetter)
            => ParseColumnIndex(columnLetter?.Trim().ToUpperInvariant());

        /// <summary>Converts a 1-based column index back to an Excel column letter (e.g. 18 → "R").</summary>
        private static string IndexToLetter(int colIndex)
        {
            var result = string.Empty;
            while (colIndex > 0)
            {
                int mod = (colIndex - 1) % 26;
                result   = (char)('A' + mod) + result;
                colIndex = (colIndex - 1) / 26;
            }
            return result;
        }

        // ── Type-number header candidates ────────────────────────────────────

        private static readonly string[] TypeNumberHeaderCandidates =
        {
            "RBR_Type_number", "RBR-Type_number", "RBR_Type_Number", "RBR-Type_Number",
            "RBR-Type number",  "RBR Type number",
            "Type_number",      "Type Number",
        };

        // ── Type-number rows load entry point ────────────────────────────────

        /// <summary>
        /// Loads PBS rows that contain a type-number template (e.g. "CAM-01ZZZZ").
        /// Returns a flat list keyed by normalised PrCode for use in the Type Numbers tab.
        /// </summary>
        public static (bool Success, string Error, List<PbsTypeNumberRow> Rows)
            LoadTypeNumberRows(PbsExcelSourceSettings settings)
        {
            if (settings == null)
                return (false, "PBS Excel settings are missing.", null);

            if (string.IsNullOrWhiteSpace(settings.ExcelPath))
                return (false, "PBS Excel file path has not been selected.", null);

            if (!File.Exists(settings.ExcelPath))
                return (false, "PBS Excel file was not found:\n" + settings.ExcelPath, null);

            try
            {
                using var zip = ZipFile.OpenRead(settings.ExcelPath);

                var sharedStrings = ReadSharedStrings(zip);
                var worksheetPath = FindWorksheetPath(zip, settings.SheetName);

                if (worksheetPath == null)
                    return (false,
                        "Worksheet '" + settings.SheetName + "' was not found in the selected Excel file.",
                        null);

                var (rows, error) = ReadTypeNumberRows(zip, worksheetPath, sharedStrings, settings);
                if (error != null)
                    return (false, error, null);

                return (true, null, rows);
            }
            catch (IOException ex)
            {
                return (false,
                    "PBS Excel file could not be read. It may be locked or unavailable.\n" + ex.Message,
                    null);
            }
            catch (Exception ex)
            {
                return (false, "Failed to load PBS Excel type-number rows.\n" + ex.Message, null);
            }
        }

        // ── Type-number rows reader ───────────────────────────────────────────

        private static (List<PbsTypeNumberRow> Rows, string Error)
            ReadTypeNumberRows(ZipArchive zip, string worksheetPath,
                string[] sharedStrings, PbsExcelSourceSettings settings)
        {
            var entry = zip.GetEntry(worksheetPath);
            if (entry == null) return (null, "Worksheet entry not found in archive.");

            int colR = LetterToIndex(settings.DisciplineCodeColumn);
            int colS = LetterToIndex(settings.ObjectCodeColumn);

            using var stream = entry.Open();
            var doc = XDocument.Load(stream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var allRows = doc.Root
                .Element(ns + "sheetData")?
                .Elements(ns + "row")
                .ToList()
                ?? new List<XElement>();

            // ── Locate PrCode column ─────────────────────────────────────────
            int colPrCode = FindColumnByHeaderCandidates(
                allRows, sharedStrings, settings.HeaderRow, ns,
                PrCodeHeaderCandidates,
                settings.PbsPrCodeColumn);

            if (colPrCode == 0)
                return (null,
                    "The RBR_Pr_Code column could not be located in the PBS sheet.");

            // ── Locate Type-number column ────────────────────────────────────
            int colTypeNum = FindColumnByHeaderCandidates(
                allRows, sharedStrings, settings.HeaderRow, ns,
                TypeNumberHeaderCandidates,
                settings.PbsTypeNumberColumn);

            if (colTypeNum == 0)
                return (null,
                    "The RBR-Type_number column could not be located in the PBS sheet. " +
                    "Please check the file or specify the column override in settings.");

            // ── Read data rows ───────────────────────────────────────────────
            var result = new List<PbsTypeNumberRow>();

            foreach (var row in allRows)
            {
                int.TryParse(row.Attribute("r")?.Value, out int rowNum);
                if (rowNum > 0 && rowNum <= settings.HeaderRow) continue;

                var cells = row.Elements(ns + "c")
                    .ToDictionary(c => ParseColumnIndex(c.Attribute("r")?.Value));

                string prCodeRaw = GetCellValue(cells, colPrCode, sharedStrings);
                if (string.IsNullOrWhiteSpace(prCodeRaw)) continue;

                string typeNumTemplate = GetCellValue(cells, colTypeNum, sharedStrings);
                // Keep rows that have either a type number template or at least a PrCode.
                if (string.IsNullOrWhiteSpace(typeNumTemplate)) continue;

                string rVal = GetCellValue(cells, colR, sharedStrings);
                string sVal = GetCellValue(cells, colS, sharedStrings);

                string normalized = prCodeRaw.Trim()
                    .Replace("\u00A0", " ")
                    .ToUpperInvariant();

                result.Add(new PbsTypeNumberRow
                {
                    RowNumber          = rowNum,
                    PrCodeRaw          = prCodeRaw.Trim(),
                    PrCodeNormalized   = normalized,
                    DisciplineCode     = rVal?.Trim().ToUpperInvariant() ?? string.Empty,
                    ObjectCode         = sVal?.Trim().ToUpperInvariant() ?? string.Empty,
                    TypeNumberTemplate = typeNumTemplate.Trim(),
                });
            }

            return (result, null);
        }

        /// <summary>
        /// Finds a column index by scanning the header rows for any of the given candidates.
        /// Falls back to a manually-specified override letter when provided.
        /// </summary>
        private static int FindColumnByHeaderCandidates(
            List<XElement> allRows,
            string[] sharedStrings,
            int headerRow,
            XNamespace ns,
            string[] candidates,
            string overrideLetter)
        {
            if (!string.IsNullOrWhiteSpace(overrideLetter))
                return LetterToIndex(overrideLetter.Trim().ToUpperInvariant());

            foreach (var row in allRows)
            {
                int.TryParse(row.Attribute("r")?.Value, out int rn);
                if (rn <= 0 || rn > headerRow) continue;

                var cells = row.Elements(ns + "c")
                    .ToDictionary(c => ParseColumnIndex(c.Attribute("r")?.Value));

                foreach (var kv in cells)
                {
                    string val = GetCellValue(
                        new Dictionary<int, XElement> { { kv.Key, kv.Value } },
                        kv.Key, sharedStrings);
                    if (val == null) continue;
                    val = val.Trim();
                    if (candidates.Any(h => string.Equals(h, val, StringComparison.OrdinalIgnoreCase)))
                        return kv.Key;
                }
            }

            return 0;
        }

        // ── PrCode rows reader ───────────────────────────────────────────────

        private static (List<PbsPrCodeRow> Rows, string ColumnFound, string Error)
            ReadPrCodeRows(ZipArchive zip, string worksheetPath,
                string[] sharedStrings, PbsExcelSourceSettings settings)
        {
            var entry = zip.GetEntry(worksheetPath);
            if (entry == null) return (null, null, "Worksheet entry not found in archive.");

            int colR = LetterToIndex(settings.DisciplineCodeColumn);
            int colS = LetterToIndex(settings.ObjectCodeColumn);

            using var stream = entry.Open();
            var doc = XDocument.Load(stream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var allRows = doc.Root
                .Element(ns + "sheetData")?
                .Elements(ns + "row")
                .ToList()
                ?? new List<XElement>();

            // ── Locate PrCode column ─────────────────────────────────────────
            int    colPrCode    = 0;
            string columnFound  = null;

            if (!string.IsNullOrWhiteSpace(settings.PbsPrCodeColumn))
            {
                colPrCode   = LetterToIndex(settings.PbsPrCodeColumn);
                columnFound = settings.PbsPrCodeColumn.Trim().ToUpperInvariant();
            }
            else
            {
                // Scan header rows for a matching header candidate.
                var foundHeaders = new List<string>(); // for diagnostics

                foreach (var headerRow in allRows)
                {
                    int.TryParse(headerRow.Attribute("r")?.Value, out int rn);
                    if (rn <= 0 || rn > settings.HeaderRow) continue;

                    var cells = headerRow.Elements(ns + "c")
                        .ToDictionary(c => ParseColumnIndex(c.Attribute("r")?.Value));

                    foreach (var kv in cells)
                    {
                        string val = GetCellValue(
                            new Dictionary<int, XElement> { { kv.Key, kv.Value } },
                            kv.Key, sharedStrings);

                        if (val == null) continue;
                        val = val.Trim();
                        if (val.Length == 0) continue;

                        foundHeaders.Add($"{IndexToLetter(kv.Key)}:{val}");

                        // Exact match first
                        bool match = PrCodeHeaderCandidates.Any(h =>
                            string.Equals(h, val, StringComparison.OrdinalIgnoreCase));

                        // Fuzzy fallback: header contains "prcode" or "pr_code" or "pr code"
                        if (!match)
                        {
                            string norm = val.Replace("-", "").Replace("_", "").Replace(" ", "")
                                            .ToUpperInvariant();
                            match = norm.Contains("PRCODE");
                        }

                        if (match)
                        {
                            colPrCode   = kv.Key;
                            columnFound = IndexToLetter(kv.Key);
                            break;
                        }
                    }
                    if (colPrCode > 0) break;
                }

                if (colPrCode == 0 && foundHeaders.Count == 0)
                {
                    // No header rows found at all — check HeaderRow setting
                    foundHeaders.Add($"(no cells found in rows 1–{settings.HeaderRow})");
                }

                if (colPrCode == 0)
                {
                    string headerList = foundHeaders.Count > 0
                        ? string.Join(", ", foundHeaders.Take(20))
                        : "(tühje lahtreid)";
                    return (null, null,
                        "PBS Excelist ei leitud RBR_Pr_Code veergu.\n" +
                        "Leitud veerupäised:\n" + headerList + "\n\n" +
                        "Seadetes saad täpsustada veerutähe käsitsi (nt \"E\").");
                }
            }

            // ── Read data rows ───────────────────────────────────────────────
            var result = new List<PbsPrCodeRow>();

            foreach (var row in allRows)
            {
                int.TryParse(row.Attribute("r")?.Value, out int rowNum);
                if (rowNum > 0 && rowNum <= settings.HeaderRow) continue;

                var cells = row.Elements(ns + "c")
                    .ToDictionary(c => ParseColumnIndex(c.Attribute("r")?.Value));

                string prCodeRaw = GetCellValue(cells, colPrCode, sharedStrings);
                if (string.IsNullOrWhiteSpace(prCodeRaw)) continue;

                string rVal = GetCellValue(cells, colR, sharedStrings);
                string sVal = GetCellValue(cells, colS, sharedStrings);

                string normalized = prCodeRaw.Trim()
                    .Replace("\u00A0", " ")
                    .ToUpperInvariant();

                result.Add(new PbsPrCodeRow
                {
                    RowNumber        = rowNum,
                    PrCodeRaw        = prCodeRaw.Trim(),
                    PrCodeNormalized = normalized,
                    ObjectIdPart1    = rVal?.Trim().ToUpperInvariant() ?? string.Empty,
                    ObjectIdPart2    = sVal?.Trim().ToUpperInvariant() ?? string.Empty,
                });
            }

            return (result, columnFound, null);
        }
    }
}