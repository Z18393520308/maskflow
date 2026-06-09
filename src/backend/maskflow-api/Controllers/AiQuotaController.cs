using Microsoft.AspNetCore.Mvc;

[Tags("AI Quota")]
public sealed class AiQuotaController : MaskFlowControllerBase
{
    public AiQuotaController(MaskFlowStore store) : base(store) { }

    [HttpGet("/api/ai/quota")]
    public IActionResult Quota()
    {
        var user = CurrentUser();
        return Ok(new { quota = Store.GetQuota(user) });
    }
}