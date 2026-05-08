using Anichron.API.Security;
using Anichron.API.Services;
using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Anichron.API.Tests.Unit.Services;

public sealed class AdminResetServiceTests
{
    private sealed class TestFixture
    {
        public IUserRepository Users { get; } = Substitute.For<IUserRepository>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
        private readonly IConfiguration _configuration = Substitute.For<IConfiguration>();
        private readonly ILogger<AdminResetService> _logger = Substitute.For<ILogger<AdminResetService>>();

        public TestFixture()
        {
            _configuration[Arg.Any<string>()].Returns((string?)null);
            _passwordHasher.Hash(Arg.Any<string>()).Returns("hashed_new_password");
        }

        public TestFixture WithResetPassword(string password)
        {
            _configuration["ADMIN_RESET_PASSWORD"].Returns(password);
            return this;
        }

        public TestFixture WithTargetUsername(string username)
        {
            _configuration["ADMIN_RESET_USERNAME"].Returns(username);
            return this;
        }

        public TestFixture WithAdminByUsername(string username, User? admin)
        {
            Users.FindAdminByUsernameAsync(username, Arg.Any<CancellationToken>()).Returns(admin);
            return this;
        }

        public TestFixture WithAdmins(List<User> admins)
        {
            Users.FindAdminsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(admins);
            return this;
        }

        public AdminResetService CreateTestee() => new(
            Users, UnitOfWork, _passwordHasher, _configuration, _logger);
    }

    // ==========================================================================
    // ResetIfRequestedAsync
    // ==========================================================================

    [Fact]
    public async Task ResetIfRequestedAsync_NoResetPasswordConfigured_ReturnsWithoutAnyDbCalls()
    {
        var fixture = new TestFixture();
        var testee = fixture.CreateTestee();

        await testee.ResetIfRequestedAsync(CancellationToken.None);

        fixture.Users.DidNotReceive().Add(Arg.Any<User>());
        await fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResetIfRequestedAsync_TargetUsernameSet_AdminFound_ResetsPasswordAndSaves()
    {
        var admin = new User { Username = "alice", MustChangePassword = false };
        var fixture = new TestFixture()
            .WithResetPassword("new_password")
            .WithTargetUsername("alice")
            .WithAdminByUsername("alice", admin);
        var testee = fixture.CreateTestee();

        await testee.ResetIfRequestedAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            admin.PasswordHash.Should().Be("hashed_new_password");
            admin.MustChangePassword.Should().BeTrue();
        });
        await fixture.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResetIfRequestedAsync_TargetUsernameSet_AdminNotFound_DoesNotSave()
    {
        var fixture = new TestFixture()
            .WithResetPassword("new_password")
            .WithTargetUsername("ghost")
            .WithAdminByUsername("ghost", null);
        var testee = fixture.CreateTestee();

        await testee.ResetIfRequestedAsync(CancellationToken.None);

        await fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResetIfRequestedAsync_TargetUsernameSet_NormalizesBeforeLookup()
    {
        var admin = new User { Username = "alice" };
        var fixture = new TestFixture()
            .WithResetPassword("new_password")
            .WithTargetUsername("  ALICE  ")
            .WithAdminByUsername("alice", admin);
        var testee = fixture.CreateTestee();

        await testee.ResetIfRequestedAsync(CancellationToken.None);

        await fixture.Users.Received(1)
            .FindAdminByUsernameAsync("alice", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResetIfRequestedAsync_NoTargetUsername_NoAdmins_DoesNotSave()
    {
        var fixture = new TestFixture()
            .WithResetPassword("new_password")
            .WithAdmins([]);
        var testee = fixture.CreateTestee();

        await testee.ResetIfRequestedAsync(CancellationToken.None);

        await fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResetIfRequestedAsync_NoTargetUsername_MultipleAdmins_DoesNotSave()
    {
        var fixture = new TestFixture()
            .WithResetPassword("new_password")
            .WithAdmins([new User(), new User()]);
        var testee = fixture.CreateTestee();

        await testee.ResetIfRequestedAsync(CancellationToken.None);

        await fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResetIfRequestedAsync_NoTargetUsername_SingleAdmin_ResetsPasswordAndSaves()
    {
        var admin = new User { Username = "admin", MustChangePassword = false };
        var fixture = new TestFixture()
            .WithResetPassword("new_password")
            .WithAdmins([admin]);
        var testee = fixture.CreateTestee();

        await testee.ResetIfRequestedAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            admin.PasswordHash.Should().Be("hashed_new_password");
            admin.MustChangePassword.Should().BeTrue();
        });
        await fixture.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
