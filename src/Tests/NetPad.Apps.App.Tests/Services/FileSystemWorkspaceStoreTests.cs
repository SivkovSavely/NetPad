using Microsoft.Extensions.Logging.Abstractions;
using NetPad.Sessions.Workspaces;
using NetPad.Services;

namespace NetPad.Apps.App.Tests.Services;

public sealed class FileSystemWorkspaceStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "netpad-tests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "workspaces.json");

    [Fact]
    public async Task SaveAndLoad_RoundTripsVersionedWorkspaceState()
    {
        var store = CreateStore();
        var document = WorkspaceDocument.CreateDefault();
        document.Workspaces[0].Documents.Add(new WorkspaceDocumentState
        {
            Id = "file:/notes.txt",
            Kind = "file",
            Path = "/notes.txt",
            IsDirty = true,
            Buffer = "unsaved",
            Bookmarks = [2],
            UndoHistory = ["saved", "unsaved"],
        });

        await store.SaveAsync(document);
        var loaded = await store.LoadAsync();

        Assert.Equal(WorkspaceDocument.CurrentVersion, loaded.Version);
        Assert.Equal("file:/notes.txt", Assert.Single(loaded.Workspaces[0].Documents).Id);
        Assert.Equal("unsaved", loaded.Workspaces[0].Documents[0].Buffer);
        Assert.Equal([2], loaded.Workspaces[0].Documents[0].Bookmarks);
        Assert.Equal(["saved", "unsaved"], loaded.Workspaces[0].Documents[0].UndoHistory);
    }

    [Fact]
    public async Task LoadAsync_UsesBackupWhenPrimaryIsTruncated()
    {
        var store = CreateStore();
        var previous = WorkspaceDocument.CreateDefault();
        previous.Workspaces[0].Name = "previous";
        await store.SaveAsync(previous);

        var current = WorkspaceDocument.CreateDefault();
        current.Workspaces[0].Name = "current";
        await store.SaveAsync(current);
        await File.WriteAllTextAsync(FilePath, "{\"version\":");

        var loaded = await store.LoadAsync();

        Assert.Equal("previous", loaded.Workspaces[0].Name);
    }

    [Fact]
    public async Task SaveAsync_DoesNotReplaceValidBackupWithCorruptPrimary()
    {
        var store = CreateStore();
        var previous = WorkspaceDocument.CreateDefault();
        previous.Workspaces[0].Name = "previous";
        await store.SaveAsync(previous);

        var current = WorkspaceDocument.CreateDefault();
        current.Workspaces[0].Name = "current";
        await store.SaveAsync(current);
        await File.WriteAllTextAsync(FilePath, "truncated");

        var replacement = WorkspaceDocument.CreateDefault();
        replacement.Workspaces[0].Name = "replacement";
        await store.SaveAsync(replacement);

        var backupStore = CreateStore();
        await File.WriteAllTextAsync(FilePath, "truncated");
        var loaded = await backupStore.LoadAsync();
        Assert.Equal("previous", loaded.Workspaces[0].Name);
    }

    [Fact]
    public async Task LoadAsync_UsesDefaultsForMissingFieldsAndIgnoresUnknownFields()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(FilePath, "{\"unknown\":true}");

        var loaded = await CreateStore().LoadAsync();

        Assert.Equal(WorkspaceDocument.CurrentVersion, loaded.Version);
        Assert.Equal(WorkspaceDocument.DefaultWorkspaceId, loaded.Workspaces[0].Id);
    }

    private FileSystemWorkspaceStore CreateStore() =>
        new(FilePath, NullLogger<FileSystemWorkspaceStore>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
