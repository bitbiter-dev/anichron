using Anichron.Worker;
using Anichron.Core.Data;
using Microsoft.EntityFrameworkCore;
using Anichron.Infrastructure.Configuration;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();

builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json")
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddJsonFile("appsettings.local.json", optional: true)
    .AddEnvironmentVariables();

var databaseConnection = DatabaseConfiguration.GetConnectionString(builder.Configuration);

builder.Services.AddDbContext<AnichronDbContext>(options =>
    options.UseNpgsql(databaseConnection, o => o.UseNodaTime()));

var host = builder.Build();
host.Run();
