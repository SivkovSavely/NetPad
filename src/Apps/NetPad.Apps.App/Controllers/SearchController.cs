using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NetPad.Services;

namespace NetPad.Controllers;

[ApiController]
[Route("search")]
public sealed class SearchController(FileSearchService searchService) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<FileSearchResult>> Search([FromQuery] string query, CancellationToken cancellationToken) =>
        searchService.SearchAsync(query, cancellationToken);

    [HttpGet("open")]
    public async Task<IActionResult> Open([FromQuery] string path, CancellationToken cancellationToken)
    {
        try { return Ok(await searchService.OpenAsync(path, cancellationToken)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidDataException) { return BadRequest(); }
        catch (DecoderFallbackException) { return BadRequest(); }
        catch (FileNotFoundException) { return NotFound(); }
        catch (DirectoryNotFoundException) { return NotFound(); }
    }

    [HttpGet("open/{handle}")]
    public async Task<IActionResult> Reload(string handle, CancellationToken cancellationToken)
    {
        try { return Ok(new { text = await searchService.ReloadAsync(handle, cancellationToken) }); }
        catch (FileNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidDataException) { return BadRequest(); }
        catch (DecoderFallbackException) { return BadRequest(); }
        catch (IOException) { return NotFound(); }
    }

    [HttpPut("open/{handle}")]
    public async Task<IActionResult> Save(string handle, [FromBody] SaveFileRequest request, CancellationToken cancellationToken)
    {
        try { await searchService.SaveAsync(handle, request.Text, cancellationToken); return NoContent(); }
        catch (FileNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (IOException) { return NotFound(); }
    }

    public sealed record SaveFileRequest(string Text);
}
