using Microsoft.AspNetCore.Http.Features;
using Microsoft.OpenApi.Models;
using MaskFlow.Api.Application;
using MaskFlow.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 512L * 1024L * 1024L;
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

builder.Services.AddHttpClient("sam", client =>
{
    client.Timeout = TimeSpan.FromMinutes(30);
    client.BaseAddress = new Uri(Environment.GetEnvironmentVariable("SAM_SERVICE_URL")
        ?? builder.Configuration["SamService:Url"]
        ?? "http://localhost:8001");
});

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 512L * 1024L * 1024L;
    options.ValueCountLimit = 4096;
    options.MultipartHeadersCountLimit = 64;
});

builder.Services.AddMaskFlowApplication();
builder.Services.AddMaskFlowInfrastructure();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "MaskFlow API",
        Version = "v1",
        Description = "MaskFlow business API and SAM inference proxy endpoints."
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Enter the JWT/session token as: Bearer {token}",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

app.UseCors();
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "MaskFlow API v1");
    options.DocumentTitle = "MaskFlow API Debug";
});

var store = app.Services.GetRequiredService<MaskFlowStore>();
store.Initialize();

app.MapControllers();

app.Run();
