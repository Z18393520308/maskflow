using Microsoft.AspNetCore.Mvc;

[Route("api/billing")]
[Tags("Billing")]
public sealed class BillingController : MaskFlowControllerBase
{
    public BillingController(MaskFlowStore store) : base(store) { }

    [HttpGet("plans")]
    public IActionResult Plans() => Ok(new
    {
        plans = new[]
        {
            new { id = "free", name = "Free", price = 0, quotaBytes = 10L * 1024 * 1024 * 1024, dailyLimit = 50 },
            new { id = "pro", name = "Pro", price = 49, quotaBytes = 50L * 1024 * 1024 * 1024, dailyLimit = 1000 },
            new { id = "team", name = "Team", price = 299, quotaBytes = 500L * 1024 * 1024 * 1024, dailyLimit = 100000 }
        }
    });

    [HttpPost("subscribe")]
    public async Task<IActionResult> Subscribe([FromBody] SubscribeRequest request)
    {
        var user = CurrentUser();
        await Store.UpdatePlanAsync(user.Id, request.Plan);
        return Ok(new { user = Store.PublicUser(Store.GetUser(user.Id)!) });
    }
}