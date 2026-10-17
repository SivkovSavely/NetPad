using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetPad.Common;
using NetPad.Sessions.Workspaces;
using JsonSerializer = NetPad.Common.JsonSerializer;

namespace NetPad.Services;

public sealed class FileSystemWorkspaceStore(
    string filePath,
    ILogger<FileSystemWorkspaceStore> logger) : IWorkspaceStore
{
    private readonly string _filePath = filePath;
    private readonly string _backupPath = filePath + ".bak";
    private readonly string _temporaryPath = filePath + ".tmp";
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<WorkspaceDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (TryLoad(_filePath, out var document))
                return Normalize(document);

            if (TryLoad(_backupPath, out document))
            {
                logger.LogWarning("Recovered workspace state from backup file {Path}", _backupPath);
                return Normalize(document);
            }

            return WorkspaceDocument.CreateDefault();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(WorkspaceDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            document = Normalize(document);
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(
                _temporaryPath,
                JsonSerializer.Serialize(document, true),
                cancellationToken);

            // Preserve only a valid current document so a corrupt primary cannot replace
            // an older recoverable backup on the next save.
            if (TryLoad(_filePath, out _))
                File.Copy(_filePath, _backupPath, overwrite: true);

            File.Move(_temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(_temporaryPath))
                File.Delete(_temporaryPath);
            _gate.Release();
        }
    }

    private bool TryLoad(string path, out WorkspaceDocument document)
    {
        document = WorkspaceDocument.CreateDefault();
        if (!File.Exists(path))
            return false;

        try
        {
            var json = File.ReadAllText(path);
            document = JsonSerializer.Deserialize<WorkspaceDocument>(json, JsonSerializer.DefaultOptions)
                       ?? throw new InvalidDataException("Workspace document is empty");
            return true;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidDataException)
        {
            logger.LogWarning(ex, "Could not read workspace state file {Path}", path);
            return false;
        }
    }

    private static WorkspaceDocument Normalize(WorkspaceDocument document)
    {
        document.Version = WorkspaceDocument.CurrentVersion;
        document.Workspaces ??= [];
        document.Shelves ??= [];

        document.Workspaces = document.Workspaces
            .Where(workspace => workspace is not null && !string.IsNullOrWhiteSpace(workspace.Id))
            .ToList();

        if (document.Workspaces.Count == 0)
            document.Workspaces.Add(WorkspaceState.CreateDefault());

        foreach (var workspace in document.Workspaces)
        {
            workspace.Name = string.IsNullOrWhiteSpace(workspace.Name) ? "Workspace" : workspace.Name;
            workspace.Documents ??= [];
            workspace.Documents = workspace.Documents
                .Where(state => state is not null && !string.IsNullOrWhiteSpace(state.Id))
                .ToList();
            workspace.ActiveHostName = string.IsNullOrWhiteSpace(workspace.ActiveHostName)
                ? "main"
                : workspace.ActiveHostName;

            foreach (var state in workspace.Documents)
            {
                state.Order = Math.Clamp(state.Order, 0, 10_000);
                state.Bookmarks ??= [];
                state.NumberedBookmarks ??= [];
                state.UndoHistory ??= [];
                state.RedoHistory ??= [];
            }
        }

        if (!document.Workspaces.Any(workspace => workspace.Id == document.ActiveWorkspaceId))
            document.ActiveWorkspaceId = document.Workspaces[0].Id;

        document.Shelves = document.Shelves.Where(shelf => shelf is not null).ToList();
        foreach (var shelf in document.Shelves)
        {
            shelf.State ??= WorkspaceState.CreateDefault();
            shelf.State.Documents ??= [];
            shelf.State.Documents = shelf.State.Documents
                .Where(state => state is not null && !string.IsNullOrWhiteSpace(state.Id))
                .ToList();
            foreach (var state in shelf.State.Documents)
            {
                state.Order = Math.Clamp(state.Order, 0, 10_000);
                state.Bookmarks ??= [];
                state.NumberedBookmarks ??= [];
                state.UndoHistory ??= [];
                state.RedoHistory ??= [];
            }
        }

        return document;
    }
}
