using Anichron.API.Security;
using Anichron.API.Services;
using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;

namespace Anichron.API.Tests.Unit.Services;

public sealed class AdminResetServiceTests
{
    private sealed class TestFixture
    {
        public IUserRepository Users { get; } = Substitute.For<IUserRepository>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        private readonly IClock _clock = Substitute.For<IClock>();
        private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
        internal readonly ITokenService TokenService = Substitute.For<ITokenService>();

        public TestFixture()
        {
            _passwordHasher.Hash(Arg.Any<string>()).Returns("hashed_new_password");
            _clock.GetCurrentInstant().Returns(Instant.FromUtc(2026, 1, 1, 12, 0, 0));
            UnitOfWork
                .ExecuteInTransactionAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>())
                .Returns(callInfo => callInfo.Arg<Func<Task>>()());
        }

        public AdminResetService CreateTestee() => new(
            Users, UnitOfWork, _clock, _passwordHasher, TokenService);
    }

    [Fact]
    public async Task ResetUserPasswordAsync_UserNotFound_ReturnsNull()
    {
        var userId = Guid.NewGuid();
        var fixture = new TestFixture();
        fixture.Users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns((User?)null);
        var testee = fixture.CreateTestee();

        var result = await testee.ResetUserPasswordAsync(userId, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResetUserPasswordAsync_UserFound_ReturnsNonEmptyTemporaryPassword()
    {
        var user = new User { Id = Guid.NewGuid() };
        var fixture = new TestFixture();
        fixture.Users.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var testee = fixture.CreateTestee();

        var result = await testee.ResetUserPasswordAsync(user.Id, CancellationToken.None);

        result.Should().NotBeNull();
        result.TemporaryPassword.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ResetUserPasswordAsync_UserFound_SetsMustChangePasswordAndHashesPassword()
    {
        var user = new User { Id = Guid.NewGuid(), MustChangePassword = false };
        var fixture = new TestFixture();
        fixture.Users.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var testee = fixture.CreateTestee();

        var result = await testee.ResetUserPasswordAsync(user.Id, CancellationToken.None);

        Assert.Multiple(() =>
        {
            user.MustChangePassword.Should().BeTrue();
            user.PasswordHash.Should().Be("hashed_new_password");
            user.PasswordHash.Should().NotBe(result!.TemporaryPassword);
        });
    }

    [Fact]
    public async Task ResetUserPasswordAsync_UserFound_RevokesAllTokens()
    {
        var user = new User { Id = Guid.NewGuid() };
        var fixture = new TestFixture();
        fixture.Users.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var testee = fixture.CreateTestee();

        await testee.ResetUserPasswordAsync(user.Id, CancellationToken.None);

        await fixture.TokenService.Received(1)
            .MarkAllSessionsRevokedAsync(user.Id, Arg.Any<Instant>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResetUserPasswordAsync_UserFound_SavesChanges()
    {
        var user = new User { Id = Guid.NewGuid() };
        var fixture = new TestFixture();
        fixture.Users.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var testee = fixture.CreateTestee();

        await testee.ResetUserPasswordAsync(user.Id, CancellationToken.None);

        await fixture.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResetUserPasswordAsync_UserFound_RevokesTokensBeforeSavingChanges()
    {
        var user = new User { Id = Guid.NewGuid() };
        var fixture = new TestFixture();
        fixture.Users.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var callOrder = new List<string>();
        fixture.TokenService
            .When(t => t.MarkAllSessionsRevokedAsync(Arg.Any<Guid>(), Arg.Any<Instant>(), Arg.Any<CancellationToken>()))
            .Do(_ => callOrder.Add("revoke"));
        fixture.UnitOfWork
            .When(u => u.SaveChangesAsync(Arg.Any<CancellationToken>()))
            .Do(_ => callOrder.Add("save"));
        var testee = fixture.CreateTestee();

        await testee.ResetUserPasswordAsync(user.Id, CancellationToken.None);

        callOrder.Should().Equal("revoke", "save");
    }
}
