using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Testing.DependencyInjection;
using Anichron.Worker.Settings;
using Anichron.Worker.Startup;
using Anichron.Worker.Work;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anichron.Worker.Tests.Unit.Startup;

public sealed class WorkerInitializerTests
{
    private sealed class TestFixture
    {
        public IUserRepository UserRepository { get; } = Substitute.For<IUserRepository>();
        public IUserStorageConfigRepository StorageConfigRepository { get; } = Substitute.For<IUserStorageConfigRepository>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public WorkerState WorkerState { get; } = new();

        private readonly IServiceScopeFactory _scopeFactory;

        public TestFixture()
        {
            _scopeFactory = ScopeFactories.Providing(services => services
                .AddSingleton(UserRepository)
                .AddSingleton(StorageConfigRepository)
                .AddSingleton(UnitOfWork));
        }

        public WorkerInitializer Build(string user = "", string rootPath = "")
            => new(
                Options.Create(new WorkerSettings { User = user, RootPath = rootPath }),
                _scopeFactory,
                WorkerState,
                Substitute.For<ILogger<WorkerInitializer>>());
    }

    // ==========================================================================
    // All-user mode (User is null/whitespace)
    // ==========================================================================

    [Fact]
    public async Task StartAsync_UserIsEmpty_LogsAllUserModeAndMakesNoDbCallsAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new TestFixture();
        var initializer = fixture.Build(user: string.Empty);

        await initializer.StartAsync(ct);

        await fixture.UserRepository.DidNotReceive().FindByCredentialAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        fixture.WorkerState.ResolvedUserId.Should().BeNull();
    }

    [Fact]
    public async Task StartAsync_UserIsWhitespace_LogsAllUserModeAndMakesNoDbCallsAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new TestFixture();
        var initializer = fixture.Build(user: "   ");

        await initializer.StartAsync(ct);

        await fixture.UserRepository.DidNotReceive().FindByCredentialAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        fixture.WorkerState.ResolvedUserId.Should().BeNull();
    }

    // ==========================================================================
    // User not found in database
    // ==========================================================================

    [Fact]
    public async Task StartAsync_UnknownUser_ThrowsInvalidOperationExceptionWithUserInMessageAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new TestFixture();
        fixture.UserRepository.FindByCredentialAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((User?)null);
        var initializer = fixture.Build(user: "ghost@example.com");

        var act = async () => await initializer.StartAsync(ct);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ghost@example.com*");
    }

    // ==========================================================================
    // Existing config — same user (idempotent)
    // ==========================================================================

    [Fact]
    public async Task StartAsync_ExistingConfigSameUser_SkipsSaveAndSetsResolvedUserIdAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new TestFixture();
        var user = new User { Id = Guid.NewGuid(), Username = "alice" };
        var existingConfig = new UserStorageConfig { Id = Guid.NewGuid(), UserId = user.Id, RootPath = "/data/originals/alice" };
        fixture.UserRepository.FindByCredentialAsync("alice", Arg.Any<CancellationToken>()).Returns(user);
        fixture.StorageConfigRepository.FindByRootPathAsync("/data/originals/alice", Arg.Any<CancellationToken>()).Returns(existingConfig);
        var initializer = fixture.Build(user: "alice", rootPath: "/data/originals/alice");

        await initializer.StartAsync(ct);

        fixture.StorageConfigRepository.DidNotReceive().Add(Arg.Any<UserStorageConfig>());
        await fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        fixture.WorkerState.ResolvedUserId.Should().Be(user.Id);
    }

    // ==========================================================================
    // Existing config — different user (conflict)
    // ==========================================================================

    [Fact]
    public async Task StartAsync_ExistingConfigDifferentUser_ThrowsInvalidOperationExceptionWithRootPathInMessageAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new TestFixture();
        var user = new User { Id = Guid.NewGuid(), Username = "alice" };
        var conflictingConfig = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/data/originals/shared" };
        fixture.UserRepository.FindByCredentialAsync("alice", Arg.Any<CancellationToken>()).Returns(user);
        fixture.StorageConfigRepository.FindByRootPathAsync("/data/originals/shared", Arg.Any<CancellationToken>()).Returns(conflictingConfig);
        var initializer = fixture.Build(user: "alice", rootPath: "/data/originals/shared");

        var act = async () => await initializer.StartAsync(ct);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*/data/originals/shared*");
    }

    // ==========================================================================
    // No existing config — creates new one
    // ==========================================================================

    [Fact]
    public async Task StartAsync_NoExistingConfig_CreatesStorageConfigWithCorrectFieldsAndSetsResolvedUserIdAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new TestFixture();
        var user = new User { Id = Guid.NewGuid(), Username = "alice" };
        fixture.UserRepository.FindByCredentialAsync("alice", Arg.Any<CancellationToken>()).Returns(user);
        fixture.StorageConfigRepository.FindByRootPathAsync("/data/originals/alice", Arg.Any<CancellationToken>())
            .Returns((UserStorageConfig?)null);
        var initializer = fixture.Build(user: "alice", rootPath: "/data/originals/alice");

        await initializer.StartAsync(ct);

        fixture.StorageConfigRepository.Received(1).Add(
            Arg.Is<UserStorageConfig>(storageConfig =>
                storageConfig.UserId == user.Id &&
                storageConfig.RootPath == "/data/originals/alice" &&
                storageConfig.IsActive));
        await fixture.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        fixture.WorkerState.ResolvedUserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task StartAsync_NonCanonicalRootPath_LooksUpAndStoresCanonicalFormAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new TestFixture();
        var user = new User { Id = Guid.NewGuid(), Username = "alice" };
        fixture.UserRepository.FindByCredentialAsync("alice", Arg.Any<CancellationToken>()).Returns(user);
        var initializer = fixture.Build(user: "alice", rootPath: "/data//originals/alice/");

        await initializer.StartAsync(ct);

        await fixture.StorageConfigRepository.Received(1).FindByRootPathAsync("/data/originals/alice", Arg.Any<CancellationToken>());
        fixture.StorageConfigRepository.Received(1).Add(
            Arg.Is<UserStorageConfig>(storageConfig => storageConfig.RootPath == "/data/originals/alice"));
    }

    [Fact]
    public async Task StartAsync_RelativeRootPath_ThrowsBeforeTouchingStorageConfigsAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new TestFixture();
        fixture.UserRepository.FindByCredentialAsync("alice", Arg.Any<CancellationToken>())
            .Returns(new User { Id = Guid.NewGuid(), Username = "alice" });
        var initializer = fixture.Build(user: "alice", rootPath: "data/originals");

        var act = () => initializer.StartAsync(ct);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*data/originals*");
        fixture.StorageConfigRepository.DidNotReceive().Add(Arg.Any<UserStorageConfig>());
    }

    [Fact]
    public async Task StartAsync_RootPathOutsideOriginalsRoot_ThrowsBeforeTouchingStorageConfigsAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new TestFixture();
        fixture.UserRepository.FindByCredentialAsync("alice", Arg.Any<CancellationToken>())
            .Returns(new User { Id = Guid.NewGuid(), Username = "alice" });
        var initializer = fixture.Build(user: "alice", rootPath: "/volume1/homes/Mira/Photos");

        var act = () => initializer.StartAsync(ct);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(
            "WORKER__ROOTPATH '/volume1/homes/Mira/Photos' must be a folder inside /data/originals: use the container path, not the host path.");
        await fixture.StorageConfigRepository.DidNotReceive().FindByRootPathAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        fixture.StorageConfigRepository.DidNotReceive().Add(Arg.Any<UserStorageConfig>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task StartAsync_UserSetWithoutRootPath_ThrowsNamingTheSettingAsync(string rootPath)
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new TestFixture();
        fixture.UserRepository.FindByCredentialAsync("alice", Arg.Any<CancellationToken>())
            .Returns(new User { Id = Guid.NewGuid(), Username = "alice" });
        var initializer = fixture.Build(user: "alice", rootPath: rootPath);

        var act = () => initializer.StartAsync(ct);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*WORKER__ROOTPATH*required*");
        fixture.StorageConfigRepository.DidNotReceive().Add(Arg.Any<UserStorageConfig>());
    }

    [Fact]
    public async Task StartAsync_RootPathIsTheOriginalsRootItself_ThrowsAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new TestFixture();
        fixture.UserRepository.FindByCredentialAsync("alice", Arg.Any<CancellationToken>())
            .Returns(new User { Id = Guid.NewGuid(), Username = "alice" });
        var initializer = fixture.Build(user: "alice", rootPath: "/data/originals");

        var act = () => initializer.StartAsync(ct);

        await act.Should().ThrowAsync<InvalidOperationException>();
        fixture.StorageConfigRepository.DidNotReceive().Add(Arg.Any<UserStorageConfig>());
    }

    [Fact]
    public async Task StartAsync_ExistingConfigSameUser_ResumesWithoutCheckingOverlapAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new TestFixture();
        var user = new User { Id = Guid.NewGuid(), Username = "alice" };
        fixture.UserRepository.FindByCredentialAsync("alice", Arg.Any<CancellationToken>()).Returns(user);
        fixture.StorageConfigRepository.FindByRootPathAsync("/data/originals/alice", Arg.Any<CancellationToken>())
            .Returns(new UserStorageConfig { Id = Guid.NewGuid(), UserId = user.Id, RootPath = "/data/originals/alice" });
        fixture.StorageConfigRepository.GetAllRootPathsAsync(Arg.Any<CancellationToken>())
            .Returns(["/data/originals/alice", "/data/originals/alice/2024"]);
        var initializer = fixture.Build(user: "alice", rootPath: "/data/originals/alice");

        await initializer.StartAsync(ct);

        fixture.WorkerState.ResolvedUserId.Should().Be(user.Id);
    }

    [Theory]
    [InlineData("/data/originals/alice/2024")]
    [InlineData("/data/originals/alice/2024/raw")]
    public async Task StartAsync_RootPathNestedWithAnotherConfig_ThrowsNamingBothPathsAsync(string existing)
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new TestFixture();
        fixture.UserRepository.FindByCredentialAsync("alice", Arg.Any<CancellationToken>())
            .Returns(new User { Id = Guid.NewGuid(), Username = "alice" });
        fixture.StorageConfigRepository.GetAllRootPathsAsync(Arg.Any<CancellationToken>()).Returns([existing]);
        var initializer = fixture.Build(user: "alice", rootPath: "/data/originals/alice");

        var act = () => initializer.StartAsync(ct);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*/data/originals/alice*{existing}*");
        fixture.StorageConfigRepository.DidNotReceive().Add(Arg.Any<UserStorageConfig>());
    }

    [Fact]
    public async Task StartAsync_SiblingConfigSharingANamePrefix_CreatesConfigAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new TestFixture();
        fixture.UserRepository.FindByCredentialAsync("alice", Arg.Any<CancellationToken>())
            .Returns(new User { Id = Guid.NewGuid(), Username = "alice" });
        fixture.StorageConfigRepository.GetAllRootPathsAsync(Arg.Any<CancellationToken>()).Returns(["/data/originals/alice2"]);
        var initializer = fixture.Build(user: "alice", rootPath: "/data/originals/alice");

        await initializer.StartAsync(ct);

        fixture.StorageConfigRepository.Received(1).Add(Arg.Any<UserStorageConfig>());
    }

    // ==========================================================================
    // StopAsync
    // ==========================================================================

    [Fact]
    public async Task StopAsync_AlwaysCompletesSuccessfullyAsync()
    {
        var initializer = new TestFixture().Build();
        var act = async () => await initializer.StopAsync(TestContext.Current.CancellationToken);
        await act.Should().NotThrowAsync();
    }
}
