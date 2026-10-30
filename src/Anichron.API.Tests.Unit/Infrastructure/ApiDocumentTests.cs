using Anichron.OpenApiGenerator;
using System.Text.RegularExpressions;

namespace Anichron.API.Tests.Unit.Infrastructure;

public sealed partial class ApiDocumentTests(RouteTableTests.RouteTable table) : IClassFixture<RouteTableTests.RouteTable>
{
    [GeneratedRegex(@"\{(\w+):[^}]+\}")]
    private static partial Regex RouteConstraint();

    [Fact]
    public async Task Document_DescribesEveryRouteButHealthz()
    {
        var document = await ApiDocument.ComposeAsync();

        var described = document.Paths
            .SelectMany(path => path.Value.Operations!.Keys.Select(method => $"{method.Method} {path.Key}"));
        var mapped = table.Routes
            .Where(route => route.Pattern != "/api/v1/healthz")
            .Select(route => $"{route.Method} {RouteConstraint().Replace(route.Pattern, "{$1}")}");

        described.Should().BeEquivalentTo(mapped);
    }

    [Fact]
    public async Task Document_IsTitledAfterTheApi()
    {
        var document = await ApiDocument.ComposeAsync();

        document.Info.Title.Should().Be("Anichron.API | v1");
    }
}
