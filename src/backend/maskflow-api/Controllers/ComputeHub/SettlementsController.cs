using Microsoft.AspNetCore.Mvc;

[Route("v1/settlements")]
[Tags("Settlements")]
public sealed class SettlementsController : ControllerBase
{
    private readonly MaskFlowStore store;

    public SettlementsController(MaskFlowStore store)
    {
        this.store = store;
    }

    [HttpGet]
    public IActionResult List() => Ok(new { settlements = store.State.Settlements });

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SettlementCreate request)
    {
        var settlement = new Settlement("settle_" + Util.Id(), request.ProviderId, request.Period, request.NodeCount, request.GrossAmount, request.PlatformFee, request.GrossAmount - request.PlatformFee, request.Status, DateTimeOffset.UtcNow, null);
        store.State.Settlements.Add(settlement);
        await store.SaveAsync();
        return Ok(new { settlement });
    }
}