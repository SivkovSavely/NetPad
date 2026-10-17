using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using NetPad.Presentation;
using NetPad.Presentation.Controls;
using NetPad.Presentation.Html;
using NetPad.Scripts.Events;

namespace NetPad.Services;

internal sealed record BenchmarkResult(string Name, double MeanNanoseconds, double ErrorNanoseconds,
    double StandardDeviationNanoseconds, double? AllocatedBytes);

internal static class BenchmarkReportParser
{
    internal static IReadOnlyList<BenchmarkResult> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("Benchmarks", out var benchmarks)
            || benchmarks.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("The BenchmarkDotNet JSON report has no Benchmarks array.");
        }

        return benchmarks.EnumerateArray().Select(benchmark =>
        {
            var statistics = benchmark.GetProperty("Statistics");
            var error = statistics.GetProperty("ConfidenceInterval").GetProperty("Margin").GetDouble();
            var name = GetString(benchmark, "FullName")
                       ?? GetString(benchmark, "DisplayInfo")
                       ?? GetString(benchmark, "Method")
                       ?? "Benchmark";

            double? allocated = null;
            if (benchmark.TryGetProperty("Memory", out var memory)
                && memory.TryGetProperty("BytesAllocatedPerOperation", out var bytes)
                && bytes.ValueKind == JsonValueKind.Number)
            {
                allocated = bytes.GetDouble();
            }

            return new BenchmarkResult(
                name,
                statistics.GetProperty("Mean").GetDouble(),
                error,
                statistics.GetProperty("StandardDeviation").GetDouble(),
                allocated);
        }).ToArray();
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}

internal static class BenchmarkOutputFactory
{
    internal static ScriptOutput Create(Guid operationId, string html, bool isUpdate)
    {
        return new ScriptOutput(ScriptOutputKind.Result, 0, html, ScriptOutputFormat.Html)
        {
            OutputId = $"benchmark-{operationId:N}",
            IsUpdate = isUpdate
        };
    }
}

internal static class BenchmarkHtml
{
    internal static string Progress(IReadOnlyCollection<string> lines)
    {
        return $"<div class=\"benchmark-progress\"><strong>Benchmark running</strong><pre>{WebUtility.HtmlEncode(string.Join(Environment.NewLine, lines))}</pre></div>";
    }

    internal static string Failure(string message, IReadOnlyCollection<string> lines)
    {
        return $"<div class=\"benchmark-error\"><strong>{WebUtility.HtmlEncode(message)}</strong><pre>{WebUtility.HtmlEncode(string.Join(Environment.NewLine, lines))}</pre></div>";
    }

    internal static string Completed(IReadOnlyList<BenchmarkResult> results)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<div class=\"benchmark-results\"><table>");
        builder.AppendLine("<thead><tr><th>Benchmark</th><th>Mean (ns)</th><th>Error (ns)</th><th>StdDev (ns)</th><th>Allocated (B)</th></tr></thead><tbody>");

        foreach (var result in results)
        {
            builder.Append("<tr><td>").Append(WebUtility.HtmlEncode(result.Name)).Append("</td><td>")
                .Append(Format(result.MeanNanoseconds)).Append("</td><td>")
                .Append(Format(result.ErrorNanoseconds)).Append("</td><td>")
                .Append(Format(result.StandardDeviationNanoseconds)).Append("</td><td>")
                .Append(result.AllocatedBytes is { } bytes ? Format(bytes) : "N/A")
                .AppendLine("</td></tr>");
        }

        builder.AppendLine("</tbody></table>");
        var chart = new ChartControl(
            results.Select(result => new ChartDataPoint(result.Name, result.MeanNanoseconds)),
            ChartType.Column)
        {
            Title = "Mean (ns)"
        };
        builder.AppendLine(HtmlPresenter.Serialize(chart));
        builder.AppendLine("</div>");
        return builder.ToString();
    }

    private static string Format(double value) => value.ToString("N2", CultureInfo.InvariantCulture);
}
