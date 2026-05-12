using Anichron.API.Security;
using Anichron.API.Services;
using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anichron.API.Tests.Unit.Services;

public sealed class AuthServiceTests
{
    private sealed class TestFixture
    {
        internal readonly IUserRepository Users = Substitute.For<IUserRepository>();
        private readonly IInviteRepository _invites = Substitute.For<IInviteRepository>();
        private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
        private readonly IClock _clock = Substitute.For<IClock>();
        private readonly IGuidFactory _guidFactory = Substitute.For<IGuidFactory>();
        private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
        private readonly IRegistrationValidator _validator = Substitute.For<IRegistrationValidator>();

        internal readonly ITokenService TokenService = Substitute.For<ITokenService>();

        public TestFixture()
        {
            _guidFactory.NewGuid().Returns(Guid.Empty);
            _passwordHasher.Hash(Arg.Any<string>()).Returns("hashed_value");
            _clock.GetCurrentInstant().Returns(Instant.FromUtc(2026, 1, 1, 12, 0, 0));
            TokenService.IssueAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
                .Returns(new AuthTokens("access_token", "refresh_token"));
            _unitOfWork
                .ExecuteInTransactionAsync(Arg.Any<Func<Task<AuthTokens>>>(), Arg.Any<CancellationToken>())
                .Returns(callInfo => callInfo.Arg<Func<Task<AuthTokens>>>()());
            _unitOfWork
                .ExecuteInTransactionAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>())
                .Returns(callInfo => callInfo.Arg<Func<Task>>()());
            // Default: return a valid invite so existing register tests are unaffected
            _invites.FindValidByHashAsync(Arg.Any<string>(), Arg.Any<Instant>(), Arg.Any<CancellationToken>())
                .Returns(new Invite { Id = Guid.NewGuid(), CreatedByUserId = Guid.NewGuid() });
        }

        public TestFixture WithNoValidInvite()
        {
            _invites.FindValidByHashAsync(Arg.Any<string>(), Arg.Any<Instant>(), Arg.Any<CancellationToken>())
                .Returns((Invite?)null);
            return this;
        }

        public TestFixture WithValidInvite(Invite invite)
        {
            _invites.FindValidByHashAsync(Arg.Any<string>(), Arg.Any<Instant>(), Arg.Any<CancellationToken>())
                .Returns(invite);
            return this;
        }

        public TestFixture WithPasswordValid()
        {
            _passwordHasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
            return this;
        }

        public TestFixture WithUser(User user)
        {
            Users.FindByCredentialAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);
            return this;
        }

        public TestFixture WithUsernameExists()
        {
            Users.AnyByUsernameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
            return this;
        }

        public TestFixture WithEmailExists()
        {
            Users.AnyByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
            return this;
        }

        public TestFixture WithNormalizedUsernameExists(string normalizedUsername)
        {
            Users.AnyByUsernameAsync(normalizedUsername, Arg.Any<CancellationToken>()).Returns(true);
            return this;
        }

        public TestFixture WithValidationError(AuthError error)
        {
            _validator
                .ValidateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns((AuthError?)error);
            return this;
        }

        public TestFixture WithSaveChangesThrows(Exception exception)
        {
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromException<int>(exception));
            return this;
        }

        public TestFixture WithTokenIssued(AuthTokens tokens)
        {
            TokenService.IssueAsync(Arg.Any<User>(), Arg.Any<CancellationToken>()).Returns(tokens);
            return this;
        }

        public TestFixture WithTokenRefresh(string rawToken, AuthResult<AuthTokens> result)
        {
            TokenService.RefreshAsync(rawToken, Arg.Any<CancellationToken>()).Returns(result);
            return this;
        }

        public TestFixture WithUserById(Guid id, User? user)
        {
            Users.FindByIdAsync(id, Arg.Any<CancellationToken>()).Returns(user);
            return this;
        }

        public TestFixture WithPasswordValidationError(AuthError error)
        {
            _validator
                .ValidatePasswordAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns((AuthError?)error);
            return this;
        }

        public AuthService CreateTestee() => new(
            Users, _invites, _unitOfWork, _clock, _guidFactory, _passwordHasher, _validator, TokenService);
    }

    // ConstraintName has no setter in Npgsql 10 — must use the full constructor.
    private static DbUpdateException UniqueViolation(string? constraintName = null)
        => new("duplicate key", new PostgresException(
            "duplicate key", "ERROR", "ERROR", "23505",
            null, null, 0, 0, null, null, null, null, null, null,
            constraintName, null, null, null));

    // ==========================================================================
    // RegisterAsync
    // ==========================================================================

    [Fact]
    public async Task RegisterAsync_ValidInput_ReturnsOkWithTokens()
    {
        var fixture = new TestFixture()
            .WithTokenIssued(new AuthTokens("acc", "ref"));
        var testee = fixture.CreateTestee();

        var result = await testee.RegisterAsync("alice", "alice@example.com", "password", "invite_token", CancellationToken.None);

        Assert.Multiple(() =>
        {
            result.IsSuccess.Should().BeTrue();
            result.Value!.AccessToken.Should().Be("acc");
            result.Value.RefreshToken.Should().Be("ref");
        });
    }

    [Fact]
    public async Task RegisterAsync_NullUsername_ThrowsArgumentNullException()
    {
        var testee = new TestFixture().CreateTestee();

        var act = async () => await testee.RegisterAsync(null!, "alice@example.com", "password", "invite_token", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("username");
    }

    [Fact]
    public async Task RegisterAsync_NullEmail_ThrowsArgumentNullException()
    {
        var testee = new TestFixture().CreateTestee();

        var act = async () => await testee.RegisterAsync("alice", null!, "password", "invite_token", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("email");
    }

    [Fact]
    public async Task RegisterAsync_NullPassword_ThrowsArgumentNullException()
    {
        var testee = new TestFixture().CreateTestee();

        var act = async () => await testee.RegisterAsync("alice", "alice@example.com", null!, "invite_token", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("password");
    }

    [Theory]
    [InlineData(AuthError.PasswordTooShort)]
    [InlineData(AuthError.PasswordTooLong)]
    [InlineData(AuthError.PasswordPwned)]
    [InlineData(AuthError.InvalidUsername)]
    [InlineData(AuthError.InvalidEmail)]
    public async Task RegisterAsync_ValidationFails_ReturnsValidationError(AuthError validationError)
    {
        var fixture = new TestFixture()
            .WithValidationError(validationError);
        var testee = fixture.CreateTestee();

        var result = await testee.RegisterAsync("alice", "alice@example.com", "password", "invite_token", CancellationToken.None);

        Assert.Multiple(() =>
        {
            result.IsSuccess.Should().BeFalse();
            result.Error.Should().Be(validationError);
        });
    }

    [Fact]
    public async Task RegisterAsync_DuplicateUsername_ReturnsUsernameTaken()
    {
        var fixture = new TestFixture()
            .WithUsernameExists();
        var testee = fixture.CreateTestee();

        var result = await testee.RegisterAsync("alice", "alice@example.com", "password", "invite_token", CancellationToken.None);

        Assert.Multiple(() =>
        {
            result.IsSuccess.Should().BeFalse();
            result.Error.Should().Be(AuthError.UsernameTaken);
        });
    }

    [Fact]
    public async Task RegisterAsync_DuplicateEmail_ReturnsEmailTaken()
    {
        var fixture = new TestFixture()
            .WithEmailExists();
        var testee = fixture.CreateTestee();

        var result = await testee.RegisterAsync("alice", "alice@example.com", "password", "invite_token", CancellationToken.None);

        Assert.Multiple(() =>
        {
            result.IsSuccess.Should().BeFalse();
            result.Error.Should().Be(AuthError.EmailTaken);
        });
    }

    [Fact]
    public async Task RegisterAsync_ConcurrentEmailUniqueViolation_ReturnsEmailTaken()
    {
        var fixture = new TestFixture()
            .WithSaveChangesThrows(UniqueViolation("ix_users_email_unique"));
        var testee = fixture.CreateTestee();

        var result = await testee.RegisterAsync("alice", "alice@example.com", "password", "invite_token", CancellationToken.None);

        Assert.Multiple(() =>
        {
            result.IsSuccess.Should().BeFalse();
            result.Error.Should().Be(AuthError.EmailTaken);
        });
    }

    [Fact]
    public async Task RegisterAsync_ConcurrentUsernameUniqueViolation_ReturnsUsernameTaken()
    {
        var fixture = new TestFixture()
            .WithSaveChangesThrows(UniqueViolation("ix_users_username_unique"));
        var testee = fixture.CreateTestee();

        var result = await testee.RegisterAsync("alice", "alice@example.com", "password", "invite_token", CancellationToken.None);

        Assert.Multiple(() =>
        {
            result.IsSuccess.Should().BeFalse();
            result.Error.Should().Be(AuthError.UsernameTaken);
        });
    }

    [Fact]
    public async Task RegisterAsync_ConcurrentViolationWithNullConstraintName_ReturnsUsernameTaken()
    {
        var fixture = new TestFixture()
            .WithSaveChangesThrows(UniqueViolation(null));
        var testee = fixture.CreateTestee();

        var result = await testee.RegisterAsync("alice", "alice@example.com", "password", "invite_token", CancellationToken.None);

        Assert.Multiple(() =>
        {
            result.IsSuccess.Should().BeFalse();
            result.Error.Should().Be(AuthError.UsernameTaken);
        });
    }

    [Fact]
    public async Task RegisterAsync_InputNormalization_NormalizesBeforeChecking()
    {
        // AnyByUsernameAsync only returns true for the exact normalized form.
        // If the service does not trim/lowercase, the check passes and registration proceeds.
        var fixture = new TestFixture()
            .WithNormalizedUsernameExists("alice");
        var testee = fixture.CreateTestee();

        var result = await testee.RegisterAsync("  ALICE  ", "alice@example.com", "password", "invite_token", CancellationToken.None);

        result.Error.Should().Be(AuthError.UsernameTaken);
    }

    [Fact]
    public void HashInviteToken_SameInput_ProducesSameHash()
    {
        var hash1 = AuthService.HashInviteToken("my-token");
        var hash2 = AuthService.HashInviteToken("my-token");

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void HashInviteToken_DifferentInputs_ProduceDifferentHashes()
    {
        var hash1 = AuthService.HashInviteToken("token-a");
        var hash2 = AuthService.HashInviteToken("token-b");

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public async Task RegisterAsync_InvalidInviteToken_ReturnsInviteTokenInvalid()
    {
        var testee = new TestFixture().WithNoValidInvite().CreateTestee();

        var result = await testee.RegisterAsync("alice", "alice@example.com", "password", "bad_token", CancellationToken.None);

        result.Error.Should().Be(AuthError.InviteTokenInvalid);
    }

    [Fact]
    public async Task RegisterAsync_NullInviteToken_ThrowsArgumentNullException()
    {
        var testee = new TestFixture().CreateTestee();

        var act = async () => await testee.RegisterAsync("alice", "alice@example.com", "password", null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("inviteToken");
    }

    [Fact]
    public async Task RegisterAsync_ValidInviteToken_MarksInviteAsUsed()
    {
        var invite = new Invite { Id = Guid.NewGuid(), CreatedByUserId = Guid.NewGuid() };
        var testee = new TestFixture().WithValidInvite(invite).CreateTestee();

        await testee.RegisterAsync("alice", "alice@example.com", "password", "invite_token", CancellationToken.None);

        Assert.Multiple(() =>
        {
            invite.UsedAt.Should().NotBeNull();
            // GuidFactory mock returns Guid.Empty — verifies the FK points to the registered user.
            invite.UsedByUserId.Should().Be(Guid.Empty);
        });
    }

    [Fact]
    public async Task RegisterAsync_EmptyInviteToken_ReturnsInviteTokenInvalid()
    {
        var testee = new TestFixture().WithNoValidInvite().CreateTestee();

        var result = await testee.RegisterAsync("alice", "alice@example.com", "password", string.Empty, CancellationToken.None);

        result.Error.Should().Be(AuthError.InviteTokenInvalid);
    }

    [Fact]
    public async Task RegisterAsync_ConcurrentInviteRace_ReturnsInviteTokenInvalid()
    {
        // Simulates xmin concurrency conflict: two requests race to consume the same invite.
        var fixture = new TestFixture()
            .WithSaveChangesThrows(new DbUpdateConcurrencyException("concurrency conflict", (Exception?)null));
        var testee = fixture.CreateTestee();

        var result = await testee.RegisterAsync("alice", "alice@example.com", "password", "invite_token", CancellationToken.None);

        result.Error.Should().Be(AuthError.InviteTokenInvalid);
    }

    // ==========================================================================
    // LoginAsync
    // ==========================================================================

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsOkWithTokens()
    {
        var user = new User { PasswordHash = "hashed_value" };
        var fixture = new TestFixture()
            .WithPasswordValid()
            .WithUser(user)
            .WithTokenIssued(new AuthTokens("acc", "ref"));
        var testee = fixture.CreateTestee();

        var result = await testee.LoginAsync("alice", "correct_password", CancellationToken.None);

        Assert.Multiple(() =>
        {
            result.IsSuccess.Should().BeTrue();
            result.Value!.AccessToken.Should().Be("acc");
            result.Value.RefreshToken.Should().Be("ref");
        });
    }

    [Fact]
    public async Task LoginAsync_NullUsernameOrEmail_ThrowsArgumentNullException()
    {
        var testee = new TestFixture().CreateTestee();

        var act = async () => await testee.LoginAsync(null!, "password", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("usernameOrEmail");
    }

    [Fact]
    public async Task LoginAsync_NullPassword_ThrowsArgumentNullException()
    {
        var testee = new TestFixture().CreateTestee();

        var act = async () => await testee.LoginAsync("alice", null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("password");
    }

    [Fact]
    public async Task LoginAsync_WhitespaceCredential_ReturnsInvalidCredentials()
    {
        var testee = new TestFixture().CreateTestee();

        var result = await testee.LoginAsync("   ", "password", CancellationToken.None);

        result.Error.Should().Be(AuthError.InvalidCredentials);
    }

    [Fact]
    public async Task LoginAsync_UnknownUser_ReturnsInvalidCredentials()
    {
        var testee = new TestFixture().CreateTestee();

        var result = await testee.LoginAsync("ghost", "password", CancellationToken.None);

        Assert.Multiple(() =>
        {
            result.IsSuccess.Should().BeFalse();
            result.Error.Should().Be(AuthError.InvalidCredentials);
        });
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ReturnsInvalidCredentials()
    {
        var user = new User { PasswordHash = "hashed_value" };
        var fixture = new TestFixture()
            .WithUser(user);
        var testee = fixture.CreateTestee();

        var result = await testee.LoginAsync("alice", "wrong_password", CancellationToken.None);

        Assert.Multiple(() =>
        {
            result.IsSuccess.Should().BeFalse();
            result.Error.Should().Be(AuthError.InvalidCredentials);
        });
    }

    [Fact]
    public async Task LoginAsync_AccountDisabled_ReturnsAccountDisabled()
    {
        var user = new User { PasswordHash = "hashed_value", IsDisabled = true };
        var fixture = new TestFixture()
            .WithPasswordValid()
            .WithUser(user);
        var testee = fixture.CreateTestee();

        var result = await testee.LoginAsync("alice", "password", CancellationToken.None);

        Assert.Multiple(() =>
        {
            result.IsSuccess.Should().BeFalse();
            result.Error.Should().Be(AuthError.AccountDisabled);
        });
    }

    [Fact]
    public async Task LoginAsync_AccountLocked_ReturnsLockedWithRetryAfterSeconds()
    {
        // Fixture clock is 2026-01-01T12:00:00Z; lock expires 60 s later.
        var user = new User
        {
            PasswordHash = "hashed_value",
            LockedUntil = Instant.FromUtc(2026, 1, 1, 12, 1, 0),
        };
        var fixture = new TestFixture()
            .WithPasswordValid()
            .WithUser(user);
        var testee = fixture.CreateTestee();

        var result = await testee.LoginAsync("alice", "password", CancellationToken.None);

        Assert.Multiple(() =>
        {
            result.IsSuccess.Should().BeFalse();
            result.Error.Should().Be(AuthError.AccountTemporarilyLocked);
            result.RetryAfterSeconds.Should().Be(60);
        });
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_IncrementsFailedAttemptsAndSetsLockout()
    {
        // 3 existing failures → 4th attempt → backoff = 2^(4-3) = 2 s
        var user = new User { PasswordHash = "hashed_value", FailedLoginAttempts = 3 };
        var fixture = new TestFixture()
            .WithUser(user);
        var testee = fixture.CreateTestee();

        await testee.LoginAsync("alice", "wrong", CancellationToken.None);

        Assert.Multiple(() =>
        {
            user.FailedLoginAttempts.Should().Be(4);
            user.LockedUntil.Should().Be(Instant.FromUtc(2026, 1, 1, 12, 0, 0) + Duration.FromSeconds(2));
        });
    }

    [Fact]
    public async Task LoginAsync_WrongPasswordWithExpiredLockout_IncrementsCounter()
    {
        // LockedUntil is in the past — lockout has expired; counter must still be incremented.
        var user = new User
        {
            PasswordHash = "hashed_value",
            FailedLoginAttempts = 3,
            LockedUntil = Instant.FromUtc(2026, 1, 1, 11, 59, 0), // 60 s before fixed clock
        };
        var fixture = new TestFixture()
            .WithUser(user);
        var testee = fixture.CreateTestee();

        await testee.LoginAsync("alice", "wrong", CancellationToken.None);

        user.FailedLoginAttempts.Should().Be(4);
    }

    [Fact]
    public async Task LoginAsync_ValidCredentialsWithExpiredLockout_ReturnsOk()
    {
        // LockedUntil is set but in the past — should NOT return AccountTemporarilyLocked.
        var user = new User
        {
            PasswordHash = "hashed_value",
            LockedUntil = Instant.FromUtc(2026, 1, 1, 11, 59, 0), // 60 s before fixed clock
        };
        var fixture = new TestFixture()
            .WithPasswordValid()
            .WithUser(user);
        var testee = fixture.CreateTestee();

        var result = await testee.LoginAsync("alice", "password", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task LoginAsync_WrongPasswordDuringActiveLockout_DoesNotIncrementCounter()
    {
        // LockedUntil is in the future → counter must not grow further.
        var user = new User
        {
            PasswordHash = "hashed_value",
            FailedLoginAttempts = 5,
            LockedUntil = Instant.FromUtc(2026, 1, 1, 12, 5, 0),
        };
        var fixture = new TestFixture()
            .WithUser(user);
        var testee = fixture.CreateTestee();

        await testee.LoginAsync("alice", "wrong", CancellationToken.None);

        user.FailedLoginAttempts.Should().Be(5);
    }

    [Fact]
    public async Task LoginAsync_SuccessfulLogin_ResetsFailedLoginAttemptsAndLockout()
    {
        var user = new User { PasswordHash = "hashed_value", FailedLoginAttempts = 3 };
        var fixture = new TestFixture()
            .WithPasswordValid()
            .WithUser(user);
        var testee = fixture.CreateTestee();

        var result = await testee.LoginAsync("alice", "password", CancellationToken.None);

        Assert.Multiple(() =>
        {
            result.IsSuccess.Should().BeTrue();
            user.FailedLoginAttempts.Should().Be(0);
            user.LockedUntil.Should().BeNull();
        });
    }

    // AllowedAttempts=3, MaxAttempts=12, MaxLockoutSeconds=300, BackoffBase=2
    [Theory]
    [InlineData(0, 0)]    // 1st attempt (≤ AllowedAttempts) → no lockout
    [InlineData(2, 0)]    // 3rd attempt (= AllowedAttempts) → no lockout
    [InlineData(3, 2)]    // 4th attempt → 2^(4-3)=2 s
    [InlineData(4, 4)]    // 5th attempt → 2^(5-3)=4 s
    [InlineData(10, 256)] // 11th attempt → 2^(11-3)=256 s
    [InlineData(11, 300)] // 12th attempt (≥ MaxAttempts) → capped at MaxLockoutSeconds
    [InlineData(15, 300)] // beyond MaxAttempts → still capped
    public async Task LoginAsync_ExponentialBackoff_SetsCorrectLockoutDuration(
        int existingAttempts, int expectedBackoffSeconds)
    {
        var user = new User { PasswordHash = "hashed_value", FailedLoginAttempts = existingAttempts };
        var fixture = new TestFixture()
            .WithUser(user);
        var testee = fixture.CreateTestee();

        await testee.LoginAsync("alice", "wrong", CancellationToken.None);

        user.LockedUntil.Should()
            .Be(Instant.FromUtc(2026, 1, 1, 12, 0, 0) + Duration.FromSeconds(expectedBackoffSeconds));
    }

    // ==========================================================================
    // RefreshAsync / RevokeAsync — thin delegation to ITokenService
    // ==========================================================================

    [Fact]
    public async Task RefreshAsync_DelegatesToTokenService_ReturnsItsResult()
    {
        var expected = AuthResult.Ok(new AuthTokens("acc", "ref"));
        var fixture = new TestFixture()
            .WithTokenRefresh("raw_token", expected);
        var testee = fixture.CreateTestee();

        var result = await testee.RefreshAsync("raw_token", CancellationToken.None);

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task RevokeAsync_DelegatesToTokenService()
    {
        var fixture = new TestFixture();
        var testee = fixture.CreateTestee();

        await testee.RevokeAsync("raw_token", CancellationToken.None);

        await fixture.TokenService.Received(1).RevokeAsync("raw_token", Arg.Any<CancellationToken>());
    }

    // ==========================================================================
    // ChangePasswordAsync
    // ==========================================================================

    [Fact]
    public async Task ChangePasswordAsync_NullCurrentPassword_ThrowsArgumentNullException()
    {
        var testee = new TestFixture().CreateTestee();

        var act = async () => await testee.ChangePasswordAsync(Guid.Empty, null!, "new_password", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("currentPassword");
    }

    [Fact]
    public async Task ChangePasswordAsync_NullNewPassword_ThrowsArgumentNullException()
    {
        var testee = new TestFixture().CreateTestee();

        var act = async () => await testee.ChangePasswordAsync(Guid.Empty, "current_password", null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("newPassword");
    }

    [Fact]
    public async Task ChangePasswordAsync_UserNotFound_ReturnsInvalidCredentials()
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var fixture = new TestFixture().WithUserById(userId, null);
        var testee = fixture.CreateTestee();

        var result = await testee.ChangePasswordAsync(userId, "current_password", "new_password", CancellationToken.None);

        Assert.Multiple(() =>
        {
            result.IsSuccess.Should().BeFalse();
            result.Error.Should().Be(AuthError.InvalidCredentials);
        });
    }

    [Fact]
    public async Task ChangePasswordAsync_WrongCurrentPassword_ReturnsInvalidCredentials()
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var user = new User { Id = userId, PasswordHash = "stored_hash" };
        var fixture = new TestFixture().WithUserById(userId, user);
        var testee = fixture.CreateTestee();

        var result = await testee.ChangePasswordAsync(userId, "wrong_password", "new_password", CancellationToken.None);

        Assert.Multiple(() =>
        {
            result.IsSuccess.Should().BeFalse();
            result.Error.Should().Be(AuthError.InvalidCredentials);
        });
    }

    [Theory]
    [InlineData(AuthError.PasswordTooShort)]
    [InlineData(AuthError.PasswordTooLong)]
    [InlineData(AuthError.PasswordPwned)]
    public async Task ChangePasswordAsync_NewPasswordFailsValidation_ReturnsValidationError(AuthError validationError)
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var user = new User { Id = userId, PasswordHash = "stored_hash" };
        var fixture = new TestFixture()
            .WithUserById(userId, user)
            .WithPasswordValid()
            .WithPasswordValidationError(validationError);
        var testee = fixture.CreateTestee();

        var result = await testee.ChangePasswordAsync(userId, "current_password", "weak", CancellationToken.None);

        Assert.Multiple(() =>
        {
            result.IsSuccess.Should().BeFalse();
            result.Error.Should().Be(validationError);
        });
    }

    [Fact]
    public async Task ChangePasswordAsync_ValidPasswords_ReturnsOk()
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var user = new User { Id = userId, PasswordHash = "stored_hash" };
        var fixture = new TestFixture()
            .WithUserById(userId, user)
            .WithPasswordValid();
        var testee = fixture.CreateTestee();

        var result = await testee.ChangePasswordAsync(userId, "current_password", "new_password", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ChangePasswordAsync_ValidPasswords_UpdatesPasswordHashAndClearsMustChangePassword()
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var user = new User { Id = userId, PasswordHash = "stored_hash", MustChangePassword = true };
        var fixture = new TestFixture()
            .WithUserById(userId, user)
            .WithPasswordValid();
        var testee = fixture.CreateTestee();

        await testee.ChangePasswordAsync(userId, "current_password", "new_password", CancellationToken.None);

        Assert.Multiple(() =>
        {
            user.PasswordHash.Should().Be("hashed_value");
            user.MustChangePassword.Should().BeFalse();
        });
    }

    [Fact]
    public async Task ChangePasswordAsync_ValidPasswords_RevokesAllSessions()
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var user = new User { Id = userId, PasswordHash = "stored_hash" };
        var fixture = new TestFixture()
            .WithUserById(userId, user)
            .WithPasswordValid();
        var testee = fixture.CreateTestee();

        await testee.ChangePasswordAsync(userId, "current_password", "new_password", CancellationToken.None);

        await fixture.TokenService.Received(1)
            .MarkAllSessionsRevokedAsync(userId, Arg.Any<Instant>(), Arg.Any<CancellationToken>());
    }

    // ==========================================================================
    // AdminCreateUserAsync
    // ==========================================================================

    [Fact]
    public async Task AdminCreateUserAsync_UsernameTaken_ReturnsUsernameTakenError()
    {
        var testee = new TestFixture().WithUsernameExists().CreateTestee();

        var result = await testee.AdminCreateUserAsync("alice", "alice@example.com", CancellationToken.None);

        result.Error.Should().Be(AuthError.UsernameTaken);
    }

    [Fact]
    public async Task AdminCreateUserAsync_EmailTaken_ReturnsEmailTakenError()
    {
        var testee = new TestFixture().WithEmailExists().CreateTestee();

        var result = await testee.AdminCreateUserAsync("alice", "alice@example.com", CancellationToken.None);

        result.Error.Should().Be(AuthError.EmailTaken);
    }

    [Fact]
    public async Task AdminCreateUserAsync_Success_IsSuccessAndTemporaryPasswordNonEmpty()
    {
        var testee = new TestFixture().CreateTestee();

        var result = await testee.AdminCreateUserAsync("alice", "alice@example.com", CancellationToken.None);

        Assert.Multiple(() =>
        {
            result.IsSuccess.Should().BeTrue();
            result.Value!.TemporaryPassword.Should().NotBeNullOrEmpty();
        });
    }

    [Fact]
    public async Task AdminCreateUserAsync_Success_UserAddedWithMustChangePasswordTrue()
    {
        User? capturedUser = null;
        var fixture = new TestFixture();
        fixture.Users.When(r => r.Add(Arg.Any<User>())).Do(c => capturedUser = c.Arg<User>());
        var testee = fixture.CreateTestee();

        await testee.AdminCreateUserAsync("alice", "alice@example.com", CancellationToken.None);

        capturedUser.Should().NotBeNull();
        capturedUser.MustChangePassword.Should().BeTrue();
    }

    [Fact]
    public async Task AdminCreateUserAsync_Success_PasswordIsHashedBeforeStorage()
    {
        User? capturedUser = null;
        var fixture = new TestFixture();
        fixture.Users.When(r => r.Add(Arg.Any<User>())).Do(c => capturedUser = c.Arg<User>());
        var testee = fixture.CreateTestee();

        var result = await testee.AdminCreateUserAsync("alice", "alice@example.com", CancellationToken.None);

        Assert.Multiple(() =>
        {
            capturedUser!.PasswordHash.Should().Be("hashed_value");
            capturedUser.PasswordHash.Should().NotBe(result.Value!.TemporaryPassword);
        });
    }

    [Fact]
    public async Task AdminCreateUserAsync_Success_NormalizesUsernameAndEmail()
    {
        var testee = new TestFixture().CreateTestee();

        var result = await testee.AdminCreateUserAsync("  Alice  ", "  Alice@Example.COM  ", CancellationToken.None);

        Assert.Multiple(() =>
        {
            result.Value!.Username.Should().Be("alice");
            result.Value.Email.Should().Be("alice@example.com");
        });
    }

    [Fact]
    public async Task AdminCreateUserAsync_DbUniqueViolationOnEmail_ReturnsEmailTakenError()
    {
        var testee = new TestFixture()
            .WithSaveChangesThrows(UniqueViolation("ix_users_email"))
            .CreateTestee();

        var result = await testee.AdminCreateUserAsync("alice", "alice@example.com", CancellationToken.None);

        result.Error.Should().Be(AuthError.EmailTaken);
    }

    [Fact]
    public async Task AdminCreateUserAsync_DbUniqueViolationOnUsername_ReturnsUsernameTakenError()
    {
        var testee = new TestFixture()
            .WithSaveChangesThrows(UniqueViolation("ix_users_username"))
            .CreateTestee();

        var result = await testee.AdminCreateUserAsync("alice", "alice@example.com", CancellationToken.None);

        result.Error.Should().Be(AuthError.UsernameTaken);
    }

    [Fact]
    public async Task AdminCreateUserAsync_NullUsername_ThrowsArgumentNullException()
    {
        var testee = new TestFixture().CreateTestee();

        var act = async () => await testee.AdminCreateUserAsync(null!, "alice@example.com", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("username");
    }

    [Fact]
    public async Task AdminCreateUserAsync_NullEmail_ThrowsArgumentNullException()
    {
        var testee = new TestFixture().CreateTestee();

        var act = async () => await testee.AdminCreateUserAsync("alice", null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("email");
    }
}
