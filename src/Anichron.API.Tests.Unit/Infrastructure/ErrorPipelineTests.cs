using Anichron.API.Tests.Unit.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Anichron.API.Tests.Unit.Infrastructure;

public sealed class ErrorPipelineTests
{
    [Fact]
    public async Task AnUnhandledException_IsAServerErrorProblemAsync()
    {
        await using var app = await AuthenticatedTestApp.StartAsync(
            api => api.MapGet("/boom", IResult () => throw new InvalidOperationException("boom")).AllowAnonymous(),
            _ => { });

        using var response = await app.Client.GetAsync("/api/v1/boom", TestContext.Current.CancellationToken);

        await ProblemAssertions.AssertStatusProblemAsync(response, 500);
    }
}
