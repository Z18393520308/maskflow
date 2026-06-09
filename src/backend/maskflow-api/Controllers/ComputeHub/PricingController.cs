using Microsoft.AspNetCore.Mvc;

[Tags("Pricing")]
public sealed class PricingController : ControllerBase
{
    private readonly MaskFlowStore store;

    public PricingController(MaskFlowStore store)
    {
        this.store = store;
    }

    [HttpGet("/v1/pricing")]
    public IActionResult List() => Ok(new { rules = store.State.PricingRules });

    [HttpPost("/v1/pricing")]
    public async Task<IActionResult> Create([FromBody] PricingCreate request)
    {
        var rule = new PricingRule("price_" + Util.Id(), request.Name, request.ResourceType, request.Pool, request.Region, request.UnitPrice, request.BillingUnit, request.Status, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        store.State.PricingRules.Add(rule);
        await store.SaveAsync();
        return Ok(new { rule });
    }
}