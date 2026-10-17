using Anichron.Core.Data;
using Anichron.Core.Domain;
using Anichron.Testing.Data;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Tests.Unit.Data;

public sealed class ModelInvariantsTests
{
    [Fact]
    public void ContentHash_IsUniqueOnlyPerStorageConfigAndOnlyAmongActiveAssets()
    {
        using var context = AnichronDbContexts.NpgsqlModel();
        var mediaAsset = context.Model.FindEntityType(typeof(MediaAsset))!;

        var uniqueWithHash = mediaAsset.GetIndexes()
            .Where(index => index.IsUnique && index.Properties.Any(p => p.Name == nameof(MediaAsset.ContentHash)))
            .ToList();

        var index = uniqueWithHash.Should().ContainSingle(
            "dedup is config-scoped; a unique index on ContentHash alone would merge two users' identical files").Which;
        index.Properties.Select(p => p.Name).Should().Equal(nameof(MediaAsset.StorageConfigId), nameof(MediaAsset.ContentHash));
        index.GetFilter().Should().Be("\"IsSoftDeleted\" = false");
        index.GetDatabaseName().Should().Be(MediaAssetIndexNames.ActiveContentHashUnique);
    }

    [Fact]
    public void QueryFilters_AreSoftDeleteOnly()
    {
        using var context = AnichronDbContexts.NpgsqlModel();

        var filters = context.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetDeclaredQueryFilters()
                .Select(filter => (Entity: entity.ClrType.Name, Expression: filter.Expression!.ToString())))
            .ToList();

        filters.Select(filter => filter.Entity).Should().BeEquivalentTo(
            [nameof(MediaAsset), nameof(Metadata), nameof(ProxyFile), nameof(AssetInteraction), nameof(Burst)],
            "soft-delete is the only global filter: on the asset and on what hangs off it, one filter each");
        filters.Should().AllSatisfy(filter => filter.Expression.Should().Contain(
            nameof(MediaAsset.IsSoftDeleted),
            "per-user rules (hidden, ownership) stay query-level, because the Worker shares the context and has no user"));
    }
}
