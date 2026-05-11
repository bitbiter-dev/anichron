using Anichron.API.Endpoints;
using Anichron.API.Services;
using Microsoft.AspNetCore.Http;

namespace Anichron.API.Tests.Unit.Endpoints;

public sealed class AdminEndpointsTests
{
    private static readonly CreateAdminUserRequest ValidRequest = new("alice", "alice@example.com");

    // ==========================================================================
    // Delegates to mapper
    // ==========================================================================

    [Fact]
    public async Task CreateUserAsync_CallsMapperWithServiceResult()
    {
        var userId = Guid.NewGuid();
        var serviceResult = AuthResult.Ok(new AdminCreatedUser(userId, "alice", "alice@example.com", "TempPass=="));
        var expectedResult = Results.Created($"/api/v1/users/{userId}", new AdminCreatedUserResponse(userId, "alice", "alice@example.com", "TempPass=="));
        var auth = Substitute.For<IAuthService>();
        var mapper = Substitute.For<IAuthResponseMapper>();
        auth.AdminCreateUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(serviceResult);
        mapper.GetAdminCreateUserResult(serviceResult).Returns(expectedResult);

        var result = await AdminEndpoints.CreateUserAsync(ValidRequest, auth, mapper, CancellationToken.None);

        result.Should().BeSameAs(expectedResult);
        mapper.Received(1).GetAdminCreateUserResult(serviceResult);
    }

    [Fact]
    public async Task CreateUserAsync_CallsServiceWithRequestValues()
    {
        var auth = Substitute.For<IAuthService>();
        var mapper = Substitute.For<IAuthResponseMapper>();
        auth.AdminCreateUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AuthResult.Ok(new AdminCreatedUser(Guid.NewGuid(), "alice", "alice@example.com", "TempPass==")));
        mapper.GetAdminCreateUserResult(Arg.Any<AuthResult<AdminCreatedUser>>())
            .Returns(Results.Created("/", null as AdminCreatedUserResponse));

        await AdminEndpoints.CreateUserAsync(ValidRequest, auth, mapper, CancellationToken.None);

        await auth.Received(1).AdminCreateUserAsync(
            ValidRequest.Username, ValidRequest.Email, Arg.Any<CancellationToken>());
    }

    // ==========================================================================
    // ResetUserPasswordAsync
    // ==========================================================================

    [Fact]
    public async Task ResetUserPasswordAsync_CallsServiceWithUserIdAndPassesResultToMapper()
    {
        var userId = Guid.NewGuid();
        var serviceResult = new AdminUserPasswordReset("TempPass==");
        var expectedResult = Results.Ok(new { temporaryPassword = "TempPass==" });
        var adminReset = Substitute.For<IAdminResetService>();
        var mapper = Substitute.For<IAuthResponseMapper>();
        adminReset.ResetUserPasswordAsync(userId, Arg.Any<CancellationToken>()).Returns(serviceResult);
        mapper.GetAdminResetPasswordResult(serviceResult).Returns(expectedResult);

        var result = await AdminEndpoints.ResetUserPasswordAsync(userId, adminReset, mapper, CancellationToken.None);

        result.Should().BeSameAs(expectedResult);
        await adminReset.Received(1).ResetUserPasswordAsync(userId, Arg.Any<CancellationToken>());
        mapper.Received(1).GetAdminResetPasswordResult(serviceResult);
    }
}
