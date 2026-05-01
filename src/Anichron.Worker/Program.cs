using Anichron.Worker;
using Anichron.Core.Data;
using Microsoft.EntityFrameworkCore;
using Anichron.Infrastructure.Configuration;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json")
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddJsonFile("appsettings.local.json", optional: true)
    .AddEnvironmentVariables();

// Validate required configuration before starting
var workerUser = builder.Configuration["Worker:User"]
    ?? throw new InvalidOperationException(
        "WORKER__USER is required. Set it to the email or username of the user this Worker belongs to.");

if (string.IsNullOrWhiteSpace(workerUser))
    throw new InvalidOperationException("WORKER__USER must not be empty.");

builder.Services.Configure<WorkerSettings>(builder.Configuration.GetSection("Worker"));
builder.Services.AddHostedService<Worker>();

var databaseConnection = DatabaseConfiguration.GetConnectionString(builder.Configuration);
builder.Services.AddDbContext<AnichronDbContext>(options =>
    options.UseNpgsql(databaseConnection, o => o.UseNodaTime()));

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AnichronDbContext>();
    await db.Database.MigrateAsync();
}

await host.RunAsync();
