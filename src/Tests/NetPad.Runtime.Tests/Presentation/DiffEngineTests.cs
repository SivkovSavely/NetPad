using NetPad.Presentation.Diff;

namespace NetPad.Runtime.Tests.Presentation;

public sealed class DiffEngineTests
{
    [Fact]
    public void Identical_Values_Produce_No_Differences()
    {
        var result = DiffEngine.Compare(new { A = 1 }, new { A = 1 });

        Assert.False(result.HasDifferences);
        Assert.Empty(result.TextDiffs);
    }

    [Fact]
    public void Strings_Compare_By_Lines()
    {
        var left = "line1\nline2\nline3";
        var right = "line1\nchanged\nline3\nline4";

        var result = DiffEngine.Compare(left, right);

        Assert.True(result.HasDifferences);
        Assert.Contains(result.TextDiffs, d => d.Kind == DiffKind.Removed && d.Text == "line2");
        Assert.Contains(result.TextDiffs, d => d.Kind == DiffKind.Added && d.Text == "changed");
        Assert.Contains(result.TextDiffs, d => d.Kind == DiffKind.Added && d.Text == "line4");
    }

    [Fact]
    public void Sequences_Report_Additions_Removals_And_Changes()
    {
        var result = DiffEngine.Compare(
            new[] { 1, 2, 3, 4 },
            new[] { 1, 9, 3 });

        Assert.Contains(result.Entries, e => e.Kind == DiffKind.Changed && e.Path == "[1]");
        Assert.Contains(result.Entries, e => e.Kind == DiffKind.Removed && e.Path == "[3]");
    }

    [Fact]
    public void Dictionaries_Compare_Keywise()
    {
        var left = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
        var right = new Dictionary<string, int> { ["a"] = 1, ["c"] = 3 };

        var result = DiffEngine.Compare(left, right);

        Assert.Contains(result.Entries, e => e.Kind == DiffKind.Removed && e.Path == "b");
        Assert.Contains(result.Entries, e => e.Kind == DiffKind.Added && e.Path == "c");
    }

    [Fact]
    public void Objects_Compare_Member_Structure()
    {
        var left = new Person("ann", 30);
        var right = new Person("bob", 30);

        var result = DiffEngine.Compare(left, right);

        Assert.Contains(result.Entries, e => e.Path == "Name" && Equals(e.Left, "ann") && Equals(e.Right, "bob"));
        Assert.DoesNotContain(result.Entries, e => e.Path == "Age");
    }

    [Fact]
    public void Mixed_Shape_Types_Report_A_Root_Change()
    {
        var result = DiffEngine.Compare("text", 42);

        Assert.True(result.HasDifferences);
        Assert.Contains(result.Entries, e => e.Kind == DiffKind.Changed);
    }

    private sealed record Person(string Name, int Age);
}
