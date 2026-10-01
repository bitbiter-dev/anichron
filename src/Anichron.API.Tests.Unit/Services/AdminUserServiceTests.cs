using Anichron.API.Services;
using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using NSubstitute.ExceptionExtensions;

namespace Anichron.API.Tests.Unit.Services;

public sealed class AdminUserServiceTests
{
    private static readonly Instant FixedNow = Instant.FromUtc(2026, 5, 12, 10, 0, 0);

    private sealed class TestFixture
    {
        public IUserRepository Users { get; } = Substitute.For<IUserRepository>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public ITokenService TokenService { get; } = Substitute.For<ITokenService>();
        private readonly IClock _clock = Substitute.For<IClock>();

        // Record whether each write ran *while the transaction lambda was executing*, not merely
        // that a transaction was opened somewhere: `Received().ExecuteInTransactionAsync(...)`
        // alone would also pass on code that opened a transaction and wrote outside it.
        public bool RevokedInsideTransaction { get; private set; }
        public bool SavedInsideTransaction { get; private set; }
        private bool insideTransaction;

        public TestFixture()
        {
            _clock.GetCurrentInstant().Returns(FixedNow);

            // The non-generic Func<Task> overload — the one AdminUserService uses. NSubstitute
            // stubs per overload, so stubbing Func<Task<T>> here would leave the lambda
            // silently un-invoked.
            UnitOfWork
                .ExecuteInTransactionAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>())
                .Returns(async callInfo =>
                {
                    insideTransaction = true;
                    try
                    {
                        await callInfo.Arg<Func<Task>>()();
                    }
                    finally
                    {
                        insideTransaction = false;
                    }
                });

            TokenService
                .When(t => t.MarkAllSessionsRevokedAsync(
                    Arg.Any<Guid>(), Arg.Any<Instant>(), Arg.Any<CancellationToken>()))
                .Do(_ => RevokedInsideTransaction = insideTransaction);

            UnitOfWork
                .When(u => u.SaveChangesAsync(Arg.Any<CancellationToken>()))
                .Do(_ => SavedInsideTransaction = insideTransaction);
        }

        public AdminUserService CreateTestee() => new(Users, UnitOfWork, _clock, TokenService);
    }

    // ==========================================================================
    // GetAllAsync
    // ==========================================================================

    [Fact]
    public async Task GetAllAsync_ReturnsAllUsersFromRepository()
    {
        var fixture = new TestFixture();
        var expected = new List<User> { new() { Id = Guid.NewGuid() }, new() { Id = Guid.NewGuid() } };
        fixture.Users.GetAllAsync(Arg.Any<CancellationToken>()).Returns(expected);

        var result = await fixture.CreateTestee().GetAllAsync(CancellationToken.None);

        result.Should().BeSameAs(expected);
    }

    // ==========================================================================
    // GetByIdAsync
    // ==========================================================================

    [Fact]
    public async Task GetByIdAsync_UserFound_ReturnsUser()
    {
        var fixture = new TestFixture();
        var userId = Guid.NewGuid();
        var user = new User { Id = userId };
        fixture.Users.FindByIdWithConfigsAsync(userId, Arg.Any<CancellationToken>()).Returns(user);

        var result = await fixture.CreateTestee().GetByIdAsync(userId, CancellationToken.None);

        result.Should().BeSameAs(user);
    }

    [Fact]
    public async Task GetByIdAsync_UserNotFound_ReturnsNull()
    {
        var fixture = new TestFixture();
        var userId = Guid.NewGuid();
        fixture.Users.FindByIdWithConfigsAsync(userId, Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await fixture.CreateTestee().GetByIdAsync(userId, CancellationToken.None);

        result.Should().BeNull();
    }

    // ==========================================================================
    // UpdateAsync
    // ==========================================================================

    [Fact]
    public async Task UpdateAsync_BothParamsNull_ReturnsUserWithoutSaving()
    {
        var fixture = new TestFixture();
        var user = new User { Id = Guid.NewGuid(), StorageConfigs = [] };
        fixture.Users.FindByIdWithConfigsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await fixture.CreateTestee().UpdateAsync(Guid.NewGuid(), user.Id, isAdmin: null, isDisabled: null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(user);
        await fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        // Nothing is written, so opening a transaction would be cost without purpose.
        await fixture.UnitOfWork.DidNotReceive().ExecuteInTransactionAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_SelfModification_ReturnsCannotModifySelf()
    {
        var id = Guid.NewGuid();
        var fixture = new TestFixture();

        var result = await fixture.CreateTestee().UpdateAsync(id, id, isAdmin: false, isDisabled: null, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(AuthError.CannotModifySelf);
        await fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_UserNotFound_ReturnsUserNotFound()
    {
        var fixture = new TestFixture();
        var targetId = Guid.NewGuid();
        fixture.Users.FindByIdWithConfigsAsync(targetId, Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await fixture.CreateTestee().UpdateAsync(Guid.NewGuid(), targetId, isAdmin: true, isDisabled: null, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(AuthError.UserNotFound);
        await fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await fixture.UnitOfWork.DidNotReceive().ExecuteInTransactionAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_SetIsAdmin_UpdatesUserAndReturnsUpdatedUser()
    {
        var fixture = new TestFixture();
        var user = new User { Id = Guid.NewGuid(), IsAdmin = false, StorageConfigs = [] };
        fixture.Users.FindByIdWithConfigsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await fixture.CreateTestee().UpdateAsync(Guid.NewGuid(), user.Id, isAdmin: true, isDisabled: null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsAdmin.Should().BeTrue();
        await fixture.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_SetIsDisabledTrue_RevokesSessionsAndSaves()
    {
        var fixture = new TestFixture();
        var user = new User { Id = Guid.NewGuid(), IsDisabled = false, StorageConfigs = [] };
        fixture.Users.FindByIdWithConfigsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        await fixture.CreateTestee().UpdateAsync(Guid.NewGuid(), user.Id, isAdmin: null, isDisabled: true, CancellationToken.None);

        await fixture.TokenService.Received(1).MarkAllSessionsRevokedAsync(user.Id, FixedNow, Arg.Any<CancellationToken>());
        await fixture.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await fixture.UnitOfWork.Received(1).ExecuteInTransactionAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>());
        fixture.RevokedInsideTransaction.Should().BeTrue(
            "revoking sessions outside the transaction cannot be rolled back when the save fails");
        fixture.SavedInsideTransaction.Should().BeTrue(
            "the user mutation must commit atomically with the revocation");
    }

    [Fact]
    public async Task UpdateAsync_SetIsDisabledTrue_WhenAlreadyDisabled_DoesNotRevokeSessionsButSaves()
    {
        var fixture = new TestFixture();
        var user = new User { Id = Guid.NewGuid(), IsDisabled = true, StorageConfigs = [] };
        fixture.Users.FindByIdWithConfigsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        await fixture.CreateTestee().UpdateAsync(Guid.NewGuid(), user.Id, isAdmin: null, isDisabled: true, CancellationToken.None);

        await fixture.TokenService.DidNotReceive().MarkAllSessionsRevokedAsync(Arg.Any<Guid>(), Arg.Any<Instant>(), Arg.Any<CancellationToken>());
        await fixture.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_SetIsDisabledFalse_DoesNotRevokeSessionsButSaves()
    {
        var fixture = new TestFixture();
        var user = new User { Id = Guid.NewGuid(), IsDisabled = true, StorageConfigs = [] };
        fixture.Users.FindByIdWithConfigsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        await fixture.CreateTestee().UpdateAsync(Guid.NewGuid(), user.Id, isAdmin: null, isDisabled: false, CancellationToken.None);

        await fixture.TokenService.DidNotReceive().MarkAllSessionsRevokedAsync(Arg.Any<Guid>(), Arg.Any<Instant>(), Arg.Any<CancellationToken>());
        await fixture.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ==========================================================================
    // DeleteAsync
    // ==========================================================================

    [Fact]
    public async Task DeleteAsync_SelfDeletion_ReturnsCannotModifySelf()
    {
        var id = Guid.NewGuid();
        var fixture = new TestFixture();

        var result = await fixture.CreateTestee().DeleteAsync(id, id, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(AuthError.CannotModifySelf);
        fixture.Users.DidNotReceive().Remove(Arg.Any<User>());
        await fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_UserNotFound_ReturnsUserNotFound()
    {
        var fixture = new TestFixture();
        var targetId = Guid.NewGuid();
        fixture.Users.FindByIdAsync(targetId, Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await fixture.CreateTestee().DeleteAsync(Guid.NewGuid(), targetId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(AuthError.UserNotFound);
        fixture.Users.DidNotReceive().Remove(Arg.Any<User>());
        await fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await fixture.UnitOfWork.DidNotReceive().ExecuteInTransactionAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_ValidUser_RevokesSessionsRemovesUserAndSaves()
    {
        var fixture = new TestFixture();
        var user = new User { Id = Guid.NewGuid() };
        fixture.Users.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await fixture.CreateTestee().DeleteAsync(Guid.NewGuid(), user.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await fixture.TokenService.Received(1).MarkAllSessionsRevokedAsync(user.Id, FixedNow, Arg.Any<CancellationToken>());
        fixture.Users.Received(1).Remove(user);
        await fixture.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await fixture.UnitOfWork.Received(1).ExecuteInTransactionAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>());
        fixture.RevokedInsideTransaction.Should().BeTrue();
        fixture.SavedInsideTransaction.Should().BeTrue();
    }

    // ==========================================================================
    // Transactional integrity — issue #172
    //
    // Revoking sessions uses ExecuteUpdateAsync, which bypasses the change tracker and issues
    // SQL immediately. Untransacted, a failing SaveChangesAsync therefore leaves the sessions
    // revoked and the user mutation lost: an admin "disables" an account, the save fails, and
    // the account is still enabled while its sessions are gone.
    // ==========================================================================

    [Fact]
    public async Task UpdateAsync_SaveThrows_TheRevocationRanInsideTheFailedTransaction()
    {
        var fixture = new TestFixture();
        var user = new User { Id = Guid.NewGuid(), IsDisabled = false, StorageConfigs = [] };
        fixture.Users.FindByIdWithConfigsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        fixture.UnitOfWork
            .SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("constraint violation"));

        var act = async () => await fixture.CreateTestee()
            .UpdateAsync(Guid.NewGuid(), user.Id, isAdmin: null, isDisabled: true, CancellationToken.None);

        // The throw propagating is not the interesting part — it did that before the fix too.
        // What matters is that BOTH writes sat inside the transaction that failed, so the
        // rollback covers them.
        await act.Should().ThrowAsync<InvalidOperationException>();
        fixture.RevokedInsideTransaction.Should().BeTrue(
            "otherwise the sessions stay revoked while the user mutation is lost — the #172 defect");
        fixture.SavedInsideTransaction.Should().BeTrue(
            "the save that failed must itself have been inside the transaction");
    }
}
