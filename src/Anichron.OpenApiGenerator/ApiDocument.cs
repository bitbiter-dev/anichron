using Anichron.API.Infrastructure;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Anichron.OpenApiGenerator;

public static class ApiDocument
{
    public static async Task<OpenApiDocument> ComposeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = "Anichron.API",
            EnvironmentName = Environments.Production,
        });
        builder.Services.AddApiSurface(builder.Configuration);
        WebApplication? app = null;
        builder.Services.AddSingleton<EndpointDataSource>(
            _ => new CompositeEndpointDataSource(((IEndpointRouteBuilder)app!).DataSources));

        await using (app = builder.Build())
        {
            app.MapApiEndpoints();
            return await app.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1").GetOpenApiDocumentAsync();
        }
    }

    public static async Task WriteAsync(string path)
    {
        var document = await ComposeAsync();
        var json = await document.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_1);
        await File.WriteAllTextAsync(path, json.ReplaceLineEndings("\n") + "\n");
    }
}
