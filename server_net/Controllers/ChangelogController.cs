using Danmu.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace Danmu.Server.Controllers;

[ApiController]
[Route("api")]
public class ChangelogController : ControllerBase
{
    private readonly ChangelogService _changelogService;

    public ChangelogController(ChangelogService changelogService)
    {
        _changelogService = changelogService;
    }

    [HttpGet("changelog")]
    public async Task<IActionResult> GetChangelog()
    {
        var entries = await _changelogService.GetAllAsync();
        return Ok(entries);
    }
}