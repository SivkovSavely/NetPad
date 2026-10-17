using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis;
using NetPad.CodeAnalysis;
using NetPad.Compilation;
using NetPad.Compilation.Scripts;
using NetPad.Exceptions;
using NetPad.Sessions;
using NetPad.Services;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;
using JsonSerializer = NetPad.Common.JsonSerializer;

namespace NetPad.Controllers;

[ApiController]
[Route("code")]
public class CodeController : ControllerBase
{
    private static readonly JsonSerializerOptions _jsonSerializerOptions = JsonSerializer.Configure(
        new JsonSerializerOptions
        {
            MaxDepth = 100
        });

    [HttpGet("{scriptId:guid}/syntax-tree")]
    public ActionResult<SyntaxNodeOrTokenSlim?> GetSyntaxTree(
        Guid scriptId,
        [FromServices] ISession session,
        [FromServices] ICodeAnalysisService codeAnalysisService)
    {
        var env = session.Get(scriptId);

        if (env == null)
        {
            throw new ScriptNotFoundException(scriptId);
        }

        var script = env.Script;

        if (string.IsNullOrWhiteSpace(script.Code))
        {
            return Ok(null);
        }

        var tree = codeAnalysisService.GetSyntaxTreeSlim(
            script.Code,
            script.Config.TargetFrameworkVersion,
            script.Config.OptimizationLevel,
            HttpContext.RequestAborted
        );

        return new JsonResult(tree, _jsonSerializerOptions);
    }

    [HttpGet("{scriptId:guid}/il")]
    [ProducesResponseType(typeof(string), 200)]
    public async Task<ActionResult<string?>> GetIntermediateLanguage(
        Guid scriptId,
        [FromQuery] bool includeAssemblyHeader,
        [FromServices] ISession session,
        [FromServices] ICodeAnalysisService codeAnalysisService,
        [FromServices] IScriptCompiler scriptCompiler)
    {
        var cancellationToken = HttpContext.RequestAborted;
        var env = session.Get(scriptId);

        if (env == null)
        {
            throw new ScriptNotFoundException(scriptId);
        }

        var script = env.Script;

        if (string.IsNullOrWhiteSpace(script.Code))
        {
            return Ok(null);
        }

        var pcResult = await scriptCompiler.ParseAndCompileAsync(script.Code, script, cancellationToken);
        if (pcResult == null)
        {
            return Problem("Script compilation failed");
        }

        var (parsingResult, compilationResult) = pcResult;
        if (!compilationResult.Success)
        {
            var errors = compilationResult
                .Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => DiagnosticsHelper.ReduceStacktraceLineNumbers(d, parsingResult.UserProgramStartLineNumber));

            return Problem(string.Join("\n", errors));
        }

        var il = codeAnalysisService.GetIntermediateLanguage(
            compilationResult.AssemblyBytes,
            includeAssemblyHeader,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(script.Code))
        {
            return Ok(il);
        }

        return il;
    }

    [HttpGet("{scriptId:guid}/disassembly")]
    [ProducesResponseType(typeof(CodePaneContent), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CodePaneContent), StatusCodes.Status501NotImplemented)]
    public async Task<ActionResult<CodePaneContent>> GetNativeDisassembly(
        Guid scriptId,
        [FromServices] ISession session,
        [FromServices] NativeDisassemblyService disassemblyService)
    {
        var env = session.Get(scriptId);
        if (env == null)
        {
            throw new ScriptNotFoundException(scriptId);
        }

        if (string.IsNullOrWhiteSpace(env.Script.Code))
        {
            return Ok(new CodePaneContent("asm", string.Empty));
        }

        var result = await disassemblyService.DisassembleAsync(env.Script, HttpContext.RequestAborted);
        if (!result.Supported)
        {
            return StatusCode(StatusCodes.Status501NotImplemented,
                new CodePaneContent("asm", string.Empty, result.Error, Supported: false));
        }

        if (result.Error != null)
        {
            return StatusCode(result.StatusCode,
                new CodePaneContent("asm", string.Empty, result.Error));
        }

        return Ok(new CodePaneContent("asm", result.Content ?? string.Empty));
    }

    [HttpPost("{scriptId:guid}/decompile")]
    [ProducesResponseType(typeof(CodeDecompilationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<CodeDecompilationResponse>> DecompileAtPosition(
        Guid scriptId,
        [FromBody] CodeDecompilationRequest request,
        [FromServices] ISession session,
        [FromServices] CodeDecompilationService decompilationService)
    {
        if (request.Line < 1 || request.Column < 1)
        {
            return BadRequest("Line and column must be 1-based positive values.");
        }

        var env = session.Get(scriptId);
        if (env == null)
        {
            throw new ScriptNotFoundException(scriptId);
        }

        var result = await decompilationService.DecompileAtPositionAsync(
            env.Script,
            request,
            HttpContext.RequestAborted);
        return result == null ? NoContent() : Ok(result);
    }
}
