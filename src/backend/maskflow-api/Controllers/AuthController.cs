using MaskFlow.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Route("api/auth")]
[Tags("Auth")]
public sealed class AuthController : ControllerBase
{
    private readonly MaskFlowStore store;
    private readonly LoginRateLimiter loginRateLimiter;

    public AuthController(MaskFlowStore store, LoginRateLimiter loginRateLimiter)
    {
        this.store = store;
        this.loginRateLimiter = loginRateLimiter;
    }

    [AllowAnonymous]
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

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var rateKey = $"{HttpContext.Connection.RemoteIpAddress}:{request.Email.Trim().ToLowerInvariant()}";
        if (loginRateLimiter.IsLocked(rateKey, out var retryAfterSeconds))
        {
            Response.Headers.RetryAfter = retryAfterSeconds.ToString();
            return StatusCode(429, new { detail = $"Too many login attempts. Retry after {retryAfterSeconds} seconds." });
        }

        var result = await store.LoginAsync(request.Email, request.Password, HttpContext);
        if (result is null)
        {
            loginRateLimiter.RecordFailure(rateKey);
            return Unauthorized(new { detail = "Invalid email or password." });
        }

        loginRateLimiter.RecordSuccess(rateKey);
        return Ok(result);
    }
}