using Microsoft.AspNetCore.Mvc;

[Route("api/export")]
[Tags("Export")]
public sealed class ExportController : MaskFlowControllerBase
{
    public ExportController(MaskFlowStore store) : base(store) { }

    [HttpGet]
    public IActionResult List([FromQuery] string? projectId)
    {
        var user = CurrentUser();
        var exports = Store.State.Exports
            .Where(x => x.UserId == user.Id && (string.IsNullOrWhiteSpace(projectId) || x.ProjectId == projectId))
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id,
                x.ProjectId,
                ProjectName = Store.State.Projects.FirstOrDefault(p => p.Id == x.ProjectId)?.Name,
                x.Status,
                x.Size,
                x.CreatedAt,
                x.FinishedAt,
                x.DownloadUrl,
                x.ErrorMessage,
                Split = x.Config.Split,
                Format = x.Config.Format
            });
        return Ok(new { exports });
    }

    [HttpPost("dataset")]
    public async Task<IActionResult> CreateDataset([FromBody] ExportRequest request)
    {
        var user = CurrentUser();
        var split = request.Split ?? new SplitConfig(70, 20, 10);
        if (split.Train + split.Val + split.Test != 100)
        {
            return BadRequest(new { detail = "Split ratios must sum to 100." });
        }

        var export = await Store.CreateDatasetExportAsync(user.Id, request with { Split = split });
        return Ok(new { export });
    }

    [HttpGet("{exportId}")]
    public IActionResult Detail(string exportId)
    {
        var user = CurrentUser();
        var export = Store.State.Exports.FirstOrDefault(x => x.Id == exportId && x.UserId == user.Id);
        return export is null ? NotFound(new { detail = "Export not found." }) : Ok(new { export });
    }

    [HttpGet("{exportId}/download")]
    public async Task<IActionResult> Download(string exportId)
    {
        var user = CurrentUser();
        var export = Store.State.Exports.FirstOrDefault(x => x.Id == exportId && x.UserId == user.Id);
        if (export is null || export.Path is null || !await Store.StoredObjectExistsAsync(export.Path)) return NotFound(new { detail = "Export file not found." });
        return File(await Store.OpenStoredObjectAsync(export.Path), "application/zip", $"{exportId}.zip");
    }
}