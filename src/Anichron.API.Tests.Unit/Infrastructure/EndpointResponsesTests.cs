using Anichron.API.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Anichron.API.Tests.Unit.Infrastructure;

public sealed class EndpointResponsesTests
{
    [Fact]
    public void AStatusLastDeclaredWithoutABody_IsDescribedAsAProblem()
    {
        var endpoint = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse("/"), 0);
        endpoint.Metadata.Add(new ProducesResponseTypeMetadata(404, typeof(ProblemDetails), ["application/problem+json"]));
        endpoint.Metadata.Add(new ProducesResponseTypeMetadata(404, typeof(void)));

        EndpointResponses.DescribeErrorsAsProblems(endpoint);

        endpoint.Metadata.OfType<IProducesResponseTypeMetadata>().Last().Type.Should().Be<ProblemDetails>();
    }
}
