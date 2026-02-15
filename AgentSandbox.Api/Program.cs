using AgentSandbox.Api.Endpoints;
using AgentSandbox.Core;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() 
    { 
        Title = "Agent Sandbox API", 
        Version = "v1",
        Description = "In-memory agent sandbox with virtual filesystem and CLI"
    });
});

builder.Services.AddSingleton<ISnapshotStore, InMemorySnapshotStore>();
builder.Services.AddSingleton<SandboxManager>(sp => new SandboxManager(
    defaultOptions: null,
    managerOptions: new SandboxManagerOptions
    {
        SnapshotStore = sp.GetRequiredService<ISnapshotStore>()
    }));

// Add CORS for development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

// Map sandbox endpoints
app.MapSandboxEndpoints();

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .WithName("HealthCheck")
    .WithOpenApi();

app.Run();
