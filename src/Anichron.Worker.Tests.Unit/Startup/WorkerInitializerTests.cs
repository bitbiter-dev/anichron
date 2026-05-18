using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Crawling;
using Anichron.Worker.Settings;
using Anichron.Worker.Startup;
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
            var serviceProvider = Substitute.For<IServiceProvider>();
            var scope = Substitute.For<IServiceScope>();
            scope.ServiceProvider.Returns(serviceProvider);

            _scopeFactory = Substitute.For<IServiceScopeFactory>();
            _scopeFactory.CreateScope().Returns(scope);

            serviceProvider.GetService(typeof(IUserRepository)).Returns(UserRepository);
            serviceProvider.GetService(typeof(IUserStorageConfigRepository)).Returns(StorageConfigRepository);
            serviceProvider.GetService(typeof(IUnitOfWork)).Returns(UnitOfWork);
        }

        public WorkerInitializer Build(string user = "", string rootPath = "/data/originals")
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
    // Credential normalization (trim + lowercase)
    // ==========================================================================

    [Fact]
    public async Task StartAsync_CredentialNormalized_TrimsAndLowercasesAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new TestFixture();
        var user = new User { Id = Guid.NewGuid(), Username = "alice" };
        fixture.UserRepository.FindByCredentialAsync("alice@example.com", Arg.Any<CancellationToken>())
            .Returns(user);
        var existingConfig = new UserStorageConfig { Id = Guid.NewGuid(), UserId = user.Id, RootPath = "/data" };
        fixture.StorageConfigRepository.FindByRootPathAsync("/data", Arg.Any<CancellationToken>())
            .Returns(existingConfig);
        var initializer = fixture.Build(user: "  Alice@Example.COM  ", rootPath: "/data");

        await initializer.StartAsync(ct);

        await fixture.UserRepository.Received(1)
            .FindByCredentialAsync("alice@example.com", Arg.Any<CancellationToken>());
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
        var existingConfig = new UserStorageConfig { Id = Guid.NewGuid(), UserId = user.Id, RootPath = "/nas" };
        fixture.UserRepository.FindByCredentialAsync("alice", Arg.Any<CancellationToken>()).Returns(user);
        fixture.StorageConfigRepository.FindByRootPathAsync("/nas", Arg.Any<CancellationToken>()).Returns(existingConfig);
        var initializer = fixture.Build(user: "alice", rootPath: "/nas");

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
        var conflictingConfig = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/shared" };
        fixture.UserRepository.FindByCredentialAsync("alice", Arg.Any<CancellationToken>()).Returns(user);
        fixture.StorageConfigRepository.FindByRootPathAsync("/shared", Arg.Any<CancellationToken>()).Returns(conflictingConfig);
        var initializer = fixture.Build(user: "alice", rootPath: "/shared");

        var act = async () => await initializer.StartAsync(ct);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*/shared*");
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
        fixture.StorageConfigRepository.FindByRootPathAsync("/nas", Arg.Any<CancellationToken>())
            .Returns((UserStorageConfig?)null);
        var initializer = fixture.Build(user: "alice", rootPath: "/nas");

        await initializer.StartAsync(ct);

        fixture.StorageConfigRepository.Received(1).Add(
            Arg.Is<UserStorageConfig>(storageConfig =>
                storageConfig.UserId == user.Id &&
                storageConfig.RootPath == "/nas" &&
                storageConfig.IsActive));
        await fixture.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        fixture.WorkerState.ResolvedUserId.Should().Be(user.Id);
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
