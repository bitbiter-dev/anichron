using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
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
    public async Task StartAsync_UserIsEmpty_LogsAllUserModeAndMakesNoDbCalls()
    {
        var ct = TestContext.Current.CancellationToken;
        var fx = new TestFixture();
        var sut = fx.Build(user: string.Empty);

        await sut.StartAsync(ct);

        await fx.UserRepository.DidNotReceive().FindByCredentialAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await fx.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        fx.WorkerState.ResolvedUserId.Should().BeNull();
    }

    [Fact]
    public async Task StartAsync_UserIsWhitespace_LogsAllUserModeAndMakesNoDbCalls()
    {
        var ct = TestContext.Current.CancellationToken;
        var fx = new TestFixture();
        var sut = fx.Build(user: "   ");

        await sut.StartAsync(ct);

        await fx.UserRepository.DidNotReceive().FindByCredentialAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        fx.WorkerState.ResolvedUserId.Should().BeNull();
    }

    // ==========================================================================
    // User not found in database
    // ==========================================================================

    [Fact]
    public async Task StartAsync_UnknownUser_ThrowsInvalidOperationExceptionWithUserInMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        var fx = new TestFixture();
        fx.UserRepository.FindByCredentialAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((User?)null);
        var sut = fx.Build(user: "ghost@example.com");

        var act = async () => await sut.StartAsync(ct);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ghost@example.com*");
    }

    // ==========================================================================
    // Credential normalization (trim + lowercase)
    // ==========================================================================

    [Fact]
    public async Task StartAsync_CredentialNormalized_TrimsAndLowercases()
    {
        var ct = TestContext.Current.CancellationToken;
        var fx = new TestFixture();
        var user = new User { Id = Guid.NewGuid(), Username = "alice" };
        fx.UserRepository.FindByCredentialAsync("alice@example.com", Arg.Any<CancellationToken>())
            .Returns(user);
        var existingConfig = new UserStorageConfig { Id = Guid.NewGuid(), UserId = user.Id, RootPath = "/data" };
        fx.StorageConfigRepository.FindByRootPathAsync("/data", Arg.Any<CancellationToken>())
            .Returns(existingConfig);
        var sut = fx.Build(user: "  Alice@Example.COM  ", rootPath: "/data");

        await sut.StartAsync(ct);

        await fx.UserRepository.Received(1)
            .FindByCredentialAsync("alice@example.com", Arg.Any<CancellationToken>());
    }

    // ==========================================================================
    // Existing config — same user (idempotent)
    // ==========================================================================

    [Fact]
    public async Task StartAsync_ExistingConfigSameUser_SkipsSaveAndSetsResolvedUserId()
    {
        var ct = TestContext.Current.CancellationToken;
        var fx = new TestFixture();
        var user = new User { Id = Guid.NewGuid(), Username = "alice" };
        var existingConfig = new UserStorageConfig { Id = Guid.NewGuid(), UserId = user.Id, RootPath = "/nas" };
        fx.UserRepository.FindByCredentialAsync("alice", Arg.Any<CancellationToken>()).Returns(user);
        fx.StorageConfigRepository.FindByRootPathAsync("/nas", Arg.Any<CancellationToken>()).Returns(existingConfig);
        var sut = fx.Build(user: "alice", rootPath: "/nas");

        await sut.StartAsync(ct);

        fx.StorageConfigRepository.DidNotReceive().Add(Arg.Any<UserStorageConfig>());
        await fx.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        fx.WorkerState.ResolvedUserId.Should().Be(user.Id);
    }

    // ==========================================================================
    // Existing config — different user (conflict)
    // ==========================================================================

    [Fact]
    public async Task StartAsync_ExistingConfigDifferentUser_ThrowsInvalidOperationExceptionWithRootPathInMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        var fx = new TestFixture();
        var user = new User { Id = Guid.NewGuid(), Username = "alice" };
        var conflictingConfig = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/shared" };
        fx.UserRepository.FindByCredentialAsync("alice", Arg.Any<CancellationToken>()).Returns(user);
        fx.StorageConfigRepository.FindByRootPathAsync("/shared", Arg.Any<CancellationToken>()).Returns(conflictingConfig);
        var sut = fx.Build(user: "alice", rootPath: "/shared");

        var act = async () => await sut.StartAsync(ct);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*/shared*");
    }

    // ==========================================================================
    // No existing config — creates new one
    // ==========================================================================

    [Fact]
    public async Task StartAsync_NoExistingConfig_CreatesStorageConfigWithCorrectFieldsAndSetsResolvedUserId()
    {
        var ct = TestContext.Current.CancellationToken;
        var fx = new TestFixture();
        var user = new User { Id = Guid.NewGuid(), Username = "alice" };
        fx.UserRepository.FindByCredentialAsync("alice", Arg.Any<CancellationToken>()).Returns(user);
        fx.StorageConfigRepository.FindByRootPathAsync("/nas", Arg.Any<CancellationToken>())
            .Returns((UserStorageConfig?)null);
        var sut = fx.Build(user: "alice", rootPath: "/nas");

        await sut.StartAsync(ct);

        fx.StorageConfigRepository.Received(1).Add(
            Arg.Is<UserStorageConfig>(c =>
                c.UserId == user.Id &&
                c.RootPath == "/nas" &&
                c.IsActive));
        await fx.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        fx.WorkerState.ResolvedUserId.Should().Be(user.Id);
    }

    // ==========================================================================
    // StopAsync
    // ==========================================================================

    [Fact]
    public async Task StopAsync_AlwaysCompletesSuccessfully()
    {
        var sut = new TestFixture().Build();
        var act = async () => await sut.StopAsync(TestContext.Current.CancellationToken);
        await act.Should().NotThrowAsync();
    }
}
