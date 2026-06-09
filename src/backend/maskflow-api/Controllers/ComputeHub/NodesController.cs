using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

[Route("v1/nodes")]
[Tags("Compute Nodes")]
public sealed class NodesController : ControllerBase
{
    private readonly MaskFlowStore store;

    public NodesController(MaskFlowStore store)
    {
        this.store = store;
    }

    [HttpGet]
    public IActionResult List() => Ok(new { nodes = store.State.Nodes.Select(x => x.Public()).ToList() });

    [HttpGet("{nodeId}")]
    public IActionResult Detail(string nodeId)
    {
        var node = store.State.Nodes.FirstOrDefault(x => x.Id == nodeId);
        return node is null ? NotFound(new { detail = "Node not found" }) : Ok(new { node = node.Public(node.ApiKey) });
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] NodeRegister request)
    {
        var apiKey = "mf_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var node = new Node("node_" + Util.Id(), request.OwnerId, request.Pool, "pending", request.GpuModel, request.VramGb, request.Region, request.PricePerHour, 0, Util.Sha256(apiKey), DateTimeOffset.UtcNow, null, null);
        store.State.Nodes.Add(node);
        await store.SaveAsync();
        return Ok(new { node = node.Public(apiKey) });
    }

    [HttpPost("{nodeId}/heartbeat")]
    public async Task<IActionResult> Heartbeat(string nodeId, [FromBody] NodeHeartbeat request)
    {
        var result = await store.HeartbeatNodeAsync(nodeId, request);
        return new JsonResult(((Microsoft.AspNetCore.Http.HttpResults.JsonHttpResult<object>)result).Value) { StatusCode = 200 };
    }

    [HttpPost("{nodeId}/status")]
    public async Task<IActionResult> SetStatus(string nodeId, [FromBody] JsonElement body)
    {
        var status = body.TryGetProperty("status", out var s) ? s.GetString() ?? "offline" : "offline";
        var approve = body.TryGetProperty("approve", out var a) && a.GetBoolean();
        var result = await store.NodeStatusAsync(nodeId, status, approve);
        return new JsonResult(((Microsoft.AspNetCore.Http.HttpResults.JsonHttpResult<object>)result).Value) { StatusCode = 200 };
    }
}