using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

[Tags("Annotation")]
public sealed class AnnotationController : ControllerBase
{
    private readonly MaskFlowStore store;
    private readonly IHttpClientFactory clientFactory;

    public AnnotationController(MaskFlowStore store, IHttpClientFactory clientFactory)
    {
        this.store = store;
        this.clientFactory = clientFactory;
    }

    [HttpPost("/api/annotations/auto")]
    public async Task<IActionResult> Auto([FromBody] AnnotationAutoRequest request)
    {
        var user = store.RequireUser(HttpContext);
        try
        {
            store.EnsureAiQuotaAvailable(user);
        }
        catch (BadHttpRequestException ex)
        {
            return StatusCode(ex.StatusCode, new { detail = ex.Message });
        }

        var file = store.State.Files.FirstOrDefault(x => x.Id == request.FileId && x.UserId == user.Id);
        if (file is null || !await store.StoredObjectExistsAsync(file.Path)) return NotFound(new { detail = "File not found." });

        using var content = new MultipartFormDataContent();
        await using var stream = await store.OpenStoredObjectAsync(file.Path);
        var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(file.ContentType ?? "application/octet-stream");
        content.Add(fileContent, "image", file.Name);
        content.Add(new StringContent(request.Conf.ToString(CultureInfo.InvariantCulture)), "conf");

        try
        {
            var client = clientFactory.CreateClient("sam");
            var response = await client.PostAsync("/api/annotation/masks", content, HttpContext.RequestAborted);
            var body = await response.Content.ReadAsStringAsync(HttpContext.RequestAborted);
            if (!response.IsSuccessStatusCode)
            {
                return new ContentResult
                {
                    Content = body,
                    ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json",
                    StatusCode = (int)response.StatusCode
                };
            }

            var set = BuildAnnotationSetFromSam(user.Id, file.Id, body);
            var task = store.CreateTask(user.Id, "yolo_auto_annotation", $"自动标注 {file.Name}", file.ProjectId, file.Id, 1);
            store.State.Tasks.Remove(task);
            store.State.Tasks.Add(task with
            {
                Status = "completed",
                Progress = 1,
                Result = new Dictionary<string, object?> { ["fileId"] = file.Id, ["annotationCount"] = set.Annotations.Count },
                FinishedAt = DateTimeOffset.UtcNow
            });
            await store.ConsumeAiQuotaAsync(user.Id, 1);
            await store.SaveAsync();
            return Ok(new { annotation = set, user = store.PublicUser(store.GetUser(user.Id)!) });
        }
        catch (BadHttpRequestException ex)
        {
            return StatusCode(ex.StatusCode, new { detail = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(503, new { detail = $"SAM inference service is unavailable: {ex.Message}" });
        }
        catch (TaskCanceledException)
        {
            return StatusCode(504, new { detail = "SAM inference service request timed out." });
        }
    }

    [HttpGet("/api/annotations/file/{fileId:int}")]
    public IActionResult GetByFile(int fileId)
    {
        var user = store.RequireUser(HttpContext);
        var set = store.GetAnnotationSet(user.Id, fileId);
        return set is null ? NotFound(new { detail = "Annotation not found." }) : Ok(new { annotation = set });
    }

    [HttpPut("/api/annotations/file/{fileId:int}")]
    public async Task<IActionResult> SaveByFile(int fileId, [FromBody] AnnotationSaveRequest request)
    {
        var user = store.RequireUser(HttpContext);
        var set = store.SaveAnnotationSet(user.Id, request with { FileId = fileId });
        await store.SaveAsync();
        return Ok(new { annotation = set });
    }

    [HttpDelete("/api/annotations/file/{fileId:int}/items/{annotationId}")]
    public async Task<IActionResult> DeleteItem(int fileId, string annotationId)
    {
        var user = store.RequireUser(HttpContext);
        var set = store.GetAnnotationSet(user.Id, fileId);
        if (set is null) return NotFound(new { detail = "Annotation not found." });
        var next = store.SaveAnnotationSet(user.Id, new AnnotationSaveRequest(fileId, set.Width, set.Height, set.Annotations.Where(x => x.Id != annotationId).ToList()));
        await store.SaveAsync();
        return Ok(new { annotation = next });
    }

    [HttpPost("/api/annotation/masks")]
    public async Task<IActionResult> Masks()
    {
        var user = store.OptionalUser(HttpContext);
        if (!Request.HasFormContentType) return BadRequest(new { detail = "Multipart form data is required." });
        var form = await Request.ReadFormAsync();
        if (form.Files.Count == 0) return BadRequest(new { detail = "Image file is required." });

        using var content = new MultipartFormDataContent();
        foreach (var field in form)
        {
            foreach (var value in field.Value)
            {
                content.Add(new StringContent(value ?? ""), field.Key);
            }
        }

        foreach (var file in form.Files)
        {
            var stream = file.OpenReadStream();
            var fileContent = new StreamContent(stream);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(file.ContentType ?? "application/octet-stream");
            content.Add(fileContent, file.Name, file.FileName);
        }

        try
        {
            var client = clientFactory.CreateClient("sam");
            var response = await client.PostAsync("/api/annotation/masks", content, HttpContext.RequestAborted);
            var body = await response.Content.ReadAsStringAsync(HttpContext.RequestAborted);
            if (response.IsSuccessStatusCode && user is not null) await store.ConsumeAiQuotaAsync(user.Id, 1);
            return new ContentResult
            {
                Content = body,
                ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json",
                StatusCode = (int)response.StatusCode
            };
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(503, new { detail = $"SAM inference service is unavailable: {ex.Message}" });
        }
        catch (TaskCanceledException)
        {
            return StatusCode(504, new { detail = "SAM inference service request timed out." });
        }
    }

    private AnnotationSet BuildAnnotationSetFromSam(int userId, int fileId, string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var width = root.TryGetProperty("width", out var widthElement) ? widthElement.GetInt32() : 0;
        var height = root.TryGetProperty("height", out var heightElement) ? heightElement.GetInt32() : 0;
        var items = new List<AnnotationItem>();

        if (root.TryGetProperty("masks", out var masks) && masks.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var mask in masks.EnumerateArray())
            {
                if (!mask.TryGetProperty("yoloBox", out var box)) continue;
                var segment = new List<double>();
                if (mask.TryGetProperty("yoloSegments", out var segments) && segments.ValueKind == JsonValueKind.Array)
                {
                    var first = segments.EnumerateArray().FirstOrDefault();
                    if (first.ValueKind == JsonValueKind.Array)
                    {
                        segment = first.EnumerateArray().Select(x => x.GetDouble()).ToList();
                    }
                }

                items.Add(new AnnotationItem(
                    mask.TryGetProperty("id", out var id) ? id.GetString() ?? $"ann_{index}" : $"ann_{index}",
                    0,
                    "object",
                    new YoloBox(
                        box.TryGetProperty("cx", out var cx) ? cx.GetDouble() : 0,
                        box.TryGetProperty("cy", out var cy) ? cy.GetDouble() : 0,
                        box.TryGetProperty("width", out var bw) ? bw.GetDouble() : 0,
                        box.TryGetProperty("height", out var bh) ? bh.GetDouble() : 0),
                    segment.Count > 0 ? segment : null,
                    1.0));
                index++;
            }
        }

        return store.SaveAnnotationSet(userId, new AnnotationSaveRequest(fileId, width, height, items));
    }
}