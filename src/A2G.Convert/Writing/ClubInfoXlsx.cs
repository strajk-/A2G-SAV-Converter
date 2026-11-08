using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using A2G.Convert.Engine;

namespace A2G.Convert.Writing
{
    /// <summary>The texts of the workbook that depend on the user's language (the column headers stay English: they identify the columns on import).</summary>
    public sealed class ClubInfoSheetText
    {
        /// <summary>Title and lines of the "Info" sheet.</summary>
        public string InfoTitle = "Club information";
        public List<string> InfoLines = new List<string>();
        /// <summary>The message of the cell validation when a text is too long ("{0}" is replaced by the limit).</summary>
        public string TooLongTitle = "Text too long";
        public string TooLongMessage = "At most {0} characters.";
        public int MaxCity = 16, MaxNickname = 18, MaxStadium = 26;
        /// <summary>Validation of the capacity column ("{0}" = minimum, "{1}" = maximum).</summary>
        public string BadCapacityTitle = "Invalid capacity";
        public string BadCapacityMessage = "A whole number from {0} to {1}.";
        public int MinCapacity = 3000, MaxCapacity = 110000;
        public string SheetName = "Clubs";
        public string InfoSheetName = "Info";
        /// <summary>The reference sheet of the kit numbers.</summary>
        public string KitSheetName = "Kits", KitNumberHeader = "No.", KitNameHeader = "Name", KitSampleHeader = "Sample", KitColoursTitle = "Colours", KitPatternsTitle = "Stripe patterns";
        public string[] ColourNames = { "White", "Red", "Yellow", "Blue", "Light green", "Dark green", "Teal", "Light blue", "Brown", "Purple", "Orange", "Dark grey", "Maroon" };
        public string[] PatternNames = { "Plain", "Vertical stripes", "Hoops", "Halves", "Shoulders", "Chest band", "Sleeves" };
        public string BadKitTitle = "Invalid kit value", BadPatternMessage = "A whole number from 0 to 6 (see the sheet Kits).", BadColourMessage = "A whole number from 0 to 12 (see the sheet Kits).";
    }

    /// <summary>
    /// Club information as a formatted Excel workbook (.xlsx) that opens without any import step: a frozen, filtered header row,
    /// read-only columns in grey, the three editable columns (City, Nickname, Stadium) highlighted with a text length check, and a
    /// second sheet with instructions. Written and read with the Open XML format directly (no Excel needed).
    /// </summary>
    public static class ClubInfoXlsx
    {
        // cell styles (indexes into cellXfs of styles.xml)
        private const int Plain = 0, Header = 1, ReadOnly = 2, Editable = 3, Title = 4, Wrapped = 5, EditableNumber = 6;

        private static readonly double[] Widths = { 10, 16, 22, 36, 36, 20, 22, 30, 14, 12, 12, 12, 12, 12, 12, 12, 12 };

        public static void Write(string path, IEnumerable<ClubInfoRow> rows, ClubInfoSheetText text)
        {
            text = text ?? new ClubInfoSheetText();
            var list = rows.ToList();
            if (File.Exists(path)) File.Delete(path);
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                Add(zip, "[Content_Types].xml", ContentTypes);
                Add(zip, "_rels/.rels", RootRels);
                Add(zip, "xl/workbook.xml", Workbook(text, list.Count));
                Add(zip, "xl/_rels/workbook.xml.rels", WorkbookRels);
                Add(zip, "xl/styles.xml", Styles());
                Add(zip, "xl/worksheets/sheet1.xml", ClubsSheet(list, text));
                Add(zip, "xl/worksheets/sheet2.xml", InfoSheet(text));
                Add(zip, "xl/worksheets/sheet3.xml", KitSheet(text));
            }
        }

        private static void Add(ZipArchive zip, string name, string xml)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var stream = entry.Open())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(xml);
        }

        private const string XmlHead = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n";

        private const string ContentTypes = XmlHead +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet3.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
            "</Types>";

        private const string RootRels = XmlHead +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>";

        private const string WorkbookRels = XmlHead +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet2.xml\"/>" +
            "<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
            "<Relationship Id=\"rId4\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet3.xml\"/>" +
            "</Relationships>";

        private static string Workbook(ClubInfoSheetText text, int rowCount)
        {
            var last = Math.Max(2, rowCount + 1);
            return XmlHead +
                "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                "<bookViews><workbookView activeTab=\"0\"/></bookViews>" +
                "<sheets><sheet name=\"" + Esc(text.SheetName) + "\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"" + Esc(text.InfoSheetName) + "\" sheetId=\"2\" r:id=\"rId2\"/><sheet name=\"" + Esc(text.KitSheetName) + "\" sheetId=\"3\" r:id=\"rId4\"/></sheets>" +
                "<definedNames><definedName name=\"_xlnm._FilterDatabase\" localSheetId=\"0\" hidden=\"1\">'" + Esc(text.SheetName) + "'!$A$1:$Q$" + last + "</definedName></definedNames>" +
                "</workbook>";
        }

        private const string StylesBase = XmlHead +
            "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
            "<fonts count=\"4\">" +
            "<font><sz val=\"11\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
            "<font><b/><sz val=\"11\"/><color rgb=\"FFFFFFFF\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
            "<font><b/><sz val=\"14\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
            "<font><sz val=\"11\"/><color rgb=\"FF595959\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
            "</fonts>" +
            "<fills count=\"5\">" +
            "<fill><patternFill patternType=\"none\"/></fill>" +
            "<fill><patternFill patternType=\"gray125\"/></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF1F3864\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFEDEDED\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFFFF2CC\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            "</fills>" +
            "<borders count=\"2\">" +
            "<border><left/><right/><top/><bottom/><diagonal/></border>" +
            "<border><left style=\"thin\"><color rgb=\"FFBFBFBF\"/></left><right style=\"thin\"><color rgb=\"FFBFBFBF\"/></right><top style=\"thin\"><color rgb=\"FFBFBFBF\"/></top><bottom style=\"thin\"><color rgb=\"FFBFBFBF\"/></bottom><diagonal/></border>" +
            "</borders>" +
            "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
            "<cellXfs count=\"7\">" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
            "<xf numFmtId=\"49\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"left\" vertical=\"center\"/></xf>" +
            "<xf numFmtId=\"49\" fontId=\"3\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"/>" +
            "<xf numFmtId=\"49\" fontId=\"0\" fillId=\"4\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyFill=\"1\" applyBorder=\"1\"/>" +
            "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment vertical=\"top\" wrapText=\"1\"/></xf>" +
            "<xf numFmtId=\"3\" fontId=\"0\" fillId=\"4\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyFill=\"1\" applyBorder=\"1\"/>" +
            "</cellXfs>" +
            "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
            "</styleSheet>";

        private const int FirstSwatchStyle = 7;

        private static string Hex(int[] rgb) { return "FF" + rgb[0].ToString("X2") + rgb[1].ToString("X2") + rgb[2].ToString("X2"); }

        /// <summary>The style sheet with a swatch style per kit colour (cell styles 7-19) and the conditional formats (dxf 0-12) that colour the kit cells.</summary>
        private static string Styles()
        {
            var fills = new StringBuilder();
            var xfs = new StringBuilder();
            var dxfs = new StringBuilder("<dxfs count=\"" + Kits.ColourCount + "\">");
            for (var k = 0; k < Kits.ColourCount; k++)
            {
                var rgb = Kits.Rgb[k];
                var dark = 0.299 * rgb[0] + 0.587 * rgb[1] + 0.114 * rgb[2] < 140;
                fills.Append("<fill><patternFill patternType=\"solid\"><fgColor rgb=\"" + Hex(rgb) + "\"/><bgColor indexed=\"64\"/></patternFill></fill>");
                xfs.Append("<xf numFmtId=\"0\" fontId=\"0\" fillId=\"" + (5 + k) + "\" borderId=\"1\" xfId=\"0\" applyFill=\"1\" applyBorder=\"1\"/>");
                dxfs.Append("<dxf>" + (dark ? "<font><color rgb=\"FFFFFFFF\"/></font>" : string.Empty) +
                            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"" + Hex(rgb) + "\"/><bgColor rgb=\"" + Hex(rgb) + "\"/></patternFill></fill></dxf>");
            }
            dxfs.Append("</dxfs>");
            return StylesBase
                .Replace("<fills count=\"5\">", "<fills count=\"" + (5 + Kits.ColourCount) + "\">").Replace("</fills>", fills + "</fills>")
                .Replace("<cellXfs count=\"7\">", "<cellXfs count=\"" + (FirstSwatchStyle + Kits.ColourCount) + "\">").Replace("</cellXfs>", xfs + "</cellXfs>")
                .Replace("</cellStyles>", "</cellStyles>" + dxfs);
        }

        private static string ClubsSheet(List<ClubInfoRow> rows, ClubInfoSheetText text)
        {
            var sb = new StringBuilder(XmlHead);
            var last = Math.Max(2, rows.Count + 1);
            sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            sb.Append("<sheetViews><sheetView tabSelected=\"1\" workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/><selection pane=\"bottomLeft\" activeCell=\"F2\" sqref=\"F2\"/></sheetView></sheetViews>");
            sb.Append("<sheetFormatPr defaultRowHeight=\"15\"/>");
            sb.Append("<cols>");
            for (var i = 0; i < Widths.Length; i++)
                sb.Append("<col min=\"" + (i + 1) + "\" max=\"" + (i + 1) + "\" width=\"" + Widths[i].ToString(CultureInfo.InvariantCulture) + "\" customWidth=\"1\"/>");
            sb.Append("</cols><sheetData>");
            sb.Append("<row r=\"1\" ht=\"22\" customHeight=\"1\">");
            for (var c = 0; c < ClubInfoCsv.Header.Length; c++) sb.Append(Cell(c, 1, ClubInfoCsv.Header[c], Header));
            sb.Append("</row>");
            var r = 2;
            foreach (var row in rows)
            {
                sb.Append("<row r=\"" + r + "\">");
                var values = new[] { row.Uid, row.Nation, row.League, row.FmName, row.WrittenName, row.City, row.Nickname, row.Stadium };
                for (var c = 0; c < values.Length; c++) sb.Append(Cell(c, r, values[c], c < 5 ? ReadOnly : Editable));
                sb.Append(NumberCell(8, r, row.Capacity));
                for (var k = 0; k < 8; k++) sb.Append(NumberCell(9 + k, r, row.Kit != null && k < row.Kit.Length ? row.Kit[k] : null));
                sb.Append("</row>");
                r++;
            }
            sb.Append("</sheetData>");
            sb.Append("<autoFilter ref=\"A1:Q" + last + "\"/>");
            // the colour cells of the kit take the colour of their number
            var colourCells = "K2:M" + last + " O2:Q" + last;
            sb.Append("<conditionalFormatting sqref=\"" + colourCells + "\">");
            for (var k = 0; k < Kits.ColourCount; k++)
                sb.Append("<cfRule type=\"cellIs\" dxfId=\"" + k + "\" priority=\"" + (k + 1) + "\" operator=\"equal\"><formula>" + k + "</formula></cfRule>");
            sb.Append("</conditionalFormatting>");
            sb.Append("<dataValidations count=\"6\">");
            sb.Append(WholeValidation("J2:J" + last + " N2:N" + last, 0, Kits.PatternCount - 1, text.BadKitTitle, text.BadPatternMessage));
            sb.Append(WholeValidation(colourCells, 0, Kits.ColourCount - 1, text.BadKitTitle, text.BadColourMessage));
            sb.Append(Validation("F", last, text.MaxCity, text));
            sb.Append(Validation("G", last, text.MaxNickname, text));
            sb.Append(Validation("H", last, text.MaxStadium, text));
            sb.Append("<dataValidation type=\"whole\" operator=\"between\" allowBlank=\"1\" showErrorMessage=\"1\" errorTitle=\"" + Esc(text.BadCapacityTitle) +
                      "\" error=\"" + Esc(string.Format(text.BadCapacityMessage, text.MinCapacity, text.MaxCapacity)) + "\" sqref=\"I2:I" + last + "\"><formula1>" +
                      text.MinCapacity + "</formula1><formula2>" + text.MaxCapacity + "</formula2></dataValidation>");
            sb.Append("</dataValidations>");
            sb.Append("<pageMargins left=\"0.7\" right=\"0.7\" top=\"0.75\" bottom=\"0.75\" header=\"0.3\" footer=\"0.3\"/>");
            sb.Append("</worksheet>");
            return sb.ToString();
        }

        private static string WholeValidation(string sqref, int min, int max, string title, string message)
        {
            return "<dataValidation type=\"whole\" operator=\"between\" allowBlank=\"1\" showErrorMessage=\"1\" errorTitle=\"" + Esc(title) + "\" error=\"" + Esc(message) +
                   "\" sqref=\"" + sqref + "\"><formula1>" + min + "</formula1><formula2>" + max + "</formula2></dataValidation>";
        }

        /// <summary>The reference sheet: the number, name and a sample of every kit colour, and the stripe patterns.</summary>
        private static string KitSheet(ClubInfoSheetText text)
        {
            var sb = new StringBuilder(XmlHead);
            sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            sb.Append("<sheetViews><sheetView workbookViewId=\"0\"/></sheetViews><sheetFormatPr defaultRowHeight=\"15\"/>");
            sb.Append("<cols><col min=\"1\" max=\"1\" width=\"8\" customWidth=\"1\"/><col min=\"2\" max=\"2\" width=\"26\" customWidth=\"1\"/><col min=\"3\" max=\"3\" width=\"14\" customWidth=\"1\"/></cols><sheetData>");
            var r = 1;
            sb.Append("<row r=\"" + r + "\" ht=\"24\" customHeight=\"1\">" + Cell(0, r, text.KitColoursTitle, Title) + "</row>"); r += 2;
            sb.Append("<row r=\"" + r + "\">" + Cell(0, r, text.KitNumberHeader, Header) + Cell(1, r, text.KitNameHeader, Header) + Cell(2, r, text.KitSampleHeader, Header) + "</row>"); r++;
            for (var k = 0; k < Kits.ColourCount; k++, r++)
                sb.Append("<row r=\"" + r + "\"><c r=\"A" + r + "\" s=\"" + ReadOnly + "\"><v>" + k + "</v></c>" + Cell(1, r, k < text.ColourNames.Length ? text.ColourNames[k] : k.ToString(), ReadOnly) +
                          "<c r=\"C" + r + "\" s=\"" + (FirstSwatchStyle + k) + "\"/></row>");
            r++;
            sb.Append("<row r=\"" + r + "\" ht=\"24\" customHeight=\"1\">" + Cell(0, r, text.KitPatternsTitle, Title) + "</row>"); r += 2;
            sb.Append("<row r=\"" + r + "\">" + Cell(0, r, text.KitNumberHeader, Header) + Cell(1, r, text.KitNameHeader, Header) + "</row>"); r++;
            for (var k = 0; k < Kits.PatternCount; k++, r++)
                sb.Append("<row r=\"" + r + "\"><c r=\"A" + r + "\" s=\"" + ReadOnly + "\"><v>" + k + "</v></c>" + Cell(1, r, k < text.PatternNames.Length ? text.PatternNames[k] : k.ToString(), ReadOnly) + "</row>");
            sb.Append("</sheetData><pageMargins left=\"0.7\" right=\"0.7\" top=\"0.75\" bottom=\"0.75\" header=\"0.3\" footer=\"0.3\"/></worksheet>");
            return sb.ToString();
        }

        private static string Validation(string column, int last, int max, ClubInfoSheetText text)
        {
            return "<dataValidation type=\"textLength\" operator=\"lessThanOrEqual\" allowBlank=\"1\" showErrorMessage=\"1\" errorTitle=\"" + Esc(text.TooLongTitle) +
                   "\" error=\"" + Esc(string.Format(text.TooLongMessage, max)) + "\" sqref=\"" + column + "2:" + column + last + "\"><formula1>" + max + "</formula1></dataValidation>";
        }

        private static string InfoSheet(ClubInfoSheetText text)
        {
            var sb = new StringBuilder(XmlHead);
            sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            sb.Append("<sheetViews><sheetView workbookViewId=\"0\"/></sheetViews><sheetFormatPr defaultRowHeight=\"15\"/>");
            sb.Append("<cols><col min=\"1\" max=\"1\" width=\"110\" customWidth=\"1\"/></cols><sheetData>");
            sb.Append("<row r=\"1\" ht=\"24\" customHeight=\"1\">" + Cell(0, 1, text.InfoTitle, Title) + "</row>");
            var r = 3;
            foreach (var line in text.InfoLines)
            {
                sb.Append("<row r=\"" + r + "\">" + Cell(0, r, line, Wrapped) + "</row>");
                r++;
            }
            sb.Append("</sheetData><pageMargins left=\"0.7\" right=\"0.7\" top=\"0.75\" bottom=\"0.75\" header=\"0.3\" footer=\"0.3\"/></worksheet>");
            return sb.ToString();
        }

        private static string Cell(int column, int row, string value, int style)
        {
            var reference = ColumnName(column) + row;
            if (string.IsNullOrEmpty(value)) return "<c r=\"" + reference + "\" s=\"" + style + "\"/>";
            var space = value.Length != value.Trim().Length || value.Contains("\n") ? " xml:space=\"preserve\"" : string.Empty;
            return "<c r=\"" + reference + "\" s=\"" + style + "\" t=\"inlineStr\"><is><t" + space + ">" + Esc(value) + "</t></is></c>";
        }

        /// <summary>A real number cell (Excel shows it with a thousands separator and can sort it).</summary>
        private static string NumberCell(int column, int row, string value)
        {
            var reference = ColumnName(column) + row;
            var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
            if (digits.Length == 0) return "<c r=\"" + reference + "\" s=\"" + EditableNumber + "\"/>";
            return "<c r=\"" + reference + "\" s=\"" + EditableNumber + "\"><v>" + digits + "</v></c>";
        }

        private static string ColumnName(int index)
        {
            var name = string.Empty;
            for (var n = index + 1; n > 0; n = (n - 1) / 26) name = (char)('A' + (n - 1) % 26) + name;
            return name;
        }

        /// <summary>XML text with the characters Excel does not allow removed.</summary>
        private static string Esc(string s)
        {
            var sb = new StringBuilder();
            foreach (var ch in s ?? string.Empty)
            {
                if (ch < 0x20 && ch != '\t' && ch != '\n' && ch != '\r') continue;
                switch (ch)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    default: sb.Append(ch); break;
                }
            }
            return sb.ToString();
        }

        // ---- reading (also files saved by Excel: shared strings, rich text, numbers) ---------------------

        private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace Pkg = "http://schemas.openxmlformats.org/package/2006/relationships";

        private static XDocument Load(ZipArchive zip, string name)
        {
            var entry = zip.GetEntry(name);
            if (entry == null) return null;
            using (var stream = entry.Open()) return XDocument.Load(stream);
        }

        /// <summary>The cells of the "Clubs" sheet (or the first sheet) as rows of text.</summary>
        public static List<List<string>> ReadTable(string path)
        {
            using (var zip = ZipFile.OpenRead(path))
            {
                var shared = ReadSharedStrings(zip);
                var sheet = Load(zip, FindSheetPath(zip));
                if (sheet == null) throw new InvalidDataException("No worksheet found in " + path);
                var rows = new List<List<string>>();
                foreach (var row in sheet.Descendants(Main + "row"))
                {
                    var cells = new List<string>();
                    foreach (var c in row.Elements(Main + "c"))
                    {
                        var column = ColumnIndex((string)c.Attribute("r"));
                        while (cells.Count < column) cells.Add(string.Empty);
                        var value = CellValue(c, shared);
                        if (cells.Count == column) cells.Add(value); else cells[column] = value;
                    }
                    if (cells.Any(x => x.Length > 0)) rows.Add(cells);
                }
                return rows;
            }
        }

        private static string CellValue(XElement c, List<string> shared)
        {
            var type = (string)c.Attribute("t");
            if (type == "inlineStr") return Text(c.Element(Main + "is"));
            var value = (string)c.Element(Main + "v") ?? string.Empty;
            if (type == "s")
            {
                int i;
                return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out i) && i >= 0 && i < shared.Count ? shared[i] : string.Empty;
            }
            if (type == null || type == "n")
            {
                double d;
                if (value.IndexOfAny(new[] { 'E', 'e', '.' }) >= 0 && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d == Math.Floor(d) && Math.Abs(d) < 1e15)
                    return ((long)d).ToString(CultureInfo.InvariantCulture);
            }
            return value;
        }

        /// <summary>The text of a string item: its text runs, plain or rich, without the phonetic hints.</summary>
        private static string Text(XElement item)
        {
            if (item == null) return string.Empty;
            return string.Concat(item.Elements(Main + "t").Concat(item.Elements(Main + "r").Elements(Main + "t")).Select(t => t.Value));
        }

        private static List<string> ReadSharedStrings(ZipArchive zip)
        {
            var doc = Load(zip, "xl/sharedStrings.xml");
            return doc == null ? new List<string>() : doc.Descendants(Main + "si").Select(Text).ToList();
        }

        /// <summary>The part name of the sheet called "Clubs", or of the first sheet.</summary>
        private static string FindSheetPath(ZipArchive zip)
        {
            var book = Load(zip, "xl/workbook.xml");
            var sheets = book.Descendants(Main + "sheet").ToList();
            var chosen = sheets.FirstOrDefault(x => string.Equals((string)x.Attribute("name"), "Clubs", StringComparison.OrdinalIgnoreCase)) ?? sheets.First();
            var relId = (string)chosen.Attribute(Rel + "id");
            var rels = Load(zip, "xl/_rels/workbook.xml.rels");
            var target = rels == null ? null : rels.Descendants(Pkg + "Relationship").Where(r => (string)r.Attribute("Id") == relId).Select(r => (string)r.Attribute("Target")).FirstOrDefault();
            if (string.IsNullOrEmpty(target)) return "xl/worksheets/sheet1.xml";
            return target.StartsWith("/") ? target.TrimStart('/') : "xl/" + target;
        }

        private static int ColumnIndex(string reference)
        {
            var n = 0;
            foreach (var ch in reference ?? string.Empty)
            {
                if (!char.IsLetter(ch)) break;
                n = n * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
            }
            return Math.Max(0, n - 1);
        }
    }
    /// <summary>Writes and reads the club information as an Excel workbook (.xlsx) or a CSV file, by the file extension.</summary>
    public static class ClubInfoFile
    {
        public static bool IsWorkbook(string path)
        {
            return string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase);
        }

        public static void Write(string path, IEnumerable<ClubInfoRow> rows, ClubInfoSheetText text)
        {
            if (IsWorkbook(path)) ClubInfoXlsx.Write(path, rows, text); else ClubInfoCsv.Write(path, rows);
        }

        public static List<ClubInfoRow> Read(string path)
        {
            return IsWorkbook(path) ? ClubInfoCsv.FromTable(ClubInfoXlsx.ReadTable(path)) : ClubInfoCsv.Read(path);
        }
    }
}
