using Microsoft.AspNetCore.Mvc;
using NetPad.Sessions.Workspaces;

namespace NetPad.Controllers;

[ApiController]
[Route("workspaces")]
public sealed class WorkspacesController(IWorkspaceStore workspaceStore) : ControllerBase
{
    [HttpGet]
    public Task<WorkspaceDocument> Get(CancellationToken cancellationToken) =>
        workspaceStore.LoadAsync(cancellationToken);

    [HttpPut]
    public async Task<IActionResult> Save(
        [FromBody] WorkspaceDocument document,
        CancellationToken cancellationToken)
    {
        await workspaceStore.SaveAsync(document, cancellationToken);
        return NoContent();
    }
}
