using Anichron.API.Endpoints;
using Anichron.Core.Domain;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using System.Text.Json.Nodes;

namespace Anichron.API.Infrastructure;

/// <summary>Describes <c>WorkKind</c> and <c>WorkItemStatus</c> by the names a <see cref="WorkItemDto"/> sends and the list's query binds.</summary>
internal sealed class WorkItemEnumDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var schemas = document.Components!.Schemas!;
        var dto = schemas[nameof(WorkItemDto)].Properties!;
        Name<WorkKind>(schemas, dto, "kind", document);
        Name<WorkItemStatus>(schemas, dto, "status", document);
        return Task.CompletedTask;
    }

    private static void Name<TEnum>(
        IDictionary<string, IOpenApiSchema> schemas, IDictionary<string, IOpenApiSchema> dto, string property, OpenApiDocument document)
        where TEnum : struct, Enum
    {
        var name = typeof(TEnum).Name;
        schemas[name] = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Enum = [.. Enum.GetNames<TEnum>().Select(value => (JsonNode)value)],
        };
        dto[property] = new OpenApiSchemaReference(name, document);
    }
}
