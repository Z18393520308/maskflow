using Microsoft.AspNetCore.Mvc;

[Route("api/auth")]
[Tags("Auth")]
public sealed class AuthController : ControllerBase
{
    private readonly MaskFlowStore store;

    public AuthController(MaskFlowStore store)
    {
        this.store = store;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { detail = "Email and password are required." });
        }

        var result = await store.RegisterAsync(request.Email, request.Password, request.Username, HttpContext);
        return result is null ? Conflict(new { detail = "Email already exists." }) : Ok(result);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var result = await store.LoginAsync(request.Email, request.Password, HttpContext);
        return result is null ? Unauthorized() : Ok(result);
    }
}