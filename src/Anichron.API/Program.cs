using Anichron.API.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AddAppConfiguration();

builder.Services
    .AddOpenApi()
    .AddDatabase(builder.Configuration)
    .AddForwardedHeadersSupport()
    .AddAuthServices(builder.Configuration)
    .AddAuthorization()
    .AddRateLimiting()
    .AddCorsPolicy(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapApiEndpoints();

await app.MigrateAndSeedDatabaseAsync(app.Lifetime.ApplicationStopping);
await app.RunAsync();
