using Anichron.API.Endpoints;
using Anichron.Core.Domain;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using System.Text.Json.Nodes;

namespace Anichron.API.Infrastructure;

/// <summary>Describes the DTO properties that send an enum's name, and the work items' query binds, by that enum's names.</summary>
internal sealed class EnumDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        Name<WorkKind>(document, nameof(WorkItemDto), "kind");
        Name<WorkItemStatus>(document, nameof(WorkItemDto), "status");
        Name<PublishedMediaType>(document, nameof(AssetDto), "mediaType");
        Name<PublishedMediaType>(document, nameof(SoftDeletedAssetDto), "mediaType");
        Name<SoftDeleteReason>(document, nameof(SoftDeletedAssetDto), "reason");
        return Task.CompletedTask;
    }

    private static void Name<TEnum>(OpenApiDocument document, string dto, string property)
        where TEnum : struct, Enum
    {
        var schemas = document.Components!.Schemas!;
        var name = typeof(TEnum).Name;
        schemas[name] = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Enum = [.. Enum.GetNames<TEnum>().Select(value => (JsonNode)value)],
        };
        schemas[dto].Properties![property] = new OpenApiSchemaReference(name, document);
    }
}
