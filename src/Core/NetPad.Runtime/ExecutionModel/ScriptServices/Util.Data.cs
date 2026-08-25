using System.Collections;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using NetPad.Presentation;

namespace NetPad.ExecutionModel.ScriptServices;

public static partial class Util
{
    #region CSV

    /// <summary>
    /// Converts a sequence of objects to CSV. Supports anonymous objects, POCOs, dictionaries,
    /// expando objects and scalar sequences.
    /// </summary>
    public static string ToCsvString(IEnumerable source, CsvOptions? options = null)
    {
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb);
        WriteCsvCore(source, writer, options ?? new CsvOptions());
        return sb.ToString();
    }

    /// <summary>
    /// Writes a sequence of objects to a CSV file, streaming rows as they are enumerated.
    /// Supports the same shapes as <see cref="ToCsvString"/>.
    /// </summary>
    public static void WriteCsv(IEnumerable source, string path, CsvOptions? options = null)
    {
        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        WriteCsvCore(source, writer, options ?? new CsvOptions());
    }

    private static void WriteCsvCore(IEnumerable source, TextWriter writer, CsvOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);

        var csvWriter = new CsvWriter(writer, options);
        var enumerator = source.GetEnumerator();

        try
        {
            if (!enumerator.MoveNext())
            {
                return;
            }

            var first = enumerator.Current;

            if (first == null)
            {
                return;
            }

            var shape = ClassifyRow(first);

            if (shape.Kind == CsvShapeKind.Dictionary)
            {
                // Dictionary keys are unioned across all rows, so rows are materialized.
                var keys = new List<string>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var rows = new List<Dictionary<string, object?>>();

                void Collect(object? item)
                {
                    var row = new Dictionary<string, object?>();
                    if (item != null)
                    {
                        foreach (var kv in EnumerateDictionaryEntries(item))
                        {
                            row[kv.Key] = kv.Value;
                            if (seen.Add(kv.Key))
                            {
                                keys.Add(kv.Key);
                            }
                        }
                    }
                    rows.Add(row);
                }

                Collect(first);
                while (enumerator.MoveNext())
                {
                    Collect(enumerator.Current);
                }

                if (options.IncludeHeader)
                {
                    csvWriter.WriteRawRow(keys);
                }

                foreach (var row in rows)
                {
                    csvWriter.WriteRow(row, keys);
                }

                return;
            }

            if (options.IncludeHeader)
            {
                if (shape.Kind == CsvShapeKind.Scalar)
                {
                    csvWriter.WriteRawRow("Value");
                }
                else
                {
                    csvWriter.WriteRawRow(shape.Columns!);
                }
            }

            do
            {
                var item = enumerator.Current;

                if (shape.Kind == CsvShapeKind.Scalar)
                {
                    csvWriter.WriteEscaped(FormatCsvValue(item, options));
                    writer.Write(options.NewLine);
                }
                else
                {
                    csvWriter.WritePropertyRow(item!, shape.Columns!);
                }
            } while (enumerator.MoveNext());
        }
        finally
        {
            (enumerator as IDisposable)?.Dispose();
        }
    }

    private static IEnumerable<KeyValuePair<string, object?>> EnumerateDictionaryEntries(object item)
    {
        if (item is IDictionary<string, object?> genericDict)
        {
            foreach (var kv in genericDict)
            {
                yield return kv;
            }

            yield break;
        }

        if (item is IDictionary dict)
        {
            foreach (var key in dict.Keys)
            {
                yield return new KeyValuePair<string, object?>(key?.ToString() ?? string.Empty, dict[key!]);
            }

            yield break;
        }

        throw new InvalidOperationException(
            $"Cannot read dictionary entries from {item.GetType().Name}.");
    }

    private static string FormatCsvValue(object? value, CsvOptions options)
    {
        if (value is null) return string.Empty;

        return value switch
        {
            bool b => b ? "true" : "false",
            string s => s,
            _ => Convert.ToString(value, options.Culture) ?? string.Empty
        };
    }

    private sealed record CsvRowShape(CsvShapeKind Kind, List<string>? Columns);

    private static CsvRowShape ClassifyRow(object item)
    {
        if (item is string or sbyte or byte or short or ushort or int or uint or long or ulong
            or float or double or decimal or bool or char or DateTime or DateTimeOffset
            or DateOnly or TimeOnly or TimeSpan or Guid)
        {
            return new CsvRowShape(CsvShapeKind.Scalar, null);
        }

        if (item is ExpandoObject or IDictionary<string, object?> or IDictionary)
        {
            return new CsvRowShape(CsvShapeKind.Dictionary, null);
        }

        var columns = item.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .Select(p => p.Name)
            .ToList();

        if (columns.Count > 0)
        {
            return new CsvRowShape(CsvShapeKind.Properties, columns);
        }

        // Objects without readable properties fall back to their string representation.
        return new CsvRowShape(CsvShapeKind.Scalar, null);
    }

    private enum CsvShapeKind
    {
        Scalar,
        Properties,
        Dictionary
    }

    private sealed class CsvWriter(TextWriter writer, CsvOptions options)
    {
        public void WriteRawRow(string value) => WriteRawRow([value]);

        public void WriteRawRow(IEnumerable<string> values)
        {
            var first = true;
            foreach (var value in values)
            {
                if (!first) writer.Write(options.Delimiter);
                first = false;
                WriteEscaped(value);
            }

            writer.Write(options.NewLine);
        }

        public void WritePropertyRow(object item, List<string> columns)
        {
            var type = item.GetType();
            var first = true;

            foreach (var column in columns)
            {
                if (!first) writer.Write(options.Delimiter);
                first = false;

                var value = type.GetProperty(column)?.GetValue(item);
                WriteEscaped(FormatCsvValue(value, options));
            }

            writer.Write(options.NewLine);
        }

        public void WriteRow(Dictionary<string, object?> row, IReadOnlyList<string> columns)
        {
            var first = true;
            foreach (var column in columns)
            {
                if (!first) writer.Write(options.Delimiter);
                first = false;

                row.TryGetValue(column, out var value);
                WriteEscaped(FormatCsvValue(value, options));
            }

            writer.Write(options.NewLine);
        }

        public void WriteEscaped(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            var needsQuoting = value.IndexOf(options.Delimiter) >= 0 ||
                               value.Contains('"') ||
                               value.Contains('\r') ||
                               value.Contains('\n');

            if (!needsQuoting)
            {
                writer.Write(value);
                return;
            }

            writer.Write('"');
            writer.Write(value.Replace("\"", "\"\""));
            writer.Write('"');
        }
    }

    #endregion

    #region XHTML

    /// <summary>
    /// Creates a writer that accumulates dumped output and renders it as a standalone HTML document.
    /// Serialization reuses the standard HTML presentation pipeline.
    /// </summary>
    public static XhtmlWriter CreateXhtmlWriter() => new();

    #endregion

    #region My Scripts

    /// <summary>
    /// Lists all scripts found in the configured script library directory.
    /// </summary>
    public static ScriptFileInfo[] GetMyScripts()
    {
        var root = PresentationSettings.GetScriptsDirectoryPath();

        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return [];
        }

        var files = Directory.EnumerateFiles(root, "*.netpad", SearchOption.AllDirectories);

        var infos = new List<ScriptFileInfo>();

        foreach (var file in files)
        {
            var info = ScriptFileInfo.TryCreate(file, root);
            if (info != null)
            {
                infos.Add(info);
            }
        }

        return infos.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    #endregion

    #region Transactions

    private static System.Data.IsolationLevel? _transactionIsolationLevel;
    private static int _dbConnectionOpenCount;

    /// <summary>
    /// The transaction isolation level applied to SQL executed against the script's data connection.
    /// Must be set before the script opens a database connection; unset means unchanged behavior.
    /// </summary>
    public static System.Data.IsolationLevel? TransactionIsolationLevel
    {
        get => _transactionIsolationLevel;
        set
        {
            if (_dbConnectionOpenCount > 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(TransactionIsolationLevel)} must be set before opening a database connection.");
            }

            _transactionIsolationLevel = value;
        }
    }

    /// <summary>
    /// Marks a database connection as opened for the current run (used by SQL-script plumbing so
    /// that <see cref="TransactionIsolationLevel"/> can reject too-late changes).
    /// </summary>
    public static void DatabaseConnectionOpened() => Interlocked.Increment(ref _dbConnectionOpenCount);

    /// <summary>Marks a previously opened database connection as closed.</summary>
    public static void DatabaseConnectionClosed() => Interlocked.Decrement(ref _dbConnectionOpenCount);

    internal static System.Data.IsolationLevel? CurrentTransactionIsolationLevel => _transactionIsolationLevel;

    #endregion
}

