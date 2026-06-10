using MaskFlow.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

public sealed class ProductionSecretsValidatorTests
{
    sealed class TestEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "MaskFlow.Api.Tests";
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    [Fact]
    public void EnsureConfigured_SkipsInDevelopment()
    {
        var env = new TestEnvironment { EnvironmentName = Environments.Development };
        ProductionSecretsValidator.EnsureConfigured(env);
    }

    [Fact]
    public void EnsureConfigured_ThrowsWhenProductionSecretsMissing()
    {
        var env = new TestEnvironment();
        var keys = new[] { "SAM3_INTERNAL_KEY", "MASKFLOW_ADMIN_API_KEY", "MASKFLOW_MYSQL" };
        var previous = keys.ToDictionary(key => key, key => Environment.GetEnvironmentVariable(key));
        try
        {
            foreach (var key in keys)
            {
                Environment.SetEnvironmentVariable(key, null);
            }

            Assert.Throws<InvalidOperationException>(() => ProductionSecretsValidator.EnsureConfigured(env));
        }
        finally
        {
            foreach (var (key, value) in previous)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    [Fact]
    public void EnsureConfigured_ThrowsWhenUsingDevDefaults()
    {
        var env = new TestEnvironment();
        var previous = new Dictionary<string, string?>
        {
            ["SAM3_INTERNAL_KEY"] = Environment.GetEnvironmentVariable("SAM3_INTERNAL_KEY"),
            ["MASKFLOW_ADMIN_API_KEY"] = Environment.GetEnvironmentVariable("MASKFLOW_ADMIN_API_KEY"),
            ["MASKFLOW_MYSQL"] = Environment.GetEnvironmentVariable("MASKFLOW_MYSQL")
        };
        try
        {
            Environment.SetEnvironmentVariable("SAM3_INTERNAL_KEY", "maskflow-sam-dev-key");
            Environment.SetEnvironmentVariable("MASKFLOW_ADMIN_API_KEY", "maskflow-admin-dev-key");
            Environment.SetEnvironmentVariable("MASKFLOW_MYSQL", "Server=127.0.0.1;Database=maskflow;");

            var ex = Assert.Throws<InvalidOperationException>(() => ProductionSecretsValidator.EnsureConfigured(env));
            Assert.Contains("development default", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            foreach (var (key, value) in previous)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}
