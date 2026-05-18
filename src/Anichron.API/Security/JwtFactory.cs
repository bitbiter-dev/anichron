using Anichron.API.Settings;
using Anichron.Core.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Anichron.API.Security;

public interface IJwtFactory
{
    string Create(User user);
}

public sealed class JwtFactory : IJwtFactory
{
    private readonly JwtSettings settings;
    private readonly IClock clock;
    private readonly IGuidFactory guidFactory;
    private readonly SigningCredentials credentials;
    private static readonly JwtSecurityTokenHandler tokenHandler = new();

    public JwtFactory(IOptions<JwtSettings> options, IClock clock, IGuidFactory guidFactory)
    {
        settings = options.Value;
        this.clock = clock;
        this.guidFactory = guidFactory;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Secret));
        credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    public string Create(User user)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.Username),
            new(JwtRegisteredClaimNames.Jti, guidFactory.NewGuid().ToString()),
        };

        if (user.MustChangePassword)
            claims.Add(new Claim(AppClaimTypes.MustChangePassword, "true"));

        if (user.IsAdmin)
            claims.Add(new Claim(AppClaimTypes.IsAdmin, "true"));

        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims: claims,
            expires: clock.GetCurrentInstant().ToDateTimeUtc().AddMinutes(settings.AccessTokenMinutes),
            signingCredentials: credentials);

        return tokenHandler.WriteToken(token);
    }
}
