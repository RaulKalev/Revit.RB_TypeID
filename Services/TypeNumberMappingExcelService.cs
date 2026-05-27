using RB_TypeName.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RB_TypeName.Services
{
    public enum TypeNumberExcelExportMode
    {
        All,
        OnlyUnmapped,
        OnlyMapped,
    }

    public enum TypeNumberExcelImportMode
    {
        Override,
        OnlyAddNew,
    }

    /// <summary>
    /// Exports/imports the Type Number mapping grid to/from an xlsx file using direct
    /// ZIP+XML (no external NuGet dependencies). Column I (RBR_ObjectID_Character_Level1)
    /// and column N (Notes) are the only user-editable fields.
    /// </summary>
    public static class TypeNumberMappingExcelService
    {
        private const string SheetName = "RBR_TypeNumber_Mapping";

        // ── Column headers (A = 1 … N = 14) ─────────────────────────────────

        private static readonly string[] Headers =
        {
            "Category",                          // A 1
            "FamilyName",                        // B 2
            "RevitTypeName",                     // C 3
            "RBR_Pr_Code",                       // D 4
            "DisciplineCode",                    // E 5
            "PBS_Template",                      // F 6
            "Existing_RBR_Type_number",          // G 7
            "Proposed_RBR_Type_number",          // H 8
            "RBR_ObjectID_Character_Level1",     // I 9  ← editable
            "PBS_Match_Status",                  // J 10
            "Status",                            // K 11
            "Message",                           // L 12
            "Notes",                             // M 13 ← editable
        };

        // Column indices (1-based)
        private const int ColL1Code = 9;   // I
        private const int ColNotes  = 13;  // M

        // ── Public: Export ───────────────────────────────────────────────────

        public static (bool Success, string Error) Export(
            List<TypeNumberPreviewRow> rows,
            TypeNumberExcelExportMode mode,
            string outputPath,
            Dictionary<string, TypeNumberMappingRecord> storedMappings)
        {
            if (rows == null || rows.Count == 0)
                return (false, "No rows to export.");

            IEnumerable<TypeNumberPreviewRow> toExport = mode switch
            {
                TypeNumberExcelExportMode.OnlyUnmapped =>
                    rows.Where(r => string.IsNullOrWhiteSpace(r.L1Code)),
                TypeNumberExcelExportMode.OnlyMapped =>
                    rows.Where(r => !string.IsNullOrWhiteSpace(r.L1Code)),
                _ => rows,
            };

            var filtered = toExport.ToList();
            if (filtered.Count == 0)
                return (false, $"No rows match export mode: {mode}.");

            try
            {
                // Build data rows
                var data = new List<string[]>();
                foreach (var row in filtered)
                {
                    string notes = string.Empty;
                    if (storedMappings != null
                        && storedMappings.TryGetValue(row.MatchKey, out var rec))
                        notes = rec.Notes ?? string.Empty;

                    data.Add(new[]
                    {
                        row.Category,                     // A
                        row.FamilyName,                   // B
                        row.TypeName,                     // C
                        row.RbrPrCode,                    // D
                        row.DisciplineCode,               // E
                        row.PbsTypeTemplate,              // F
                        row.ExistingTypeNumber,           // G
                        row.ProposedTypeNumber,           // H
                        row.L1Code,                       // I
                        row.PbsMatchStatus,               // J
                        row.Status,                       // K
                        row.Message,                      // L
                        notes,                            // M
                    });
                }

                var allRows = new List<string[]> { Headers };
                allRows.AddRange(data);

                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                BuildXlsx(outputPath, SheetName, allRows);
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        // ── Public: Import ───────────────────────────────────────────────────

        public static (List<TypeNumberMappingRecord> Records,
                        TypeNumberMappingImportResult Result,
                        string Error)
            Import(string inputPath)
        {
            var result = new TypeNumberMappingImportResult();

            if (!File.Exists(inputPath))
                return (null, null, "File not found: " + inputPath);

            try
            {
                using var zip = ZipFile.OpenRead(inputPath);

                var sharedStrings = ReadSharedStrings(zip);
                var wsPath        = FindWorksheetPath(zip);

                if (wsPath == null)
                    return (null, null,
                        $"Worksheet '{SheetName}' was not found in the Excel file.");

                var entry = zip.GetEntry(wsPath);
                if (entry == null)
                    return (null, null, "Worksheet entry missing in ZIP archive.");

                using var stream = entry.Open();
                var doc = XDocument.Load(stream);
                XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

                var xlRows = doc.Root
                    .Element(ns + "sheetData")?
                    .Elements(ns + "row")
                    .ToList()
                    ?? new List<XElement>();

                if (xlRows.Count < 2)
                    return (null, null, "The mapping sheet contains no data rows.");

                // ── Locate header row (first row) ─────────────────────────────
                var headerRow = xlRows[0];
                var headerCells = headerRow.Elements(ns + "c")
                    .ToDictionary(
                        c => ParseColIndex(c.Attribute("r")?.Value),
                        c => (GetCellValue(c, sharedStrings) ?? string.Empty).Trim());

                var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in headerCells)
                    colMap[kv.Value] = kv.Key;

                // ── Validate required headers ─────────────────────────────────
                var required = new[]
                {
                    "Category", "FamilyName", "RevitTypeName",
                    "RBR_ObjectID_Character_Level1",
                };

                var missing = required.Where(h => !colMap.ContainsKey(h)).ToList();
                if (missing.Count > 0)
                    return (null, null,
                        "Import failed. Missing required columns:\n- "
                        + string.Join("\n- ", missing));

                // Optional columns (graceful fallback when absent)
                int colCat      = colMap.TryGetValue("Category",                     out int v) ? v : -1;
                int colFamily   = colMap.TryGetValue("FamilyName",                   out v)     ? v : -1;
                int colType     = colMap.TryGetValue("RevitTypeName",                out v)     ? v : -1;
                int colPrCode   = colMap.TryGetValue("RBR_Pr_Code",                  out v)     ? v : -1;
                int colL1       = colMap["RBR_ObjectID_Character_Level1"];
                int colNotes    = colMap.TryGetValue("Notes",                         out v)     ? v : -1;

                // ── Parse data rows ───────────────────────────────────────────
                // Detect duplicates by match key within the import file.
                var seen       = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                var rawRecords = new List<TypeNumberMappingRecord>();

                for (int i = 1; i < xlRows.Count; i++)
                {
                    var cells = xlRows[i].Elements(ns + "c")
                        .ToDictionary(c => ParseColIndex(c.Attribute("r")?.Value),
                                      c => GetCellValue(c, sharedStrings) ?? string.Empty);

                    string cat      = GetCol(cells, colCat);
                    string family   = GetCol(cells, colFamily);
                    string typeName = GetCol(cells, colType);
                    string prCode   = GetCol(cells, colPrCode);
                    string l1Raw    = GetCol(cells, colL1);
                    string notes    = GetCol(cells, colNotes);

                    if (string.IsNullOrWhiteSpace(typeName)
                        || string.IsNullOrWhiteSpace(l1Raw))
                    {
                        result.Invalid++;
                        result.Issues.Add($"Row {i + 1}: missing RevitTypeName or L1 code — skipped.");
                        continue;
                    }

                    // Validate L1 code: uppercase, A-Z 0-9 _ -
                    string l1 = l1Raw.Trim().ToUpperInvariant();
                    if (!Regex.IsMatch(l1, @"^[A-Z0-9_\-]+$"))
                    {
                        result.Invalid++;
                        result.Issues.Add($"Row {i + 1}: invalid L1 code '{l1Raw}' — skipped.");
                        continue;
                    }

                    var rec = new TypeNumberMappingRecord
                    {
                        Category      = cat.Trim(),
                        FamilyName    = family.Trim(),
                        RevitTypeName = typeName.Trim(),
                        RbrPrCode     = prCode.Trim(),
                        L1Code        = l1,
                        Notes         = notes.Trim(),
                    };

                    string key = rec.MatchKey;

                    if (!seen.TryGetValue(key, out var l1List))
                        seen[key] = l1List = new List<string>();

                    l1List.Add(l1);
                    rawRecords.Add(rec);
                }

                // ── Detect conflicting duplicates ─────────────────────────────
                var finalRecords  = new List<TypeNumberMappingRecord>();
                var conflictKeys  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var kv in seen.Where(x => x.Value.Count > 1))
                {
                    bool allSame = kv.Value.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;
                    if (!allSame)
                    {
                        conflictKeys.Add(kv.Key);
                        result.Conflicts++;
                        result.Issues.Add($"Conflicting duplicate key: '{kv.Key}' — all rows skipped.");
                    }
                    else
                    {
                        // All duplicates agree on L1 code — keep just one.
                        result.Issues.Add($"Duplicate key (identical values): '{kv.Key}' — accepted once.");
                    }
                }

                // Deduplicate keeping only first occurrence per key, exclude conflicts.
                var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var rec in rawRecords)
                {
                    if (conflictKeys.Contains(rec.MatchKey))   continue;
                    if (!seenKeys.Add(rec.MatchKey))           continue;
                    finalRecords.Add(rec);
                }

                return (finalRecords, result, null);
            }
            catch (Exception ex)
            {
                return (null, null, "Failed to read import file: " + ex.Message);
            }
        }

        // ── xlsx builder ─────────────────────────────────────────────────────

        private static void BuildXlsx(string filePath, string sheetName,
                                       List<string[]> rows)
        {
            string contentTypes =
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                "</Types>";

            string rootRels =
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                "</Relationships>";

            string workbook =
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                "<sheets>" +
                $"<sheet name=\"{XmlEsc(sheetName)}\" sheetId=\"1\" r:id=\"rId1\"/>" +
                "</sheets>" +
                "</workbook>";

            string workbookRels =
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                "</Relationships>";

            string styles =
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                "<fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
                "<fills count=\"2\">" +
                "<fill><patternFill patternType=\"none\"/></fill>" +
                "<fill><patternFill patternType=\"gray125\"/></fill>" +
                "</fills>" +
                "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                "<cellXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/></cellXfs>" +
                "</styleSheet>";

            string worksheet = BuildWorksheetXml(rows);

            using var archive = ZipFile.Open(filePath, ZipArchiveMode.Create);
            WriteEntry(archive, "[Content_Types].xml", contentTypes);
            WriteEntry(archive, "_rels/.rels",          rootRels);
            WriteEntry(archive, "xl/workbook.xml",      workbook);
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", workbookRels);
            WriteEntry(archive, "xl/styles.xml",        styles);
            WriteEntry(archive, "xl/worksheets/sheet1.xml", worksheet);
        }

        private static string BuildWorksheetXml(List<string[]> rows)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            sb.Append("<sheetData>");

            for (int ri = 0; ri < rows.Count; ri++)
            {
                int rowNum = ri + 1;
                sb.Append($"<row r=\"{rowNum}\">");
                var cols = rows[ri];
                for (int ci = 0; ci < cols.Length; ci++)
                {
                    string cellRef = ColLetter(ci + 1) + rowNum;
                    string val     = XmlEsc(Sanitize(cols[ci] ?? string.Empty));
                    sb.Append($"<c r=\"{cellRef}\" t=\"inlineStr\"><is><t>{val}</t></is></c>");
                }
                sb.Append("</row>");
            }

            sb.Append("</sheetData></worksheet>");
            return sb.ToString();
        }

        private static void WriteEntry(ZipArchive zip, string name, string content)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(content);
        }

        // ── xlsx reader helpers ──────────────────────────────────────────────

        private static string[] ReadSharedStrings(ZipArchive zip)
        {
            var entry = zip.GetEntry("xl/sharedStrings.xml");
            if (entry == null) return Array.Empty<string>();

            using var stream = entry.Open();
            var doc = XDocument.Load(stream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            return doc.Root
                .Elements(ns + "si")
                .Select(si => string.Concat(si.Descendants(ns + "t").Select(t => t.Value)))
                .ToArray();
        }

        private static string FindWorksheetPath(ZipArchive zip)
        {
            // 1. Try workbook to find sheet by name.
            var wbEntry = zip.GetEntry("xl/workbook.xml");
            if (wbEntry == null) return null;

            using var wbStream = wbEntry.Open();
            var wbDoc = XDocument.Load(wbStream);
            XNamespace ns  = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace r   = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

            // Try named sheet first, fall back to first sheet.
            var sheet = wbDoc.Root.Element(ns + "sheets")?
                .Elements(ns + "sheet")
                .FirstOrDefault(s => string.Equals(s.Attribute("name")?.Value,
                    SheetName, StringComparison.OrdinalIgnoreCase))
                ?? wbDoc.Root.Element(ns + "sheets")?
                .Elements(ns + "sheet").FirstOrDefault();

            if (sheet == null) return null;
            string relId = sheet.Attribute(r + "id")?.Value;
            if (relId == null) return null;

            var relsEntry = zip.GetEntry("xl/_rels/workbook.xml.rels");
            if (relsEntry == null) return null;

            using var relsStream = relsEntry.Open();
            var relsDoc = XDocument.Load(relsStream);
            XNamespace rns = "http://schemas.openxmlformats.org/package/2006/relationships";
            var rel = relsDoc.Root.Elements(rns + "Relationship")
                .FirstOrDefault(x => x.Attribute("Id")?.Value == relId);

            string target = rel?.Attribute("Target")?.Value;
            if (target == null) return null;
            return target.StartsWith("/") ? target.TrimStart('/') : "xl/" + target;
        }

        private static string GetCellValue(XElement cell, string[] sharedStrings)
        {
            XNamespace ns  = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            string type    = cell.Attribute("t")?.Value;
            string rawVal  = cell.Element(ns + "v")?.Value;
            string inlineT = cell.Element(ns + "is")?.Element(ns + "t")?.Value;

            if (type == "inlineStr") return inlineT;
            if (type == "s" && rawVal != null && int.TryParse(rawVal, out int idx))
                return idx < sharedStrings.Length ? sharedStrings[idx] : rawVal;
            return rawVal;
        }

        private static string GetCol(Dictionary<int, string> cells, int colIdx)
        {
            if (colIdx <= 0) return string.Empty;
            return cells.TryGetValue(colIdx, out string v) ? v ?? string.Empty : string.Empty;
        }

        private static int ParseColIndex(string cellRef)
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

        private static string ColLetter(int col)
        {
            string result = string.Empty;
            while (col > 0)
            {
                col--;
                result = (char)('A' + col % 26) + result;
                col   /= 26;
            }
            return result;
        }

        private static string XmlEsc(string v)
            => v.Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");

        private static string Sanitize(string v)
        {
            var sb = new StringBuilder(v.Length);
            foreach (var c in v)
                if (c >= 0x20 || c == '\t') sb.Append(c);
            return sb.ToString().Replace("\t", " ");
        }
    }
}
