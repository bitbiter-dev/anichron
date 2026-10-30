using Anichron.API.Infrastructure;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.AddAppConfiguration();
builder.Services.AddApiSurface(builder.Configuration);

var app = builder.Build();

app.UseSecurityHeaders();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.UseCors();
app.UseUi();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<MustChangePasswordMiddleware>();
app.UseAuthorization();

app.MapApiEndpoints();

app.Services.GetRequiredService<IStartupValidator>().Validate();
await app.MigrateAndSeedDatabaseAsync(app.Lifetime.ApplicationStopping);
await app.RunAsync();
