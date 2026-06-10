using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[AllowAnonymous]
[Tags("System")]
public sealed class SystemController : ControllerBase
{
    private readonly MaskFlowStore store;
    private readonly IConfiguration configuration;

    public SystemController(MaskFlowStore store, IConfiguration configuration)
    {
        this.store = store;
        this.configuration = configuration;
    }

    [HttpGet("/")]
    public IActionResult Root() => Ok(new { name = "MaskFlow API", docs = "/swagger" });

    [HttpGet("/api/status")]
    public IActionResult Status() => Ok(new
    {
        service = "maskflow-api-dotnet",
        storage = store.StorageRoot,
        samService = Environment.GetEnvironmentVariable("SAM_SERVICE_URL") ?? configuration["SamService:Url"] ?? "http://localhost:8001"
    });
}