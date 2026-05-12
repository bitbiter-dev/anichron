using Anichron.API.Endpoints;
using Anichron.API.Services;
using Anichron.Core.Domain;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace Anichron.API.Tests.Unit.Endpoints;

public sealed class AdminUserEndpointsTests
{
    private static ClaimsPrincipal PrincipalWithGuid(Guid id)
        => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())]));

    private static ClaimsPrincipal PrincipalWithoutNameIdentifier()
        => new(new ClaimsIdentity([]));

    // ==========================================================================
    // GetUsersAsync
    // ==========================================================================

    [Fact]
    public async Task GetUsersAsync_CallsServiceAndReturnsMapperResult()
    {
        var service = Substitute.For<IAdminUserService>();
        var mapper = Substitute.For<IAuthResponseMapper>();
        var users = new List<User>();
        var expected = Results.Ok(users);
        service.GetAllAsync(Arg.Any<CancellationToken>()).Returns(users);
        mapper.GetAdminGetUsersResult(users).Returns(expected);

        var result = await AdminEndpoints.GetUsersAsync(service, mapper, CancellationToken.None);

        result.Should().BeSameAs(expected);
    }

    // ==========================================================================
    // GetUserAsync
    // ==========================================================================

    [Fact]
    public async Task GetUserAsync_PassesUserIdToServiceAndReturnsMapperResult()
    {
        var service = Substitute.For<IAdminUserService>();
        var mapper = Substitute.For<IAuthResponseMapper>();
        var userId = Guid.NewGuid();
        var user = new User { Id = userId };
        var expected = Results.Ok(user);
        service.GetByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(user);
        mapper.GetAdminGetUserResult(user).Returns(expected);

        var result = await AdminEndpoints.GetUserAsync(userId, service, mapper, CancellationToken.None);

        result.Should().BeSameAs(expected);
    }

    // ==========================================================================
    // PatchUserAsync
    // ==========================================================================

    [Fact]
    public async Task PatchUserAsync_NoNameIdentifierClaim_ReturnsUnauthorized()
    {
        var service = Substitute.For<IAdminUserService>();
        var mapper = Substitute.For<IAuthResponseMapper>();

        var result = await AdminEndpoints.PatchUserAsync(
            Guid.NewGuid(), new PatchAdminUserRequest(null, null),
            PrincipalWithoutNameIdentifier(), service, mapper, CancellationToken.None);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(401);
        await service.DidNotReceive().UpdateAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<bool?>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PatchUserAsync_PassesCallerAndTargetIdsAndReturnsMappedResult()
    {
        var service = Substitute.For<IAdminUserService>();
        var mapper = Substitute.For<IAuthResponseMapper>();
        var callerId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var req = new PatchAdminUserRequest(IsAdmin: true, IsDisabled: null);
        var serviceResult = AuthResult.Ok(new User { Id = targetId, StorageConfigs = [] });
        var expected = Results.Ok();
        service.UpdateAsync(callerId, targetId, true, null, Arg.Any<CancellationToken>()).Returns(serviceResult);
        mapper.GetAdminPatchUserResult(serviceResult).Returns(expected);

        var result = await AdminEndpoints.PatchUserAsync(
            targetId, req, PrincipalWithGuid(callerId), service, mapper, CancellationToken.None);

        result.Should().BeSameAs(expected);
        await service.Received(1).UpdateAsync(callerId, targetId, true, null, Arg.Any<CancellationToken>());
    }

    // ==========================================================================
    // DeleteUserAsync
    // ==========================================================================

    [Fact]
    public async Task DeleteUserAsync_NoNameIdentifierClaim_ReturnsUnauthorized()
    {
        var service = Substitute.For<IAdminUserService>();
        var mapper = Substitute.For<IAuthResponseMapper>();

        var result = await AdminEndpoints.DeleteUserAsync(
            Guid.NewGuid(), PrincipalWithoutNameIdentifier(), service, mapper, CancellationToken.None);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(401);
        await service.DidNotReceive().DeleteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteUserAsync_PassesCallerAndTargetIdsAndReturnsMappedResult()
    {
        var service = Substitute.For<IAdminUserService>();
        var mapper = Substitute.For<IAuthResponseMapper>();
        var callerId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var serviceResult = AuthResult.Ok();
        var expected = Results.NoContent();
        service.DeleteAsync(callerId, targetId, Arg.Any<CancellationToken>()).Returns(serviceResult);
        mapper.GetAdminDeleteUserResult(serviceResult).Returns(expected);

        var result = await AdminEndpoints.DeleteUserAsync(
            targetId, PrincipalWithGuid(callerId), service, mapper, CancellationToken.None);

        result.Should().BeSameAs(expected);
        await service.Received(1).DeleteAsync(callerId, targetId, Arg.Any<CancellationToken>());
    }
}
