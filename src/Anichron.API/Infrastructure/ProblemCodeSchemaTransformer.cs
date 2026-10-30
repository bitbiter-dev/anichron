using Anichron.API.Endpoints;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Anichron.API.Infrastructure;

/// <summary>Describes the <c>code</c> extension that the API's own problems carry.</summary>
internal sealed class ProblemCodeSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(ProblemDetails)))
        {
            schema.Properties ??= new Dictionary<string, IOpenApiSchema>();
            schema.Properties[ApiProblems.CodeExtension] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null };
        }

        return Task.CompletedTask;
    }
}
