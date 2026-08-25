using System.Collections;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Reflection;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Oxs = DocumentFormat.OpenXml.Spreadsheet;

namespace NetPad.Presentation;

/// <summary>
/// A simple in-memory spreadsheet workbook that can be populated by scripts and saved to an
/// .xlsx file. Created via <c>Util.ToSpreadsheet</c> or <c>new Workbook()</c>.
/// </summary>
public sealed class Workbook
{
    private readonly List<Worksheet> _sheets = [];
    private int _unnamedSheetCounter;

    /// <summary>Adds a new sheet with the given name (auto-named when omitted).</summary>
    public Worksheet AddSheet(string? name = null)
    {
        name ??= $"Sheet{++_unnamedSheetCounter}";

        if (_sheets.Any(s => s.Name == name))
        {
            throw new ArgumentException($"A sheet named '{name}' already exists.", nameof(name));
        }

        var sheet = new Worksheet(name);
        _sheets.Add(sheet);
        return sheet;
    }

    public IReadOnlyList<Worksheet> Sheets => _sheets;

    public Worksheet this[int index] => _sheets[index];

    public Worksheet this[string name]
        => _sheets.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
           ?? throw new KeyNotFoundException($"No sheet named '{name}'.");

    /// <summary>Saves the workbook to a file path.</summary>
    public void Save(string path)
    {
        using var stream = File.Create(path);
        Save(stream);
    }

    /// <summary>Saves the workbook to a stream.</summary>
    public void Save(Stream stream) => SpreadsheetWriter.Write(stream, this);
}

/// <summary>
/// A single worksheet within a <see cref="Workbook"/>. Rows and columns are 1-based.
/// </summary>
public sealed class Worksheet
{
    private readonly Dictionary<int, Dictionary<int, Cell>> _rows = [];

    internal Worksheet(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public IEnumerable<int> RowNumbers => _rows.Keys.OrderBy(r => r);

    internal IEnumerable<KeyValuePair<int, IReadOnlyList<Cell>>> GetRows()
        => _rows
            .OrderBy(kv => kv.Key)
            .Select(kv => new KeyValuePair<int, IReadOnlyList<Cell>>(
                kv.Key,
                kv.Value.Values.OrderBy(c => c.Column).ToList()));

    public Cell GetOrCreateCell(int row, int column)
    {
        if (row < 1 || column < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(row), "Row and column are 1-based.");
        }

        if (!_rows.TryGetValue(row, out var cells))
        {
            cells = [];
            _rows[row] = cells;
        }

        if (!cells.TryGetValue(column, out var cell))
        {
            cell = new Cell(row, column);
            cells[column] = cell;
        }

        return cell;
    }

    public Cell this[int row, int column] => GetOrCreateCell(row, column);

    /// <summary>
    /// Sets a cell value. Strings beginning with '=' are treated as formulas and are stored
    /// un-evaluated. Supports strings, numbers, booleans and dates.
    /// </summary>
    public void SetCell(int row, int column, object? value)
    {
        var cell = GetOrCreateCell(row, column);

        if (value is string s && s.StartsWith('='))
        {
            cell.Formula = s[1..]; // Store without the leading '='.
            cell.Value = null;
            return;
        }

        cell.Value = value;
        cell.Formula = null;
    }
}

/// <summary>A single worksheet cell.</summary>
public sealed class Cell(int row, int column)
{
    public int Row { get; } = row;

    public int Column { get; } = column;

    public object? Value { get; set; }

    /// <summary>Formula text without the leading '='; stored un-evaluated.</summary>
    public string? Formula { get; set; }
}

/// <summary>
/// Extensions for creating workbooks from sequences of objects.
/// </summary>
public static class SpreadsheetExtensions
{
    /// <summary>
    /// Creates a workbook with one sheet containing the sequence's values. Scalars produce a
    /// single column; anonymous objects/POCOs produce one column per readable property;
    /// dictionaries/expando objects produce unioned key columns.
    /// </summary>
    public static Workbook ToSpreadsheet<T>(this IEnumerable<T>? source, string? sheetName = null)
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet(sheetName);

        if (source == null)
        {
            return workbook;
        }

        const int firstDataRow = 2;
        var rowIndex = firstDataRow;
        List<string> columns;

        // Materialize rows so dictionary keys can be unioned before writing.
        var items = source.Cast<object?>().ToList();
        var firstNonNull = items.FirstOrDefault(i => i != null);

        if (firstNonNull is string or sbyte or byte or short or ushort or int or uint or long or ulong
            or float or double or decimal or bool or char or DateTime or DateTimeOffset
            or DateOnly or TimeOnly or TimeSpan or Guid)
        {
            columns = ["Value"];
            WriteHeader(sheet, columns);
            foreach (var item in items)
            {
                sheet.SetCell(rowIndex++, 1, item);
            }

            return workbook;
        }

        if (firstNonNull is ExpandoObject or IDictionary<string, object?> or IDictionary)
        {
            columns = [];
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var rows = new List<Dictionary<string, object?>>();

            foreach (var item in items)
            {
                var row = new Dictionary<string, object?>();
                if (item != null)
                {
                    foreach (var kv in EnumerateEntries(item))
                    {
                        row[kv.Key] = kv.Value;
                        if (seen.Add(kv.Key))
                        {
                            columns.Add(kv.Key);
                        }
                    }
                }
                rows.Add(row);
            }

            WriteHeader(sheet, columns);
            foreach (var row in rows)
            {
                for (var c = 0; c < columns.Count; c++)
                {
                    row.TryGetValue(columns[c], out var v);
                    sheet.SetCell(rowIndex, c + 1, v);
                }
                rowIndex++;
            }

            return workbook;
        }

        columns = firstNonNull?.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .Select(p => p.Name)
            .ToList() ?? [];

        WriteHeader(sheet, columns);
        foreach (var item in items)
        {
            for (var c = 0; c < columns.Count; c++)
            {
                var value = item?.GetType().GetProperty(columns[c])?.GetValue(item);
                sheet.SetCell(rowIndex, c + 1, value);
            }
            rowIndex++;
        }

        return workbook;
    }

    private static void WriteHeader(Worksheet sheet, IReadOnlyList<string> columns)
    {
        for (var c = 0; c < columns.Count; c++)
        {
            sheet.SetCell(1, c + 1, columns[c]);
        }
    }

    private static IEnumerable<KeyValuePair<string, object?>> EnumerateEntries(object item)
    {
        if (item is IDictionary<string, object?> dict)
        {
            foreach (var kv in dict)
            {
                yield return kv;
            }

            yield break;
        }

        if (item is IDictionary nonGeneric)
        {
            foreach (var key in nonGeneric.Keys)
            {
                yield return new KeyValuePair<string, object?>(key?.ToString() ?? string.Empty, nonGeneric[key!]);
            }
        }
    }
}

internal static class SpreadsheetWriter
{
    private const int DateFormatStyleIndex = 1;

    public static void Write(Stream stream, Workbook workbook)
    {
        using var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();

        AddStyles(workbookPart);

        var sheetsElement = new Oxs.Sheets();
        uint sheetId = 1;

        var sheets = workbook.Sheets.Count > 0
            ? workbook.Sheets
            : [new Workbook().AddSheet("Sheet1")]; // Excel requires at least one sheet.

        foreach (var sheet in sheets)
        {
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            worksheetPart.Worksheet = BuildWorksheet(sheet);
            sheetsElement.AppendChild(new Oxs.Sheet
            {
                Name = sheet.Name,
                SheetId = sheetId,
                Id = workbookPart.GetIdOfPart(worksheetPart)
            });
            sheetId++;
        }

        workbookPart.Workbook = new Oxs.Workbook(sheetsElement);
    }

    private static Oxs.Worksheet BuildWorksheet(Worksheet sheet)
    {
        var sheetData = new Oxs.SheetData();

        foreach (var row in sheet.GetRows())
        {
            var rowElement = new Oxs.Row { RowIndex = (uint)row.Key };

            foreach (var cell in row.Value)
            {
                rowElement.AppendChild(BuildCellElement(cell));
            }

            sheetData.AppendChild(rowElement);
        }

        return new Oxs.Worksheet(sheetData);
    }

    private static Oxs.Cell BuildCellElement(Cell cell)
    {
        var reference = $"{ColumnLetter(cell.Column)}{cell.Row}";

        var element = new Oxs.Cell { CellReference = reference };

        if (cell.Formula != null)
        {
            element.CellFormula = new Oxs.CellFormula(cell.Formula);
            return element;
        }

        switch (cell.Value)
        {
            case null:
                break;

            case string s:
                element.DataType = Oxs.CellValues.InlineString;
                element.InlineString = new Oxs.InlineString(new Oxs.Text(s) { Space = SpaceProcessingModeValues.Preserve });
                break;

            case bool b:
                element.DataType = Oxs.CellValues.Boolean;
                element.CellValue = new Oxs.CellValue(b ? "1" : "0");
                break;

            case DateTime dt:
                element.StyleIndex = DateFormatStyleIndex;
                element.CellValue = new Oxs.CellValue(dt.ToOADate().ToString(CultureInfo.InvariantCulture));
                break;

            case DateTimeOffset dto:
                element.StyleIndex = DateFormatStyleIndex;
                element.CellValue = new Oxs.CellValue(dto.UtcDateTime.ToOADate().ToString(CultureInfo.InvariantCulture));
                break;

            case DateOnly d:
                element.StyleIndex = DateFormatStyleIndex;
                element.CellValue = new Oxs.CellValue(d.ToDateTime(TimeOnly.MinValue).ToOADate().ToString(CultureInfo.InvariantCulture));
                break;

            case TimeOnly t:
                element.DataType = Oxs.CellValues.InlineString;
                element.InlineString = new Oxs.InlineString(new Oxs.Text(t.ToString("HH:mm:ss.fffffff")));
                break;

            case byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                element.CellValue = new Oxs.CellValue(Convert.ToString(cell.Value, CultureInfo.InvariantCulture)!);
                break;

            case Guid g:
                element.DataType = Oxs.CellValues.InlineString;
                element.InlineString = new Oxs.InlineString(new Oxs.Text(g.ToString()));
                break;

            default:
                element.DataType = Oxs.CellValues.InlineString;
                element.InlineString = new Oxs.InlineString(
                    new Oxs.Text(Convert.ToString(cell.Value, CultureInfo.InvariantCulture) ?? string.Empty)
                    {
                        Space = SpaceProcessingModeValues.Preserve
                    });
                break;
        }

        return element;
    }

    private static void AddStyles(WorkbookPart workbookPart)
    {
        var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();

        stylesPart.Stylesheet = new Oxs.Stylesheet(
            new Oxs.Fonts(new Oxs.Font()),
            new Oxs.Fills(
                new Oxs.Fill(new Oxs.PatternFill { PatternType = Oxs.PatternValues.None }),
                new Oxs.Fill(new Oxs.PatternFill { PatternType = Oxs.PatternValues.Gray125 })),
            new Oxs.Borders(new Oxs.Border()),
            new Oxs.CellStyleFormats(
                new Oxs.CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0 }),
            new Oxs.CellFormats(
                new Oxs.CellFormat { FormatId = 0, NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0 },
                new Oxs.CellFormat
                {
                    FormatId = 0,
                    NumberFormatId = 14, // Built-in locale-aware short date format.
                    FontId = 0,
                    FillId = 0,
                    BorderId = 0,
                    ApplyNumberFormat = new BooleanValue(true)
                })
        );
    }

    internal static string ColumnLetter(int column)
    {
        var letters = "";
        while (column > 0)
        {
            var rem = (column - 1) % 26;
            letters = (char)('A' + rem) + letters;
            column = (column - 1) / 26;
        }
        return letters;
    }
}
