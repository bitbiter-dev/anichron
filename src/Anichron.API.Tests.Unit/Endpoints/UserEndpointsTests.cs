using Anichron.API.Endpoints;
using Anichron.API.Infrastructure;
using Anichron.API.Services;
using Anichron.API.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace Anichron.API.Tests.Unit.Endpoints;

public sealed class UserEndpointsTests
{
    private static readonly ChangePasswordRequest ValidRequest = new("OldPass123!", "NewPass456!");

    private static ClaimsPrincipal PrincipalWithGuid(Guid userId)
        => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())]));

    private static ClaimsPrincipal PrincipalWithClaim(string value)
        => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, value)]));

    private static ClaimsPrincipal PrincipalWithoutNameIdentifier()
        => new(new ClaimsIdentity([]));

    // ==========================================================================
    // Claims parsing
    // ==========================================================================

    [Fact]
    public async Task ChangePasswordAsync_MissingNameIdentifierClaim_ReturnsUnauthorized()
    {
        var authService = Substitute.For<IAuthService>();
        var mapper = Substitute.For<IAuthResponseMapper>();
        var options = Options.Create(new PasswordPolicy());

        var result = await UserEndpoints.ChangePasswordAsync(
            ValidRequest, PrincipalWithoutNameIdentifier(), authService, mapper, options,
            CancellationToken.None);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.UnauthorizedHttpResult>();
        await authService.DidNotReceive().ChangePasswordAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangePasswordAsync_NonGuidNameIdentifierClaim_ReturnsUnauthorized()
    {
        var authService = Substitute.For<IAuthService>();
        var mapper = Substitute.For<IAuthResponseMapper>();
        var options = Options.Create(new PasswordPolicy());

        var result = await UserEndpoints.ChangePasswordAsync(
            ValidRequest, PrincipalWithClaim("not-a-guid"), authService, mapper, options,
            CancellationToken.None);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.UnauthorizedHttpResult>();
        await authService.DidNotReceive().ChangePasswordAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ==========================================================================
    // Happy path
    // ==========================================================================

    [Fact]
    public async Task ChangePasswordAsync_ValidClaim_CallsAuthServiceWithCorrectUserId()
    {
        var userId = Guid.NewGuid();
        var authService = Substitute.For<IAuthService>();
        var mapper = Substitute.For<IAuthResponseMapper>();
        var options = Options.Create(new PasswordPolicy());
        authService.ChangePasswordAsync(userId, ValidRequest.CurrentPassword, ValidRequest.NewPassword, Arg.Any<CancellationToken>())
            .Returns(AuthResult.Ok());
        mapper.GetChangePasswordResult(Arg.Any<AuthResult>(), Arg.Any<PasswordPolicy>())
            .Returns(Results.NoContent());

        await UserEndpoints.ChangePasswordAsync(
            ValidRequest, PrincipalWithGuid(userId), authService, mapper, options,
            CancellationToken.None);

        await authService.Received(1).ChangePasswordAsync(
            userId, ValidRequest.CurrentPassword, ValidRequest.NewPassword, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangePasswordAsync_ValidClaim_ReturnsMappedResult()
    {
        var userId = Guid.NewGuid();
        var authService = Substitute.For<IAuthService>();
        var mapper = Substitute.For<IAuthResponseMapper>();
        var options = Options.Create(new PasswordPolicy());
        var expectedResult = Results.NoContent();
        authService.ChangePasswordAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AuthResult.Ok());
        mapper.GetChangePasswordResult(Arg.Any<AuthResult>(), Arg.Any<PasswordPolicy>())
            .Returns(expectedResult);

        var result = await UserEndpoints.ChangePasswordAsync(
            ValidRequest, PrincipalWithGuid(userId), authService, mapper, options,
            CancellationToken.None);

        result.Should().BeSameAs(expectedResult);
    }

    // ==========================================================================
    // Auth service error
    // ==========================================================================

    [Fact]
    public async Task ChangePasswordAsync_ValidClaim_AuthServiceReturnsError_ReturnsMappedResult()
    {
        var userId = Guid.NewGuid();
        var authService = Substitute.For<IAuthService>();
        var mapper = Substitute.For<IAuthResponseMapper>();
        var policy = new PasswordPolicy();
        var options = Options.Create(policy);
        var failureResult = AuthResult.Fail(AuthError.InvalidCredentials);
        var expectedResult = Results.Json(new { error = AuthMessages.InvalidCredentials }, statusCode: 400);
        authService.ChangePasswordAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(failureResult);
        mapper.GetChangePasswordResult(failureResult, policy)
            .Returns(expectedResult);

        var result = await UserEndpoints.ChangePasswordAsync(
            ValidRequest, PrincipalWithGuid(userId), authService, mapper, options,
            CancellationToken.None);

        result.Should().BeSameAs(expectedResult);
        mapper.Received(1).GetChangePasswordResult(failureResult, policy);
    }
}
