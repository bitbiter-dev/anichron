using Anichron.API;
using Anichron.API.Infrastructure;
using Anichron.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

if (args is [DatabaseStartupExtensions.MigrateCommand, .. var migrateArgs])
{
    await DatabaseStartupExtensions.MigrateAndSeedAsync(migrateArgs);
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.AddAppConfiguration(ApiIniDefaults.Entries);
builder.Services.AddApiSurface(builder.Configuration);

var app = builder.Build();

app.UseSecurityHeaders();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseForwardedHeaders();
app.UseApiProblems();
app.UseCors();
app.UseUi();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapApiEndpoints();

app.Services.GetRequiredService<IStartupValidator>().Validate();
await app.RunAsync();
