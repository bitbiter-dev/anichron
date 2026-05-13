using Anichron.API.Security;
using Anichron.API.Services;
using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;

namespace Anichron.API.Tests.Unit.Services;

public sealed class AdminStorageConfigServiceTests
{
    private sealed class TestFixture
    {
        public IUserRepository Users { get; } = Substitute.For<IUserRepository>();
        public IUserStorageConfigRepository StorageConfigs { get; } = Substitute.For<IUserStorageConfigRepository>();
        public IGuidFactory GuidFactory { get; } = Substitute.For<IGuidFactory>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();

        public AdminStorageConfigService CreateTestee() => new(Users, StorageConfigs, GuidFactory, UnitOfWork);
    }

    // ==========================================================================
    // GetByUserIdAsync
    // ==========================================================================

    [Fact]
    public async Task GetByUserIdAsync_UserNotFound_ReturnsUserNotFound()
    {
        var fixture = new TestFixture();
        var userId = Guid.NewGuid();
        fixture.Users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await fixture.CreateTestee().GetByUserIdAsync(userId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(AuthError.UserNotFound);
        await fixture.StorageConfigs.DidNotReceive().GetByUserIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByUserIdAsync_UserExists_ReturnsConfigs()
    {
        var fixture = new TestFixture();
        var userId = Guid.NewGuid();
        fixture.Users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(new User { Id = userId });
        var configs = new List<UserStorageConfig>
        {
            new() { Id = Guid.NewGuid(), UserId = userId, RootPath = "/nas/photos", IsActive = true },
        };
        fixture.StorageConfigs.GetByUserIdAsync(userId, Arg.Any<CancellationToken>()).Returns(configs);

        var result = await fixture.CreateTestee().GetByUserIdAsync(userId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(configs);
    }

    // ==========================================================================
    // AddAsync
    // ==========================================================================

    [Fact]
    public async Task AddAsync_UserNotFound_ReturnsUserNotFound()
    {
        var fixture = new TestFixture();
        var userId = Guid.NewGuid();
        fixture.Users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await fixture.CreateTestee().AddAsync(userId, "/nas/photos", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(AuthError.UserNotFound);
        fixture.StorageConfigs.DidNotReceive().Add(Arg.Any<UserStorageConfig>());
        await fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddAsync_PathAlreadyAssigned_ReturnsPathAlreadyAssigned()
    {
        var fixture = new TestFixture();
        var userId = Guid.NewGuid();
        fixture.Users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(new User { Id = userId });
        fixture.StorageConfigs.FindByRootPathAsync("/nas/photos", Arg.Any<CancellationToken>())
            .Returns(new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/nas/photos" });

        var result = await fixture.CreateTestee().AddAsync(userId, "/nas/photos", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(AuthError.PathAlreadyAssigned);
        fixture.StorageConfigs.DidNotReceive().Add(Arg.Any<UserStorageConfig>());
        await fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddAsync_ValidInput_AddsConfigAndSaves()
    {
        var fixture = new TestFixture();
        var userId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        fixture.Users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(new User { Id = userId });
        fixture.StorageConfigs.FindByRootPathAsync("/nas/photos", Arg.Any<CancellationToken>())
            .Returns((UserStorageConfig?)null);
        fixture.GuidFactory.NewGuid().Returns(configId);

        var result = await fixture.CreateTestee().AddAsync(userId, "/nas/photos", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(configId);
        result.Value.UserId.Should().Be(userId);
        result.Value.RootPath.Should().Be("/nas/photos");
        result.Value.IsActive.Should().BeTrue();
        fixture.StorageConfigs.Received(1).Add(Arg.Is<UserStorageConfig>(c => c.Id == configId));
        await fixture.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ==========================================================================
    // DeleteAsync
    // ==========================================================================

    [Fact]
    public async Task DeleteAsync_ConfigNotFound_ReturnsStorageConfigNotFound()
    {
        var fixture = new TestFixture();
        var userId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        fixture.StorageConfigs.FindByIdAsync(configId, Arg.Any<CancellationToken>()).Returns((UserStorageConfig?)null);

        var result = await fixture.CreateTestee().DeleteAsync(userId, configId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(AuthError.StorageConfigNotFound);
        fixture.StorageConfigs.DidNotReceive().Remove(Arg.Any<UserStorageConfig>());
        await fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_ConfigBelongsToDifferentUser_ReturnsStorageConfigNotFound()
    {
        var fixture = new TestFixture();
        var userId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        var config = new UserStorageConfig { Id = configId, UserId = Guid.NewGuid(), RootPath = "/nas/photos" };
        fixture.StorageConfigs.FindByIdAsync(configId, Arg.Any<CancellationToken>()).Returns(config);

        var result = await fixture.CreateTestee().DeleteAsync(userId, configId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(AuthError.StorageConfigNotFound);
        fixture.StorageConfigs.DidNotReceive().Remove(Arg.Any<UserStorageConfig>());
        await fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_ValidInput_RemovesConfigAndSaves()
    {
        var fixture = new TestFixture();
        var userId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        var config = new UserStorageConfig { Id = configId, UserId = userId, RootPath = "/nas/photos" };
        fixture.StorageConfigs.FindByIdAsync(configId, Arg.Any<CancellationToken>()).Returns(config);

        var result = await fixture.CreateTestee().DeleteAsync(userId, configId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.StorageConfigs.Received(1).Remove(config);
        await fixture.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
