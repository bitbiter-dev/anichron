using Anichron.API.Settings;
using Konscious.Security.Cryptography;
using System.Security.Cryptography;
using System.Text;

namespace Anichron.API.Security;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string storedHash);
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

    public bool Verify(string password, string storedHash)
    {
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
