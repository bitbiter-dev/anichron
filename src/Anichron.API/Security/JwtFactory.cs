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
    private readonly JwtSettings _settings;
    private readonly IClock _clock;
    private readonly IGuidFactory _guidFactory;
    private readonly SigningCredentials _credentials;
    private static readonly JwtSecurityTokenHandler TokenHandler = new();

    public JwtFactory(IOptions<JwtSettings> options, IClock clock, IGuidFactory guidFactory)
    {
        _settings = options.Value;
        _clock = clock;
        _guidFactory = guidFactory;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret));
        _credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    public string Create(User user)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.Username),
            new(JwtRegisteredClaimNames.Jti, _guidFactory.NewGuid().ToString()),
        };

        if (user.MustChangePassword)
            claims.Add(new Claim(AppClaimTypes.MustChangePassword, "true"));

        if (user.IsAdmin)
            claims.Add(new Claim(AppClaimTypes.IsAdmin, "true"));

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: _clock.GetCurrentInstant().ToDateTimeUtc().AddMinutes(_settings.AccessTokenMinutes),
            signingCredentials: _credentials);

        return TokenHandler.WriteToken(token);
    }
}
