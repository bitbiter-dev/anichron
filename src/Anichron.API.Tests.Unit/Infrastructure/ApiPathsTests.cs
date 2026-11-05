using Anichron.API.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace Anichron.API.Tests.Unit.Infrastructure;

public sealed class ApiPathsTests
{
    [Theory]
    [InlineData("/api")]
    [InlineData("/api/")]
    [InlineData("/api/v1/healthz")]
    [InlineData("/API/v1/auth/login")]
    public void IsApi_UnderTheApiRoot_IsTrue(string path)
        => ApiPaths.IsApi(new PathString(path)).Should().BeTrue();

    [Theory]
    [InlineData("/")]
    [InlineData("/apis")]
    [InlineData("/login")]
    [InlineData("/assets/api/index.js")]
    public void IsApi_OutsideTheApiRoot_IsFalse(string path)
        => ApiPaths.IsApi(new PathString(path)).Should().BeFalse();
}
