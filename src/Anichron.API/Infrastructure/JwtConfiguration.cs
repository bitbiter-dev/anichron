using System.Text;

namespace Anichron.API.Infrastructure;

internal sealed record JwtConfiguration(string Secret, string Issuer, string Audience)
{
    private const int minSecretBytes = 32;

    internal static JwtConfiguration Read(IConfiguration configuration)
    {
        var secret = configuration["Jwt:Secret"]
                     ?? throw new InvalidOperationException("Jwt:Secret configuration is missing.");
        var issuer = configuration["Jwt:Issuer"]
                     ?? throw new InvalidOperationException("Jwt:Issuer is missing.");
        var audience = configuration["Jwt:Audience"]
                       ?? throw new InvalidOperationException("Jwt:Audience is missing.");

        if (Encoding.UTF8.GetByteCount(secret) < minSecretBytes)
            throw new InvalidOperationException("Jwt:Secret must be at least 32 bytes.");

        return new JwtConfiguration(secret, issuer, audience);
    }
}
