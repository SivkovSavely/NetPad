using System.Dynamic;
using System.Globalization;
using System.Text;
using NetPad.Presentation;

namespace NetPad.ExecutionModel.ScriptServices.Tests;

public sealed class CsvTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    public void Dispose()
    {
        foreach (var file in _tempFiles.Where(File.Exists))
        {
            File.Delete(file);
        }
    }

    private string TempPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"netpad-csv-{Guid.NewGuid():N}.csv");
        _tempFiles.Add(path);
        return path;
    }

    private sealed record Person(string Name, int Age);

    [Fact]
    public void AnonymousObjects_ProduceStableHeadersAndRows()
    {
        var csv = Util.ToCsvString(new[]
        {
            new { Name = "Alice", Age = 30 },
            new { Name = "Bob", Age = 40 }
        });

        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Equal("Name,Age", lines[0]);
        Assert.Equal("Alice,30", lines[1]);
        Assert.Equal("Bob,40", lines[2]);
    }

    [Fact]
    public void ValuesContainingCommasQuotesAndNewlinesAreQuoted()
    {
        var csv = Util.ToCsvString(new[]
        {
            new { Value = "has,comma" },
            new { Value = "has\"quote" },
            new { Value = "has\nnewline" },
            new { Value = "plain" }
        });

        var lines = ParseCsvLines(csv);
        Assert.Equal(5, lines.Count); // header + 4 rows (one spans two physical lines)
        Assert.Equal("Value", lines[0][0]);
        Assert.Equal("has,comma", lines[1][0]);
        Assert.Equal("has\"quote", lines[2][0]);
        Assert.Equal("has\nnewline", lines[3][0]);
        Assert.Equal("plain", lines[4][0]);
    }

    [Fact]
    public void NullValues_AreEmptyFields()
    {
        var csv = Util.ToCsvString(new[] { new { A = (string?)"x", B = (string?)null } });

        Assert.Equal("A,B\r\nx,\r\n", csv);
    }

    [Fact]
    public void CustomDelimiter_IsUsedAndEscapedAround()
    {
        var options = new CsvOptions { Delimiter = ';' };
        var csv = Util.ToCsvString(new[] { new { A = "x;y", B = 1 } }, options);

        Assert.Equal("A;B\r\n\"x;y\";1\r\n", csv);
    }

    [Fact]
    public void CultureOverride_FormatsNumbers()
    {
        var options = new CsvOptions { Culture = CultureInfo.GetCultureInfoByIetfLanguageTag("de-DE") };
        var csv = Util.ToCsvString(new[] { new { Value = 12.5 } }, options);

        Assert.Contains("12,5", csv);
    }

    [Fact]
    public void ScalarSequences_GetDefaultValueHeader()
    {
        Assert.Equal("Value\r\n1\r\n2\r\n", Util.ToCsvString(new[] { 1, 2 }));
    }

    [Fact]
    public void DictionaryKeysAreUnioned()
    {
        dynamic row1 = new ExpandoObject();
        row1.A = 1;
        row1.B = 2;

        dynamic row2 = new ExpandoObject();
        row2.A = 3;
        row2.C = 4;

        var csv = Util.ToCsvString(new object[] { row1, row2 });
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("A,B,C", lines[0]);
        Assert.Equal("1,2,", lines[1]);
        Assert.Equal("3,,4", lines[2]);
    }

    [Fact]
    public void IncludeHeaderFalse_OmitsHeaderRow()
    {
        var options = new CsvOptions { IncludeHeader = false };
        var csv = Util.ToCsvString(new[] { new { A = 1 } }, options);

        Assert.Equal("1\r\n", csv);
    }

    [Fact]
    public void WriteCsv_StreamsToFile()
    {
        var path = TempPath();

        Util.WriteCsv(new[] { new Person("Ann", 20), new Person("Bo", 30) }, path);

        var content = File.ReadAllText(path);
        Assert.Equal("Name,Age\r\nAnn,20\r\nBo,30\r\n", content);
    }

    /// <summary>
    /// Minimal RFC4180 parser so assertions observe logical records, not raw line splits.
    /// </summary>
    private static List<List<string>> ParseCsvLines(string csv)
    {
        var records = new List<List<string>>();
        var field = new StringBuilder();
        var record = new List<string>();
        var inQuotes = false;

        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < csv.Length && csv[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                record.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\n')
            {
                if (field.Length > 0 || record.Count > 0)
                {
                    record.Add(field.ToString());
                    field.Clear();
                    records.Add(record);
                    record = [];
                }
            }
            else if (c != '\r')
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            records.Add(record);
        }

        return records;
    }
}
