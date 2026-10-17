using System.Text.Json;

namespace NetPad.Sessions.Workspaces;

public sealed class WorkspaceDocument
{
    public const int CurrentVersion = 1;
    public const string DefaultWorkspaceId = "default";

    public int Version { get; set; } = CurrentVersion;
    public bool IsInitialized { get; set; }
    public string ActiveWorkspaceId { get; set; } = DefaultWorkspaceId;
    public List<WorkspaceState> Workspaces { get; set; } = [WorkspaceState.CreateDefault()];
    public List<WorkspaceShelf> Shelves { get; set; } = [];

    public static WorkspaceDocument CreateDefault() => new();
}

public sealed class WorkspaceState
{
    public string Id { get; set; } = WorkspaceDocument.DefaultWorkspaceId;
    public string Name { get; set; } = "Default";
    public List<WorkspaceDocumentState> Documents { get; set; } = [];
    public string? ActiveDocumentId { get; set; }
    public string ActiveHostName { get; set; } = "main";

    public static WorkspaceState CreateDefault() => new();
}

public sealed class WorkspaceDocumentState
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "script";
    public string? Name { get; set; }
    public string? Path { get; set; }
    public string? Source { get; set; }
    public string HostName { get; set; } = "main";
    public int Order { get; set; }
    public bool IsDirty { get; set; }
    public string? Buffer { get; set; }
    public string? DiskFingerprint { get; set; }
    public DateTime? DiskModifiedUtc { get; set; }
    public JsonElement? ViewState { get; set; }
    public List<int> Bookmarks { get; set; } = [];
    public Dictionary<int, int> NumberedBookmarks { get; set; } = [];
    public List<string> UndoHistory { get; set; } = [];
    public List<string> RedoHistory { get; set; } = [];
}

public sealed class WorkspaceShelf
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public bool IsAutomatic { get; set; }
    public WorkspaceState State { get; set; } = new();
}
