using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using NetPad.ExecutionModel.ScriptServices;
using NetPad.Presentation;
using Workbook = NetPad.Presentation.Workbook;

namespace NetPad.Runtime.Tests.Presentation;

public sealed class XhtmlWriterTests
{
    [Fact]
    public void BuildsStandaloneDocumentReusingHtmlPipeline()
    {
        var writer = Util.CreateXhtmlWriter();
        writer.WriteLine(new { Name = "Alice" });
        writer.Write("plain text");

        var html = writer.ToString();

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<html", html);
        Assert.Contains("</body>", html);
        // Serialized through the standard pipeline: property dumps render as tables/groups.
        Assert.Contains("Alice", html);
        Assert.Contains("plain", html);
        // Text groups render spaces as non-breaking spaces in the presentation pipeline.
        Assert.Contains("&nbsp;", html);
    }
}

public sealed class SpreadsheetTests
{
    private string TempPath(string ext = ".xlsx")
    {
        var path = Path.Combine(Path.GetTempPath(), $"netpad-xlsx-{Guid.NewGuid():N}{ext}");
        return path;
    }

    [Fact]
    public void ToSpreadsheet_FromAnonymousObjects_WritesHeadersAndCells()
    {
        var path = TempPath();

        new[]
        {
            new { Name = "Alice", Age = 30 },
            new { Name = "Bob", Age = 40 }
        }.ToSpreadsheet().Save(path);

        using var doc = SpreadsheetDocument.Open(path, false);
        var sheetData = doc.WorkbookPart!.WorksheetParts.First().Worksheet.GetFirstChild<SheetData>()!;

        var rows = sheetData.Elements<Row>().ToList();
        Assert.Equal(3, rows.Count);
        Assert.Equal("Name", GetCellText(rows[0], "A1"));
        Assert.Equal("Age", GetCellText(rows[0], "B1"));
        Assert.Equal("Alice", GetCellText(rows[1], "A2"));
        Assert.Equal("30", GetCellValue(rows[1], "B2"));
    }

    [Fact]
    public void FormulasAreStoredUnevaluated()
    {
        var path = TempPath();

        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Calc");
        sheet.SetCell(1, 1, 2);
        sheet.SetCell(2, 1, 3);
        sheet.SetCell(3, 1, "=SUM(A1:A2)");
        workbook.Save(path);

        using var doc = SpreadsheetDocument.Open(path, false);
        var sheetData = doc.WorkbookPart!.WorksheetParts.First().Worksheet.GetFirstChild<SheetData>()!;
        var formulaCell = sheetData.Descendants<DocumentFormat.OpenXml.Spreadsheet.Cell>()
            .Single(c => c.CellReference == "A3");

        Assert.NotNull(formulaCell.CellFormula);
        Assert.Equal("SUM(A1:A2)", formulaCell.CellFormula!.InnerText);
        Assert.Null(formulaCell.CellValue); // No cached value — stored un-evaluated.
    }

    [Fact]
    public void MultipleSheetsAreWritten()
    {
        var path = TempPath();

        var workbook = new Workbook();
        workbook.AddSheet("One").SetCell(1, 1, "first");
        workbook.AddSheet("Two").SetCell(1, 1, "second");
        workbook.Save(path);

        using var doc = SpreadsheetDocument.Open(path, false);
        var sheets = doc.WorkbookPart!.Workbook.Sheets!.Elements<Sheet>().ToList();

        Assert.Equal(2, sheets.Count);
        Assert.Equal(["One", "Two"], sheets.Select(s => s.Name!.Value));
    }

    [Fact]
    public void DatesGetDateStyle()
    {
        var path = TempPath();

        var workbook = new Workbook();
        workbook.AddSheet("S").SetCell(1, 1, new DateTime(2024, 1, 15));
        workbook.Save(path);

        using var doc = SpreadsheetDocument.Open(path, false);
        var cell = doc.WorkbookPart!.WorksheetParts.First()
            .Worksheet.GetFirstChild<SheetData>()!
            .Descendants<DocumentFormat.OpenXml.Spreadsheet.Cell>()
            .Single(c => c.CellReference == "A1");

        Assert.True(cell.StyleIndex is { } style && style > 0);
    }

    [Fact]
    public void WorksheetIndexerAccessCreatesCells()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet();
        sheet[5, 7].Value = "r5c7";

        Assert.Equal("r5c7", sheet[5, 7].Value);
        Assert.Equal("G", SpreadsheetWriter.ColumnLetter(7));
    }

    private static string? GetCellText(Row row, string reference)
        => row.Elements<DocumentFormat.OpenXml.Spreadsheet.Cell>()
            .FirstOrDefault(c => c.CellReference == reference)?
            .InnerText;

    private static string? GetCellValue(Row row, string reference)
        => row.Elements<DocumentFormat.OpenXml.Spreadsheet.Cell>()
            .FirstOrDefault(c => c.CellReference == reference)?.CellValue?.InnerText;
}
