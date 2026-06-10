namespace MaskFlow.Api.Infrastructure.Security;

public static class ProductionSecretsValidator
{
    static readonly HashSet<string> DeniedValues = new(StringComparer.Ordinal)
    {
        "maskflow-sam-dev-key",
        "maskflow-admin-dev-key"
    };

    public static void EnsureConfigured(IWebHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            return;
        }

        RequireSecret("SAM3_INTERNAL_KEY", minLength: 16);
        RequireSecret("MASKFLOW_ADMIN_API_KEY", minLength: 16);
        RequireSecret("MASKFLOW_MYSQL", minLength: 8);
    }

    static void RequireSecret(string name, int minLength)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{name} must be set in Production.");
        }

        if (value.Length < minLength)
        {
            throw new InvalidOperationException($"{name} must be at least {minLength} characters in Production.");
        }

        if (DeniedValues.Contains(value))
        {
            throw new InvalidOperationException($"{name} is using a known development default. Set a unique secret before deploying.");
        }
    }
}
