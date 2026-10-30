using System.Text.Json;

namespace Anichron.API.Tests.Unit.Endpoints;

internal static class ProblemAssertions
{
    public static async Task<JsonElement> AssertProblemAsync(
        HttpResponseMessage response, int status, string code, string detail)
    {
        ((int)response.StatusCode).Should().Be(status);
        var problem = await ProblemAsync(response);
        problem.GetProperty("code").GetString().Should().Be(code);
        problem.GetProperty("status").GetInt32().Should().Be(status);
        problem.GetProperty("detail").GetString().Should().Be(detail);
        return problem;
    }

    public static async Task AssertFieldProblemAsync(
        HttpResponseMessage response, int status, string code, string field, string detail)
    {
        var problem = await AssertProblemAsync(response, status, code, detail);
        var errors = problem.GetProperty("errors").EnumerateObject().ToList();
        errors.Should().ContainSingle().Which.Name.Should().Be(field);
        errors[0].Value.EnumerateArray().Select(e => e.GetString()).Should().Equal(code);
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return body.RootElement.Clone();
    }
}
