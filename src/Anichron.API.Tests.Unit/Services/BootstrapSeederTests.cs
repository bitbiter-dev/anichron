using Anichron.API.Security;
using Anichron.API.Services;
using Anichron.API.Settings;
using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Anichron.API.Tests.Unit.Services;

public sealed class BootstrapSeederTests
{
    private sealed class TestFixture
    {
        public IUserRepository Users { get; } = Substitute.For<IUserRepository>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        private readonly IGuidFactory _guidFactory = Substitute.For<IGuidFactory>();
        private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
        private readonly IConfiguration _configuration = Substitute.For<IConfiguration>();
        private readonly ILogger<BootstrapSeeder> _logger = Substitute.For<ILogger<BootstrapSeeder>>();

        public TestFixture()
        {
            _guidFactory.NewGuid().Returns(Guid.Parse("00000000-0000-0000-0000-000000000001"));
            _passwordHasher.Hash(Arg.Any<string>()).Returns("hashed_password");
            // NSubstitute returns "" for unstubbed string members; force null so the service's
            // null-coalescing fallback to AppDefaults works correctly.
            _configuration[Arg.Any<string>()].Returns((string?)null);
        }

        public TestFixture WithUsersExist()
        {
            Users.AnyAsync(Arg.Any<CancellationToken>()).Returns(true);
            return this;
        }

        public TestFixture WithConfig(string key, string value)
        {
            _configuration[key].Returns(value);
            return this;
        }

        public TestFixture WithSaveChangesThrows(Exception exception)
        {
            UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromException<int>(exception));
            return this;
        }

        public TestFixture CaptureAddedUser(Action<User> capture)
        {
            Users.When(u => u.Add(Arg.Any<User>())).Do(call => capture(call.Arg<User>()));
            return this;
        }

        public BootstrapSeeder CreateTestee() => new(
            Users, UnitOfWork, _guidFactory, _passwordHasher, _configuration, _logger);
    }

    private static DbUpdateException UniqueViolation()
        => new("duplicate key", new PostgresException(
            "duplicate key", "ERROR", "ERROR", "23505",
            null, null, 0, 0, null, null, null, null, null, null,
            null, null, null, null));

    private static DbUpdateException NonUniqueViolation()
        => new("constraint violation", new PostgresException(
            "constraint violation", "ERROR", "ERROR", "23000",
            null, null, 0, 0, null, null, null, null, null, null,
            null, null, null, null));

    // ==========================================================================
    // SeedAsync
    // ==========================================================================

    [Fact]
    public async Task SeedAsync_UsersAlreadyExist_ReturnsWithoutCreatingAdmin()
    {
        var fixture = new TestFixture().WithUsersExist();
        var testee = fixture.CreateTestee();

        await testee.SeedAsync(CancellationToken.None);

        fixture.Users.DidNotReceive().Add(Arg.Any<User>());
        await fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_NoUsers_AddsAdminAndSavesChanges()
    {
        var fixture = new TestFixture();
        var testee = fixture.CreateTestee();

        await testee.SeedAsync(CancellationToken.None);

        fixture.Users.Received(1).Add(Arg.Any<User>());
        await fixture.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_NoUsers_UsesDefaultUsernameAndPasswordWhenNotConfigured()
    {
        User? captured = null;
        var fixture = new TestFixture().CaptureAddedUser(u => captured = u);
        var testee = fixture.CreateTestee();

        await testee.SeedAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            captured!.Username.Should().Be(AppDefaults.Startup.AdminDefaultUsername);
            captured.PasswordHash.Should().Be("hashed_password");
        });
    }

    [Fact]
    public async Task SeedAsync_NoUsers_UsesConfiguredUsernameAndPassword()
    {
        User? captured = null;
        var fixture = new TestFixture()
            .WithConfig("BOOTSTRAP_ADMIN_USERNAME", "superadmin")
            .WithConfig("BOOTSTRAP_ADMIN_PASSWORD", "SecurePass!")
            .CaptureAddedUser(u => captured = u);
        var testee = fixture.CreateTestee();

        await testee.SeedAsync(CancellationToken.None);

        captured!.Username.Should().Be("superadmin");
    }

    [Fact]
    public async Task SeedAsync_NoUsers_NormalizesConfiguredUsername()
    {
        User? captured = null;
        var fixture = new TestFixture()
            .WithConfig("BOOTSTRAP_ADMIN_USERNAME", "  SUPERADMIN  ")
            .CaptureAddedUser(u => captured = u);
        var testee = fixture.CreateTestee();

        await testee.SeedAsync(CancellationToken.None);

        captured!.Username.Should().Be("superadmin");
    }

    [Fact]
    public async Task SeedAsync_NoUsers_SetsAdminPropertiesCorrectly()
    {
        User? captured = null;
        var fixture = new TestFixture().CaptureAddedUser(u => captured = u);
        var testee = fixture.CreateTestee();

        await testee.SeedAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            captured!.Id.Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000001"));
            captured.Email.Should().Be(AppDefaults.Startup.AdminDefaultMail);
            captured.IsAdmin.Should().BeTrue();
            captured.MustChangePassword.Should().BeTrue();
        });
    }

    [Fact]
    public async Task SeedAsync_ConcurrentSeed_SwallowsUniqueViolation()
    {
        var fixture = new TestFixture().WithSaveChangesThrows(UniqueViolation());
        var testee = fixture.CreateTestee();

        var act = async () => await testee.SeedAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SeedAsync_OtherDbException_Propagates()
    {
        var fixture = new TestFixture().WithSaveChangesThrows(NonUniqueViolation());
        var testee = fixture.CreateTestee();

        var act = async () => await testee.SeedAsync(CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
