using Anichron.API.Endpoints;
using Anichron.Core.Domain;
using System.Reflection;

namespace Anichron.API.Tests.Unit.Endpoints;

public sealed class ResponseContractTests
{
    private static IEnumerable<Type> ResponseTypes()
        => typeof(AssetDto).Assembly.GetTypes()
            .Where(type => type.Name.EndsWith("Response", StringComparison.Ordinal)
                           || type.Name.EndsWith("Dto", StringComparison.Ordinal));

    private static HashSet<Type> Reachable(Type root)
    {
        var seen = new HashSet<Type>();
        var pending = new Stack<Type>([root]);
        while (pending.TryPop(out var type))
        {
            if (!seen.Add(type))
                continue;

            if (type.HasElementType)
                pending.Push(type.GetElementType()!);
            foreach (var argument in type.GetGenericArguments())
                pending.Push(argument);
            if (type.Namespace?.StartsWith("Anichron.", StringComparison.Ordinal) == true)
            {
                foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    pending.Push(property.PropertyType);
            }
        }

        return seen;
    }

    private static IEnumerable<Type> EntityTypesIn(Type root)
        => Reachable(root).Where(type => type.Namespace == typeof(MediaAsset).Namespace);

    [Fact]
    public void NoResponseType_ExposesAnEfEntity()
    {
        var leaks = ResponseTypes()
            .SelectMany(response => EntityTypesIn(response).Select(entity => $"{response.Name} -> {entity.Name}"));

        leaks.Should().BeEmpty();
    }

    [Fact]
    public void TheScan_CoversTheAssetDtoAndWhatItContains()
    {
        ResponseTypes().Should().Contain([typeof(AssetDto), typeof(UserProfileResponse)]);
        Reachable(typeof(AssetDto)).Should().Contain([typeof(AssetProxyUrls), typeof(AssetInteractionDto)]);
    }

    private sealed record LeakyResponse(IReadOnlyList<Wrapper?>[] Pages);

    private sealed record Wrapper(MediaType Type);

    [Fact]
    public void TheScan_FindsAnEntityNestedInsideCollectionsAndRecords()
    {
        EntityTypesIn(typeof(LeakyResponse)).Should().Equal(typeof(MediaType));
    }
}
