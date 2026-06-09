using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

[Route("v1/jobs")]
[Tags("Compute Jobs")]
public sealed class JobsController : ControllerBase
{
    private readonly MaskFlowStore store;

    public JobsController(MaskFlowStore store)
    {
        this.store = store;
    }

    [HttpGet]
    public IActionResult List() => Ok(new { jobs = store.State.Jobs });

    [HttpGet("{jobId}")]
    public IActionResult Detail(string jobId)
    {
        var job = store.State.Jobs.FirstOrDefault(x => x.Id == jobId);
        return job is null ? NotFound(new { detail = "Job not found" }) : Ok(new { job });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] JobCreate request)
    {
        var job = new Job("job_" + Util.Id(), "maskflow", request.Type, request.UserId, request.ProjectId, "platform-gpu", "normal", "queued", new Dictionary<string, object?> { ["gpu"] = 1 }, request.Input, null, request.Params, null, null, null, null, DateTimeOffset.UtcNow, null, null);
        store.State.Jobs.Add(job);
        await store.SaveAsync();
        return Ok(new { job });
    }

    [HttpPost("{jobId}/status")]
    public async Task<IActionResult> SetStatus(string jobId, [FromBody] JsonElement body)
    {
        var status = body.TryGetProperty("status", out var s) ? s.GetString() ?? "queued" : "queued";
        var result = await store.SetJobStatusAsync(jobId, status);
        return new JsonResult(((Microsoft.AspNetCore.Http.HttpResults.JsonHttpResult<object>)result).Value) { StatusCode = 200 };
    }

    [HttpPost("{jobId}/events")]
    public async Task<IActionResult> AddEvent(string jobId, [FromBody] JobEventCreate request)
    {
        var result = await store.AddJobEventAsync(jobId, request);
        return result is null ? NotFound(new { detail = "Job not found" }) : Ok(result);
    }
}