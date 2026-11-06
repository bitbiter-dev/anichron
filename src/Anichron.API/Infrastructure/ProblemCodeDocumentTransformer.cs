using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using System.Text.Json.Nodes;

namespace Anichron.API.Infrastructure;

/// <summary>Describes the <c>code</c> extension that the API's own problems carry, and each field error, as an <c>ErrorCode</c>.</summary>
internal sealed class ProblemCodeDocumentTransformer : IOpenApiDocumentTransformer
{
    private const string errorCodeSchema = "ErrorCode";
    private const string errorsProperty = "errors";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var schemas = document.Components!.Schemas!;
        document.AddComponent(errorCodeSchema, ErrorCode());
        foreach (var name in new[] { nameof(ProblemDetails), nameof(HttpValidationProblemDetails) })
            schemas[name].Properties![ApiProblems.CodeExtension] = new OpenApiSchemaReference(errorCodeSchema, document);

        schemas[nameof(HttpValidationProblemDetails)].Properties![errorsProperty] = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            AdditionalProperties = new OpenApiSchema
            {
                Type = JsonSchemaType.Array,
                Items = new OpenApiSchemaReference(errorCodeSchema, document),
            },
        };

        return Task.CompletedTask;
    }

    private static OpenApiSchema ErrorCode()
        => new()
        {
            Type = JsonSchemaType.String,
            Enum = [.. ErrorCodes.All().Select(code => (JsonNode)code)],
        };
}
