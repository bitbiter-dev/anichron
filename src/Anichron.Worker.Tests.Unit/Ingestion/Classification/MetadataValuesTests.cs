using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Classification;
using System.Reflection;

namespace Anichron.Worker.Tests.Unit.Ingestion.Classification;

public sealed class MetadataValuesTests
{
    private static IEnumerable<PropertyInfo> ValueProperties => typeof(Metadata)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanWrite && p.Name is not nameof(Metadata.AssetId) and not nameof(Metadata.Asset));

    private static object? DifferentFrom(object? fresh, PropertyInfo property)
        => (Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType) switch
        {
            var type when type == typeof(string) => $"{property.Name}-value",
            var type when type == typeof(double) => 12.5,
            var type when type == typeof(int) => ((int?)fresh ?? 0) + 7,
            _ => fresh,
        };

    internal static Metadata FullyPopulated()
    {
        var fresh = new Metadata();
        var populated = new Metadata();
        foreach (var property in ValueProperties)
            property.SetValue(populated, DifferentFrom(property.GetValue(fresh), property));
        return populated;
    }

    [Fact]
    public void FullyPopulated_DiffersFromAFreshRowInEveryValueProperty()
    {
        var (fresh, populated) = (new Metadata(), FullyPopulated());

        ValueProperties.Should().AllSatisfy(property =>
            property.GetValue(populated).Should().NotBe(property.GetValue(fresh), $"{property.Name} must be filled"));
    }

    [Fact]
    public void Copy_CarriesEveryValuePropertyAndKeepsTheTargetsKey()
    {
        var source = FullyPopulated();
        var target = new Metadata { AssetId = Guid.NewGuid() };
        var key = target.AssetId;

        MetadataValues.Copy(source, target);

        target.Should().BeEquivalentTo(source, options => options
            .Excluding(m => m.AssetId)
            .Excluding(m => m.Asset));
        target.AssetId.Should().Be(key);
    }
}
