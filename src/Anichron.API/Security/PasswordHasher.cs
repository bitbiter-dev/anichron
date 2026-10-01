using Anichron.API.Settings;
using Konscious.Security.Cryptography;
using System.Security.Cryptography;
using System.Text;

namespace Anichron.API.Security;

public interface IPasswordHasher
{
    string Hash(string password);

    // storedHash is null when no such account exists. A null hash still costs a full Argon2
    // pass, so verification itself takes the same time either way. That removes the HASHING
    // asymmetry only — it does not make a caller's whole code path constant-time.
    bool Verify(string password, string? storedHash);
}

public sealed class Argon2PasswordHasher : IPasswordHasher
{
    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(AppDefaults.Argon2.SaltLength);
        var hash = RunArgon2idSecure(password, salt);

        var combined = new byte[salt.Length + hash.Length];
        salt.CopyTo(combined, 0);
        hash.CopyTo(combined, salt.Length);
        return Convert.ToBase64String(combined);
    }

    public bool Verify(string password, string? storedHash)
    {
        // ⛔ Equal work, not an early return. Verifying a missing account must cost the same
        // Argon2 pass as verifying a real one, so this method leaks nothing about account
        // existence. ⚠️ Whether the CALLER leaks it is the caller's problem — AuthService.Login
        // still does a database write for a known user that it skips for an unknown one.
        //
        // The invariant lives here because this class owns the cost. It used to live in an
        // AuthService field initializer, which — AuthService being Scoped — burned 64 MiB and
        // three Argon2 passes on every request that resolved IAuthService, including ones that
        // never read the value. See #173.
        //
        // ⚠️ `return false` unconditionally, rather than comparing against random bytes:
        // correctness must not rest on two random values differing. The FixedTimeEquals of the
        // real path is deliberately not mirrored here — it is microseconds against ~100 ms of
        // Argon2, so adding it back would buy nothing and reintroduce that dependency.
        if (storedHash is null)
        {
            var throwawaySalt = RandomNumberGenerator.GetBytes(AppDefaults.Argon2.SaltLength);
            CryptographicOperations.ZeroMemory(RunArgon2idSecure(password, throwawaySalt));
            return false;
        }

        var combined = Convert.FromBase64String(storedHash);
        var salt = combined[..AppDefaults.Argon2.SaltLength];
        var expected = combined[AppDefaults.Argon2.SaltLength..];
        var actual = RunArgon2idSecure(password, salt);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] RunArgon2idSecure(string password, byte[] salt)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            return RunArgon2id(passwordBytes, salt);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    private static byte[] RunArgon2id(byte[] password, byte[] salt)
    {
        using var argon2 = new Argon2id(password)
        {
            Salt = salt,
            DegreeOfParallelism = AppDefaults.Argon2.Parallelism,
            Iterations = AppDefaults.Argon2.Iterations,
            MemorySize = AppDefaults.Argon2.MemoryKiB,
        };
        return argon2.GetBytes(AppDefaults.Argon2.HashLength);
    }
}
