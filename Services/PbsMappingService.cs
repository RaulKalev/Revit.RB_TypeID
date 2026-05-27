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
        // Primary entry point (configurable)

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
    }
}