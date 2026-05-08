using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Infrastructure.Configuration;
using Anichron.Worker;
using Anichron.Worker.Infrastructure;
using Anichron.Worker.Settings;
using Anichron.Worker.Startup;
using Microsoft.EntityFrameworkCore;
using System.IO.Abstractions;

var builder = Host.CreateApplicationBuilder(args);
builder.AddAppConfiguration();

builder.Services.Configure<WorkerSettings>(builder.Configuration.GetSection("Worker"));
builder.Services.AddSingleton<WorkerState>();
builder.Services.AddScoped<IUserRepository, EfUserRepository>();
builder.Services.AddScoped<IUserStorageConfigRepository, EfUserStorageConfigRepository>();

var databaseConnection = DatabaseConfiguration.GetConnectionString(builder.Configuration, new FileSystem());
builder.Services.AddDbContext<AnichronDbContext>(options =>
    options.UseNpgsql(databaseConnection, o => o.UseNodaTime()));

builder.Services.AddHostedService<DatabaseMigratorService>();
builder.Services.AddHostedService<WorkerInitializer>();
builder.Services.AddHostedService<Worker>();

await builder.Build().RunAsync();
