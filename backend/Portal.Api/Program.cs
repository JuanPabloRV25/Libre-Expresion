using Portal.Infrastructure;
using Portal.Infrastructure.Persistence.Seeding;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

if (DatabaseSeeding.IsEnabled(builder.Configuration))
{
    await app.Services.SeedDatabaseAsync(builder.Configuration);
}

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }))
    .WithName("Health");

app.Run();

public partial class Program
{
}
