namespace NetPad.Sessions.Workspaces;

public interface IWorkspaceStore
{
    Task<WorkspaceDocument> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(WorkspaceDocument document, CancellationToken cancellationToken = default);
}
