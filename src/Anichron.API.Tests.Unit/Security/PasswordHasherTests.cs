using Anichron.API.Security;
using System.Diagnostics;

namespace Anichron.API.Tests.Unit.Security;

public sealed class PasswordHasherTests
{
    // Argon2id("password", salt=all-zeros-16-bytes) with production parameters
    // (Parallelism=4, Iterations=3, MemoryKiB=65536, HashLength=32)
    // Combined Base64(salt‖hash) = 48 bytes → 64 Base64 chars (no padding)
    private const string KnownPasswordHash = "AAAAAAAAAAAAAAAAAAAAAACx7tm+5twGQaUHcX23a2Ug7IduzmzRCSXkOHW1Q1de";

    [Fact]
    public void Hash_ValidPassword_ReturnsBase64StringOfExpectedLength()
    {
        var testee = new Argon2PasswordHasher();

        var result = testee.Hash("password");

        result.Length.Should().Be(64);
    }

    [Fact]
    public void Verify_CorrectPassword_ReturnsTrue()
    {
        var testee = new Argon2PasswordHasher();

        var result = testee.Verify("password", KnownPasswordHash);

        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("wrongpassword")]
    [InlineData("Password")]
    public void Verify_WrongPassword_ReturnsFalse(string password)
    {
        var testee = new Argon2PasswordHasher();

        var result = testee.Verify(password, KnownPasswordHash);

        result.Should().BeFalse();
    }

    [Fact]
    public void Verify_InvalidBase64StoredHash_ThrowsFormatException()
    {
        var testee = new Argon2PasswordHasher();

        var act = () => testee.Verify("password", "not-valid-base64!!!");

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Verify_TruncatedStoredHash_ThrowsException()
    {
        var testee = new Argon2PasswordHasher();

        var act = () => testee.Verify("password", "AA==");

        act.Should().Throw<Exception>();
    }

    [Fact]
    public void Hash_SamePassword_ProducesDifferentHashes()
    {
        var testee = new Argon2PasswordHasher();

        var hash1 = testee.Hash("password");
        var hash2 = testee.Hash("password");

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void Verify_TamperedStoredHash_ReturnsFalse()
    {
        var testee = new Argon2PasswordHasher();

        var result = testee.Verify("password", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");

        result.Should().BeFalse();
    }

    // ==========================================================================
    // Equal work for a user that does not exist — issue #173
    //
    // A null storedHash means "no such account". Verifying it costs the same Argon2 pass as
    // verifying a real one, so this method leaks nothing about account existence.
    //
    // ⚠️ That is a claim about the HASHER, not about login. AuthService.LoginAsync still writes
    // a failed-attempt record for a known user and skips it for an unknown one, so end-to-end
    // login timing remains distinguishable. Scoped deliberately — overstating it here is how the
    // residual leak gets forgotten.
    //
    // The hasher owns the invariant because it owns the cost. It previously lived in an
    // AuthService field initializer, which — AuthService being registered Scoped — spent 64 MiB
    // and three Argon2 passes on *every request that resolved IAuthService*, including /refresh
    // and /logout, which never read the value.
    // ==========================================================================

    [Fact]
    public void Verify_NullStoredHash_ReturnsFalse()
    {
        var testee = new Argon2PasswordHasher();

        var result = testee.Verify("password", null);

        result.Should().BeFalse();
    }

    // ⚠️ Named for what it actually pins: that the null path is not a cheap early return. It
    // does NOT pin "equal work" — it would still pass if the throwaway pass used weaker Argon2
    // parameters, or were replaced by a sleep. Pinning true equality needs a comparison against
    // the real path, and that is the wall-clock-ratio shape that goes flaky on a loaded runner.
    // The guard against weakened parameters is that both paths call the same RunArgon2idSecure.
    [Fact]
    public void Verify_NullStoredHash_DoesNotShortCircuit()
    {
        var testee = new Argon2PasswordHasher();

        var stopwatch = Stopwatch.StartNew();
        testee.Verify("password", null);
        stopwatch.Stop();

        // ⭐ A LOWER bound, deliberately. "At least this slow" cannot fail on a loaded or slow
        // machine — only on an impossibly fast one, and 64 MiB of Argon2id at three iterations
        // does not complete in single-digit milliseconds on any hardware this runs on (measured
        // ~30–80 ms locally). An upper bound, or a ratio against the real-hash path, is the
        // shape that goes flaky on a busy CI runner.
        stopwatch.ElapsedMilliseconds.Should().BeGreaterThan(10,
            "a null hash must still cost an Argon2 pass; returning early would make "
            + "verification time reveal that the account does not exist");
    }
}
