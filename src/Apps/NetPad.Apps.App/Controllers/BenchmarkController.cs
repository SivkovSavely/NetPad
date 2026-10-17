using MediatR;
using Microsoft.AspNetCore.Mvc;
using NetPad.Apps.CQs;
using NetPad.Exceptions;
using NetPad.Scripts;
using NetPad.Services;

namespace NetPad.Controllers;

[ApiController]
[Route("code/{scriptId:guid}/benchmark")]
public sealed class BenchmarkController(IMediator mediator, BenchmarkService benchmarkService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<Guid>> Start(Guid scriptId, [FromBody] string selectedCode)
    {
        if (string.IsNullOrWhiteSpace(selectedCode))
        {
            return BadRequest("Selected code is required.");
        }

        var environment = await mediator.Send(new GetOpenedScriptEnvironmentQuery(scriptId, true))
                          ?? throw new ScriptNotFoundException(scriptId);
        if (environment.Script.Config.Kind == ScriptKind.SQL)
        {
            return BadRequest("Benchmark selection requires a C# script.");
        }

        return Ok(benchmarkService.Start(environment.Script, selectedCode));
    }

    [HttpDelete("{operationId:guid}")]
    public IActionResult Cancel(Guid scriptId, Guid operationId)
    {
        return benchmarkService.Cancel(scriptId, operationId) ? NoContent() : NotFound();
    }
}
