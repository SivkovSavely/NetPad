using System.Collections;
using System.Linq;
using System.Reflection;

namespace NetPad.Presentation.Diff;

public enum DiffKind
{
    Added,
    Removed,
    Changed,
}

/// <summary>
/// One difference between two values. For structural diffs <see cref="Left"/>/<see cref="Right"/>
/// hold the compared member values; for text diffs they hold affected line content.
/// </summary>
public sealed record DiffEntry(DiffKind Kind, string Path, object? Left, object? Right);

/// <summary>
/// The structured result of comparing two values with <see cref="DiffEngine"/>.
/// </summary>
public sealed class DiffResult
{
    public IReadOnlyList<DiffEntry> Entries { get; init; } = [];

    public bool HasDifferences => Entries.Count > 0;

    /// <summary>
    /// Line-level differences produced when compared strings were encountered. Empty otherwise.
    /// </summary>
    public IReadOnlyList<TextLineDiff> TextDiffs { get; init; } = [];
}

public sealed record TextLineDiff(DiffKind Kind, int LineNumber, string Text);

/// <summary>
/// Produces meaningful structured differences for strings (line-oriented), sequences,
/// dictionaries, and object/member structures. Used by <c>Util.Dif</c>.
/// </summary>
public static class DiffEngine
{
    private const long LcsCellBudget = 1_000_000;
    private const int MaxDepth = 8;

    public static DiffResult Compare(object? left, object? right)
    {
        var entries = new List<DiffEntry>();
        var textDiffs = new List<TextLineDiff>();

        CompareCore(left, right, string.Empty, entries, textDiffs, depth: 0);

        return new DiffResult
        {
            Entries = entries,
            TextDiffs = textDiffs,
        };
    }

    private static void CompareCore(
        object? left,
        object? right,
        string path,
        List<DiffEntry> entries,
        List<TextLineDiff> textDiffs,
        int depth)
    {
        if (ReferenceEquals(left, right) || Equals(left, right))
        {
            return;
        }

        if (depth >= MaxDepth)
        {
            AddChanged(entries, path, left, right);
            return;
        }

        if (left is string || right is string)
        {
            if (left is string leftText && right is string rightText)
            {
                CompareText(leftText, rightText, path, entries, textDiffs);
                return;
            }

            AddChanged(entries, path, left, right);
            return;
        }

        if (left is IDictionary leftDict && right is IDictionary rightDict)
        {
            CompareDictionaries(leftDict, rightDict, path, entries, textDiffs, depth);
            return;
        }

        if (left is IEnumerable leftEnumerable && right is IEnumerable rightEnumerable
            && leftEnumerable is not string && rightEnumerable is not string
            && leftEnumerable is not IDictionary && rightEnumerable is not IDictionary)
        {
            CompareSequences(leftEnumerable, rightEnumerable, path, entries, textDiffs, depth);
            return;
        }

        if (IsStructure(left) && IsStructure(right))
        {
            CompareMembers(left!, right!, path, entries, textDiffs, depth);
            return;
        }

        AddChanged(entries, path, left, right);
    }

    private static void CompareText(
        string left,
        string right,
        string path,
        List<DiffEntry> entries,
        List<TextLineDiff> textDiffs)
    {
        foreach (var (kind, lineNumber, text) in DiffLines(SplitLines(left), SplitLines(right)))
        {
            textDiffs.Add(new TextLineDiff(kind, lineNumber, text));
            entries.Add(new DiffEntry(kind, path.Length == 0 ? "text" : path, kind == DiffKind.Added ? null : text, kind == DiffKind.Removed ? null : text));
        }
    }

    internal static IReadOnlyList<(DiffKind Kind, int LineNumber, string Text)> DiffLines(string[] leftLines, string[] rightLines)
    {
        // Bound LCS cost for very large texts with a positional fallback.
        if ((long)leftLines.Length * rightLines.Length > LcsCellBudget)
        {
            return PositionalLineDiff(leftLines, rightLines);
        }

        return LcsLineDiff(leftLines, rightLines);
    }

    private static IReadOnlyList<(DiffKind, int, string)> LcsLineDiff(string[] leftLines, string[] rightLines)
    {
        int n = leftLines.Length, m = rightLines.Length;
        var lcs = new int[n + 1, m + 1];

        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                lcs[i, j] = leftLines[i] == rightLines[j]
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var result = new List<(DiffKind, int, string)>();
        int li = 0, ri = 0;

        while (li < n && ri < m)
        {
            if (leftLines[li] == rightLines[ri])
            {
                li++;
                ri++;
            }
            else if (lcs[li + 1, ri] >= lcs[li, ri + 1])
            {
                result.Add((DiffKind.Removed, li + 1, leftLines[li]));
                li++;
            }
            else
            {
                result.Add((DiffKind.Added, ri + 1, rightLines[ri]));
                ri++;
            }
        }

        while (li < n)
        {
            result.Add((DiffKind.Removed, ++li, leftLines[li - 1]));
        }

        while (ri < m)
        {
            result.Add((DiffKind.Added, ++ri, rightLines[ri - 1]));
        }

        return result;
    }

    private static IReadOnlyList<(DiffKind, int, string)> PositionalLineDiff(string[] leftLines, string[] rightLines)
    {
        var result = new List<(DiffKind, int, string)>();
        var common = Math.Min(leftLines.Length, rightLines.Length);

        for (var i = 0; i < common; i++)
        {
            if (leftLines[i] != rightLines[i])
            {
                result.Add((DiffKind.Changed, i + 1, $"{leftLines[i]} => {rightLines[i]}"));
            }
        }

        for (var i = common; i < leftLines.Length; i++)
        {
            result.Add((DiffKind.Removed, i + 1, leftLines[i]));
        }

        for (var i = common; i < rightLines.Length; i++)
        {
            result.Add((DiffKind.Added, i + 1, rightLines[i]));
        }

        return result;
    }

    private static void CompareDictionaries(
        IDictionary left,
        IDictionary right,
        string path,
        List<DiffEntry> entries,
        List<TextLineDiff> textDiffs,
        int depth)
    {
        var leftMap = MapEntries(left);
        var rightMap = MapEntries(right);

        foreach (var key in leftMap.Keys.Union(rightMap.Keys).OrderBy(k => k))
        {
            var childPath = JoinPath(path, key);

            if (!leftMap.TryGetValue(key, out var leftEntry))
            {
                entries.Add(new DiffEntry(DiffKind.Added, childPath, null, rightMap[key].Value));
                continue;
            }

            if (!rightMap.TryGetValue(key, out var rightEntry))
            {
                entries.Add(new DiffEntry(DiffKind.Removed, childPath, leftEntry.Value, null));
                continue;
            }

            CompareCore(leftEntry.Value, rightEntry.Value, childPath, entries, textDiffs, depth + 1);
        }
    }

    private static Dictionary<string, (object? Key, object? Value)> MapEntries(IDictionary dictionary)
    {
        var map = new Dictionary<string, (object?, object?)>();

        foreach (var keyObj in dictionary.Keys)
        {
            var key = (object?)keyObj;
            var name = key?.ToString() ?? "(null)";
            map[name] = (key, dictionary[key!]);
        }

        return map;
    }

    private static void CompareSequences(
        IEnumerable left,
        IEnumerable right,
        string path,
        List<DiffEntry> entries,
        List<TextLineDiff> textDiffs,
        int depth)
    {
        var leftItems = left.Cast<object?>().ToArray();
        var rightItems = right.Cast<object?>().ToArray();

        // Align common prefix/suffix so middle insertions/removals read naturally.
        int start = 0;
        while (start < leftItems.Length && start < rightItems.Length && Equals(leftItems[start], rightItems[start]))
        {
            start++;
        }

        int endLeft = leftItems.Length - 1, endRight = rightItems.Length - 1;
        while (endLeft >= start && endRight >= start && Equals(leftItems[endLeft], rightItems[endRight]))
        {
            endLeft--;
            endRight--;
        }

        var changedCount = Math.Max(0, Math.Min(endLeft - start + 1, endRight - start + 1));
        for (var i = 0; i < changedCount; i++)
        {
            CompareCore(
                leftItems[start + i],
                rightItems[start + i],
                JoinPath(path, $"[{start + i}]"),
                entries,
                textDiffs,
                depth + 1);
        }

        for (var i = start + changedCount; i <= endLeft; i++)
        {
            entries.Add(new DiffEntry(DiffKind.Removed, JoinPath(path, $"[{i}]"), leftItems[i], null));
        }

        for (var i = start + changedCount; i <= endRight; i++)
        {
            entries.Add(new DiffEntry(DiffKind.Added, JoinPath(path, $"[{i}]"), null, rightItems[i]));
        }
    }

    private static void CompareMembers(
        object left,
        object right,
        string path,
        List<DiffEntry> entries,
        List<TextLineDiff> textDiffs,
        int depth)
    {
        var leftType = left.GetType();
        var rightType = right.GetType();

        foreach (var name in GetMemberNames(leftType).Union(GetMemberNames(rightType)).OrderBy(n => n))
        {
            var leftValue = TryGetMember(left, leftType, name);
            var rightValue = TryGetMember(right, rightType, name);

            if (Equals(leftValue, rightValue))
            {
                continue;
            }

            // Member-level scalars/strings report as a single change; only structures recurse.
            if ((leftValue is string || rightValue is string || leftValue is not null && !IsStructure(leftValue)) &&
                (rightValue is string || rightValue is not null && !IsStructure(rightValue)))
            {
                entries.Add(new DiffEntry(DiffKind.Changed, JoinPath(path, name), leftValue, rightValue));
                continue;
            }

            CompareCore(leftValue, rightValue, JoinPath(path, name), entries, textDiffs, depth + 1);
        }
    }

    private static HashSet<string> GetMemberNames(Type type)
    {
        var names = new HashSet<string>();

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.CanRead && property.GetIndexParameters().Length == 0)
            {
                names.Add(property.Name);
            }
        }

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            names.Add(field.Name);
        }

        return names;
    }

    private static object? TryGetMember(object obj, Type type, string name)
    {
        try
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property?.CanRead == true && property.GetIndexParameters().Length == 0)
            {
                return property.GetValue(obj);
            }

            return type.GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(obj);
        }
        catch
        {
            return "<error reading member>";
        }
    }

    private static bool IsSequence(object? value)
    {
        return value is not string && value is not IDictionary && value is IEnumerable;
    }

    private static bool IsStructure(object? value)
    {
        if (value == null) return false;

        var type = value.GetType();
        return value is not IEnumerable
               && !type.IsPrimitive
               && type != typeof(string)
               && !type.IsEnum;
    }

    private static void AddChanged(List<DiffEntry> entries, string path, object? left, object? right)
    {
        entries.Add(new DiffEntry(DiffKind.Changed, path.Length == 0 ? "value" : path, left, right));
    }

    private static string JoinPath(string parent, string child)
    {
        return parent.Length == 0 ? child : $"{parent}.{child}";
    }

    private static string[] SplitLines(string text)
    {
        return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    }
}
