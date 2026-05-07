using Anichron.API.Security;

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
}
