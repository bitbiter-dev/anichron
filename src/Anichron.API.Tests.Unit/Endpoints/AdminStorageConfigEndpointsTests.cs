using Anichron.API.Endpoints;
using Anichron.API.Services;
using Anichron.Core.Domain;
using Microsoft.AspNetCore.Http;

namespace Anichron.API.Tests.Unit.Endpoints;

public sealed class AdminStorageConfigEndpointsTests
{
    // ==========================================================================
    // GetStorageConfigsAsync
    // ==========================================================================

    [Fact]
    public async Task GetStorageConfigsAsync_CallsServiceAndReturnsMapperResult()
    {
        var service = Substitute.For<IAdminStorageConfigService>();
        var mapper = Substitute.For<IAuthResponseMapper>();
        var userId = Guid.NewGuid();
        var serviceResult = AuthResult.Ok(new List<UserStorageConfig>());
        var expected = Results.Ok(serviceResult.Value);
        service.GetByUserIdAsync(userId, Arg.Any<CancellationToken>()).Returns(serviceResult);
        mapper.GetAdminGetStorageConfigsResult(serviceResult).Returns(expected);

        var result = await AdminEndpoints.GetStorageConfigsAsync(userId, service, mapper, CancellationToken.None);

        result.Should().BeSameAs(expected);
        await service.Received(1).GetByUserIdAsync(userId, Arg.Any<CancellationToken>());
        mapper.Received(1).GetAdminGetStorageConfigsResult(serviceResult);
    }

    // ==========================================================================
    // CreateStorageConfigAsync
    // ==========================================================================

    [Fact]
    public async Task CreateStorageConfigAsync_CallsServiceWithUserIdAndRootPathAndReturnsMapperResult()
    {
        var service = Substitute.For<IAdminStorageConfigService>();
        var mapper = Substitute.For<IAuthResponseMapper>();
        var userId = Guid.NewGuid();
        var req = new CreateStorageConfigRequest("/nas/photos");
        var config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = userId, RootPath = "/nas/photos" };
        var serviceResult = AuthResult.Ok(config);
        var expected = Results.Created("/some/location", config);
        service.AddAsync(userId, "/nas/photos", Arg.Any<CancellationToken>()).Returns(serviceResult);
        mapper.GetAdminCreateStorageConfigResult(serviceResult).Returns(expected);

        var result = await AdminEndpoints.CreateStorageConfigAsync(userId, req, service, mapper, CancellationToken.None);

        result.Should().BeSameAs(expected);
        await service.Received(1).AddAsync(userId, "/nas/photos", Arg.Any<CancellationToken>());
        mapper.Received(1).GetAdminCreateStorageConfigResult(serviceResult);
    }

    // ==========================================================================
    // DeleteStorageConfigAsync
    // ==========================================================================

    [Fact]
    public async Task DeleteStorageConfigAsync_CallsServiceWithUserIdAndConfigIdAndReturnsMapperResult()
    {
        var service = Substitute.For<IAdminStorageConfigService>();
        var mapper = Substitute.For<IAuthResponseMapper>();
        var userId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        var serviceResult = AuthResult.Ok();
        var expected = Results.NoContent();
        service.DeleteAsync(userId, configId, Arg.Any<CancellationToken>()).Returns(serviceResult);
        mapper.GetAdminDeleteStorageConfigResult(serviceResult).Returns(expected);

        var result = await AdminEndpoints.DeleteStorageConfigAsync(userId, configId, service, mapper, CancellationToken.None);

        result.Should().BeSameAs(expected);
        await service.Received(1).DeleteAsync(userId, configId, Arg.Any<CancellationToken>());
        mapper.Received(1).GetAdminDeleteStorageConfigResult(serviceResult);
    }
}
