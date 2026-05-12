using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Infrastructure.Configuration;
using Anichron.Worker.Crawling;
using Anichron.Worker.Infrastructure;
using Anichron.Worker.Maintenance;
using Anichron.Worker.Settings;
using Anichron.Worker.Startup;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using System.IO.Abstractions;

var builder = Host.CreateApplicationBuilder(args);
builder.AddAppConfiguration();

builder.Services.Configure<WorkerSettings>(builder.Configuration.GetSection("Worker"));
builder.Services.AddSingleton<IClock>(SystemClock.Instance);
builder.Services.AddSingleton<WorkerState>();
builder.Services.AddScoped<IUserRepository, EfUserRepository>();
builder.Services.AddScoped<IUserStorageConfigRepository, EfUserStorageConfigRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, EfRefreshTokenRepository>();
builder.Services.AddScoped<IDatabaseMigrator, EfDatabaseMigrator>();

var databaseConnection = DatabaseConfiguration.GetConnectionString(builder.Configuration, new FileSystem());
builder.Services.AddDbContext<AnichronDbContext>(options =>
    options.UseNpgsql(databaseConnection, o => o.UseNodaTime()));

builder.Services.AddHostedService<DatabaseMigratorService>();
builder.Services.AddHostedService<WorkerInitializer>();
builder.Services.AddHostedService<TokenCleanupService>();
builder.Services.AddHostedService<Worker>();

await builder.Build().RunAsync();
