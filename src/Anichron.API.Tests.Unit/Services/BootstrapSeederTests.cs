using Anichron.API.Security;
using Anichron.API.Services;
using Anichron.API.Settings;
using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Testing.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Anichron.API.Tests.Unit.Services;

public sealed class BootstrapSeederTests
{
    private const string PasswordSetting = "BOOTSTRAP_ADMIN_PASSWORD";
    private const string ValidPassword = "first-run passphrase";

    private sealed class TestFixture
    {
        public IUserRepository Users { get; } = Substitute.For<IUserRepository>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        private readonly IGuidFactory _guidFactory = Substitute.For<IGuidFactory>();
        public IPasswordHasher PasswordHasher { get; } = Substitute.For<IPasswordHasher>();
        private readonly IConfiguration _configuration = Substitute.For<IConfiguration>();
        private readonly ILogger<BootstrapSeeder> _logger = new FakeLogger<BootstrapSeeder>();
        public IPwnedPasswordClient Pwned { get; } = Substitute.For<IPwnedPasswordClient>();

        public TestFixture()
        {
            _guidFactory.NewGuid().Returns(Guid.Parse("00000000-0000-0000-0000-000000000001"));
            PasswordHasher.Hash(Arg.Any<string>()).Returns("hashed_password");
            _configuration[Arg.Any<string>()].Returns((string?)null);
            _configuration[PasswordSetting].Returns(ValidPassword);
        }

        public TestFixture WithoutPassword()
        {
            _configuration[PasswordSetting].Returns((string?)null);
            return this;
        }

        public TestFixture WithPwned(string password)
        {
            Pwned.IsPwnedAsync(password, Arg.Any<CancellationToken>()).Returns(true);
            return this;
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
            Users, UnitOfWork, _guidFactory, PasswordHasher,
            new RegistrationValidator(Options.Create(new PasswordPolicy()), Options.Create(new UsernamePolicy()), Pwned),
            _configuration, _logger);
    }

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
    public async Task SeedAsync_NoUsers_UsesDefaultUsernameAndConfiguredPassword()
    {
        User? captured = null;
        var fixture = new TestFixture().CaptureAddedUser(u => captured = u);
        var testee = fixture.CreateTestee();

        await testee.SeedAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            captured!.Username.Should().Be("admin");
            captured.PasswordHash.Should().Be("hashed_password");
        });
        fixture.PasswordHasher.Received(1).Hash(ValidPassword);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SeedAsync_NoUsers_NoConfiguredPassword_RefusesToCreateTheAdmin(string? configured)
    {
        var fixture = new TestFixture().WithoutPassword();
        if (configured is not null)
            fixture.WithConfig(PasswordSetting, configured);

        var act = async () => await fixture.CreateTestee().SeedAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("BOOTSTRAP_ADMIN_PASSWORD must be set to create the bootstrap admin.");
        fixture.Users.DidNotReceive().Add(Arg.Any<User>());
        await fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("elevenchars", "PasswordTooShort")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "PasswordTooLong")]
    [InlineData("correct horse battery", "PasswordPwned")]
    public async Task SeedAsync_NoUsers_PasswordBreaksThePolicy_RefusesToCreateTheAdmin(string password, string rule)
    {
        var fixture = new TestFixture().WithConfig(PasswordSetting, password).WithPwned("correct horse battery");

        var act = async () => await fixture.CreateTestee().SeedAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"BOOTSTRAP_ADMIN_PASSWORD does not meet the password policy: {rule}.");
        fixture.Users.DidNotReceive().Add(Arg.Any<User>());
        await fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("weak")]
    public async Task SeedAsync_UsersAlreadyExist_NeitherRequiresNorChecksThePassword(string? configured)
    {
        var fixture = new TestFixture().WithUsersExist().WithoutPassword();
        if (configured is not null)
            fixture.WithConfig(PasswordSetting, configured);

        var act = async () => await fixture.CreateTestee().SeedAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        await fixture.Pwned.DidNotReceive().IsPwnedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_NoUsers_UsesConfiguredUsernameAndPassword()
    {
        User? captured = null;
        var fixture = new TestFixture()
            .WithConfig("BOOTSTRAP_ADMIN_USERNAME", "superadmin")
            .WithConfig("BOOTSTRAP_ADMIN_PASSWORD", "Secure passphrase!")
            .CaptureAddedUser(u => captured = u);
        var testee = fixture.CreateTestee();

        await testee.SeedAsync(CancellationToken.None);

        captured!.Username.Should().Be("superadmin");
        fixture.PasswordHasher.Received(1).Hash("Secure passphrase!");
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
    public async Task SeedAsync_EmptyStringUsernameConfig_UsesDefaultUsername()
    {
        User? captured = null;
        var fixture = new TestFixture()
            .WithConfig("BOOTSTRAP_ADMIN_USERNAME", string.Empty)
            .CaptureAddedUser(u => captured = u);
        var testee = fixture.CreateTestee();

        await testee.SeedAsync(CancellationToken.None);

        captured!.Username.Should().Be(AppDefaults.Startup.AdminDefaultUsername);
    }

    [Fact]
    public async Task SeedAsync_WhitespaceUsernameConfig_UsesDefaultUsername()
    {
        User? captured = null;
        var fixture = new TestFixture()
            .WithConfig("BOOTSTRAP_ADMIN_USERNAME", "   ")
            .CaptureAddedUser(u => captured = u);
        var testee = fixture.CreateTestee();

        await testee.SeedAsync(CancellationToken.None);

        captured!.Username.Should().Be(AppDefaults.Startup.AdminDefaultUsername);
    }

    [Fact]
    public async Task SeedAsync_ConcurrentSeed_SwallowsUniqueViolation()
    {
        var fixture = new TestFixture().WithSaveChangesThrows(DatabaseErrors.UniqueViolation());
        var testee = fixture.CreateTestee();

        var act = async () => await testee.SeedAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SeedAsync_OtherDbException_Propagates()
    {
        var fixture = new TestFixture().WithSaveChangesThrows(DatabaseErrors.Violation(PostgresErrorCodes.IntegrityConstraintViolation));
        var testee = fixture.CreateTestee();

        var act = async () => await testee.SeedAsync(CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
