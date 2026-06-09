using Microsoft.AspNetCore.Mvc;

[Route("api/export")]
[Tags("Export")]
public sealed class ExportController : MaskFlowControllerBase
{
    public ExportController(MaskFlowStore store) : base(store) { }

    [HttpPost("dataset")]
    public async Task<IActionResult> CreateDataset([FromBody] ExportRequest request)
    {
        var user = CurrentUser();
        var export = await Store.CreateDatasetExportAsync(user.Id, request);
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