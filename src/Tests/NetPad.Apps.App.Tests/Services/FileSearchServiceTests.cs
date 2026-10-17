using NetPad.Configuration;
using NetPad.Services;

namespace NetPad.Apps.App.Tests.Services;

public sealed class FileSearchServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SearchAsync_Finds_Literal_Code_Lines_And_Skips_Netpad_Metadata()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, "a.netpad"), "id\n{}\n#Code\nvar value = \"a.b\";\n#Code metadata");
        await File.WriteAllTextAsync(Path.Combine(_root, "b.cs"), "a.b\nother");
        await File.WriteAllTextAsync(Path.Combine(_root, "c.text"), "a.b");
        await File.WriteAllTextAsync(Path.Combine(_root, "d.text"), "aXb");
        var settings = new Settings().SetScriptLibraryRoots([_root]);

        var results = await new FileSearchService(settings).SearchAsync("a.b", CancellationToken.None);

        Assert.Equal(3, results.Count);
        Assert.DoesNotContain(results, r => r.Path.EndsWith("d.text", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Open_Save_And_Reload_Use_An_Opaque_Handle()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "open.cs");
        await File.WriteAllTextAsync(path, "original");
        var service = new FileSearchService(new Settings().SetScriptLibraryRoots([_root]));

        var opened = await service.OpenAsync(path, CancellationToken.None);
        Assert.Equal("original", opened.Text);
        Assert.Equal(Path.GetFullPath(path), opened.Path);
        await service.SaveAsync(opened.Handle, "updated", CancellationToken.None);
        Assert.Equal("updated", await service.ReloadAsync(opened.Handle, CancellationToken.None));
    }

    [Fact]
    public async Task Open_Rejects_Out_Of_Root_And_Unsupported_Files()
    {
        Directory.CreateDirectory(_root);
        var outside = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cs");
        var unsupported = Path.Combine(_root, "bad.bin");
        await File.WriteAllTextAsync(outside, "outside");
        await File.WriteAllTextAsync(unsupported, "unsupported");
        try
        {
            var service = new FileSearchService(new Settings().SetScriptLibraryRoots([_root]));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.OpenAsync(outside, CancellationToken.None));
            await Assert.ThrowsAsync<InvalidDataException>(() => service.OpenAsync(unsupported, CancellationToken.None));
        }
        finally { File.Delete(outside); }
    }

    [Fact]
    public async Task Search_Deduplicates_Files_Inside_Overlapping_Roots()
    {
        var nestedRoot = Path.Combine(_root, "nested");
        Directory.CreateDirectory(nestedRoot);
        var path = Path.Combine(nestedRoot, "match.cs");
        await File.WriteAllTextAsync(path, "needle");
        var settings = new Settings().SetScriptsDirectoryPath(_root).SetScriptLibraryRoots([nestedRoot]);

        var matches = await new FileSearchService(settings).SearchAsync("needle", CancellationToken.None);

        Assert.Single(matches);
        Assert.Equal(Path.GetFullPath(path), matches[0].Path);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
