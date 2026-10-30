using Anichron.API.Endpoints;
using Anichron.API.Infrastructure;
using Anichron.API.Security;
using Anichron.API.Services;
using Anichron.API.Settings;
using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Work;
using Anichron.Infrastructure.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace Anichron.API.Tests.Unit.Infrastructure;

public sealed class ApiServiceRegistrationTests
{
    private const string Secret = "0123456789abcdef0123456789abcdef-registration";

    private static IConfiguration Configuration(Dictionary<string, string?>? extra = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = Secret,
            ["Jwt:Issuer"] = "the-issuer",
            ["Jwt:Audience"] = "the-audience",
        };
        foreach (var (key, value) in extra ?? [])
            values[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static ServiceCollection AuthServices(IConfiguration? configuration = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthServices(configuration ?? Configuration());
        return services;
    }

    // ==========================================================================
    // Service registrations
    // ==========================================================================

    public static TheoryData<Type, Type, ServiceLifetime> TypedRegistrations => new()
    {
        { typeof(IGuidFactory), typeof(TimeOrderedGuidFactory), ServiceLifetime.Singleton },
        { typeof(IJwtFactory), typeof(JwtFactory), ServiceLifetime.Singleton },
        { typeof(IPasswordHasher), typeof(Argon2PasswordHasher), ServiceLifetime.Singleton },
        { typeof(IAuthResponseMapper), typeof(AuthResponseMapper), ServiceLifetime.Singleton },
        { typeof(IUserRepository), typeof(EfUserRepository), ServiceLifetime.Scoped },
        { typeof(IRefreshTokenRepository), typeof(EfRefreshTokenRepository), ServiceLifetime.Scoped },
        { typeof(IInviteRepository), typeof(EfInviteRepository), ServiceLifetime.Scoped },
        { typeof(IUserStorageConfigRepository), typeof(EfUserStorageConfigRepository), ServiceLifetime.Scoped },
        { typeof(IMediaAssetReadRepository), typeof(EfMediaAssetReadRepository), ServiceLifetime.Scoped },
        { typeof(IAssetViewReader), typeof(EfAssetViewReader), ServiceLifetime.Scoped },
        { typeof(IAssetInteractionWriter), typeof(EfAssetInteractionWriter), ServiceLifetime.Scoped },
        { typeof(IProxyStore), typeof(ProxyStore), ServiceLifetime.Singleton },
        { typeof(IOriginalStore), typeof(OriginalStore), ServiceLifetime.Singleton },
        { typeof(IRegistrationValidator), typeof(RegistrationValidator), ServiceLifetime.Scoped },
        { typeof(ILockoutService), typeof(LockoutService), ServiceLifetime.Scoped },
        { typeof(ITokenService), typeof(TokenService), ServiceLifetime.Scoped },
        { typeof(IAuthService), typeof(AuthService), ServiceLifetime.Scoped },
        { typeof(IBootstrapSeeder), typeof(BootstrapSeeder), ServiceLifetime.Transient },
        { typeof(IAdminResetService), typeof(AdminResetService), ServiceLifetime.Scoped },
        { typeof(IAdminUserService), typeof(AdminUserService), ServiceLifetime.Scoped },
        { typeof(IAdminStorageConfigService), typeof(AdminStorageConfigService), ServiceLifetime.Scoped },
        { typeof(IBootstrapResetService), typeof(BootstrapResetService), ServiceLifetime.Transient },
    };

    [Theory]
    [MemberData(nameof(TypedRegistrations))]
    public void AddAuthServices_RegistersTheImplementationWithItsLifetime(Type service, Type implementation, ServiceLifetime lifetime)
        => AuthServices().Should().ContainSingle(d => d.ServiceType == service)
            .Which.Should().Match<ServiceDescriptor>(d => d.ImplementationType == implementation && d.Lifetime == lifetime);

    [Fact]
    public void AddAuthServices_UsesTheSystemClocks()
    {
        using var provider = AuthServices().BuildServiceProvider();

        provider.GetRequiredService<IClock>().Should().BeSameAs(NodaTime.SystemClock.Instance);
        provider.GetRequiredService<TimeProvider>().Should().BeSameAs(TimeProvider.System);
    }

    [Fact]
    public void AddAuthServices_UsesTheServersTimeZone()
    {
        using var provider = AuthServices().BuildServiceProvider();

        provider.GetRequiredService<DateTimeZone>().Should().BeSameAs(DateTimeZoneProviders.Tzdb.GetSystemDefault());
    }

    [Fact]
    public void AddAuthServices_UnitOfWorkIsTheScopedDbContext()
        => AuthServices().Should().ContainSingle(d => d.ServiceType == typeof(IUnitOfWork))
            .Which.Lifetime.Should().Be(ServiceLifetime.Scoped);

    // ==========================================================================
    // Work items
    // ==========================================================================

    private static ServiceCollection WorkItemServices(Dictionary<string, string?>? values = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(values ?? []).Build());
        services.AddWorkItemServices();
        return services;
    }

    [Theory]
    [InlineData(typeof(IWorkItemReader), typeof(EfWorkItemReader))]
    [InlineData(typeof(IWorkItemCanceller), typeof(EfWorkItemCanceller))]
    [InlineData(typeof(WorkItemEvents), typeof(WorkItemEvents))]
    [InlineData(typeof(IWorkQueue), typeof(EfWorkQueue))]
    [InlineData(typeof(IProxyOrphanReader), typeof(EfProxyOrphanReader))]
    public void AddWorkItemServices_RegistersTheScopedImplementation(Type service, Type implementation)
        => WorkItemServices().Should().ContainSingle(d => d.ServiceType == service)
            .Which.Should().Match<ServiceDescriptor>(d => d.ImplementationType == implementation && d.Lifetime == ServiceLifetime.Scoped);

    [Fact]
    public void AddWorkItemServices_EventIntervalDefaultsToTwoSeconds()
    {
        using var provider = WorkItemServices().BuildServiceProvider();

        provider.GetRequiredService<IOptions<WorkItemEventsSettings>>().Value.IntervalSeconds.Should().Be(2);
    }

    [Fact]
    public void AddWorkItemServices_BindsTheEventIntervalFromConfiguration()
    {
        using var provider = WorkItemServices(new() { ["WorkItemEvents:IntervalSeconds"] = "0.5" }).BuildServiceProvider();

        provider.GetRequiredService<IOptions<WorkItemEventsSettings>>().Value.IntervalSeconds.Should().Be(0.5);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void AddWorkItemServices_RejectsAnEventIntervalThatIsNotPositive(string seconds)
    {
        using var provider = WorkItemServices(new() { ["WorkItemEvents:IntervalSeconds"] = seconds }).BuildServiceProvider();

        var read = () => provider.GetRequiredService<IOptions<WorkItemEventsSettings>>().Value;

        read.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void AddWorkItemServices_ValidatesTheEventIntervalAtStartup()
        => WorkItemServices().Should().Contain(d => d.ServiceType == typeof(IStartupValidator));

    // ==========================================================================
    // Options binding
    // ==========================================================================

    [Fact]
    public void AddAuthServices_BindsJwtPasswordAndUsernamePolicies()
    {
        using var provider = AuthServices(Configuration(new()
        {
            ["Jwt:AccessTokenMinutes"] = "7",
            ["PasswordPolicy:MinLength"] = "20",
            ["UsernamePolicy:MaxLength"] = "16",
        })).BuildServiceProvider();

        provider.GetRequiredService<IOptions<JwtSettings>>().Value.AccessTokenMinutes.Should().Be(7);
        provider.GetRequiredService<IOptions<PasswordPolicy>>().Value.MinLength.Should().Be(20);
        provider.GetRequiredService<IOptions<UsernamePolicy>>().Value.MaxLength.Should().Be(16);
    }

    [Fact]
    public void AddAuthServices_RefreshCookieLifetimeComesFromJwtRefreshTokenDays()
    {
        using var provider = AuthServices(Configuration(new() { ["Jwt:RefreshTokenDays"] = "9" })).BuildServiceProvider();

        provider.GetRequiredService<AuthCookieSettings>().RefreshTokenDays.Should().Be(9);
    }

    [Fact]
    public void AddAuthServices_LoginFloorComesFromConfiguration()
    {
        var services = AuthServices(Configuration(new() { ["Login:FailureFloorMilliseconds"] = "250" }));
        var time = new FakeTimeProvider();
        services.AddSingleton<TimeProvider>(time);
        using var provider = services.BuildServiceProvider();
        var floor = provider.GetRequiredService<ILoginResponseFloor>();

        var pad = floor.PadAsync(floor.Start(), CancellationToken.None);
        time.Advance(TimeSpan.FromMilliseconds(249));
        var completedEarly = pad.IsCompleted;
        time.Advance(TimeSpan.FromMilliseconds(1));

        completedEarly.Should().BeFalse();
        pad.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public void AddAuthServices_PwnedClientTargetsTheRangeApiWithPadding()
    {
        using var provider = AuthServices().BuildServiceProvider();

        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(IPwnedPasswordClient));

        client.BaseAddress.Should().Be(new Uri("https://api.pwnedpasswords.com/"));
        client.DefaultRequestHeaders.GetValues("Add-Padding").Should().Equal("true");
    }

    [Fact]
    public void AddAuthServices_PwnedCheckGivesUpAfterFiveSecondsInTotal()
    {
        using var provider = AuthServices().BuildServiceProvider();

        var resilience = provider.GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>()
            .Get($"{nameof(IPwnedPasswordClient)}-standard");

        resilience.TotalRequestTimeout.Timeout.Should().Be(TimeSpan.FromSeconds(5));
        resilience.AttemptTimeout.Timeout.Should().Be(TimeSpan.FromSeconds(5));
    }

    // ==========================================================================
    // Authentication and authorization
    // ==========================================================================

    [Fact]
    public void AddAuthServices_BearerIsTheDefaultScheme()
    {
        using var provider = AuthServices().BuildServiceProvider();

        provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value.DefaultScheme
            .Should().Be(JwtBearerDefaults.AuthenticationScheme);
    }

    [Fact]
    public void AddAuthServices_ValidatesEveryPartOfTheTokenWithNoClockSkew()
    {
        using var provider = AuthServices().BuildServiceProvider();

        var parameters = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters;

        parameters.ValidateIssuerSigningKey.Should().BeTrue();
        parameters.ValidateIssuer.Should().BeTrue();
        parameters.ValidateAudience.Should().BeTrue();
        parameters.ValidateLifetime.Should().BeTrue();
        parameters.ClockSkew.Should().Be(TimeSpan.Zero);
        parameters.ValidIssuer.Should().Be("the-issuer");
        parameters.ValidAudience.Should().Be("the-audience");
        parameters.IssuerSigningKey.Should().BeOfType<SymmetricSecurityKey>()
            .Which.Key.Should().Equal(Encoding.UTF8.GetBytes(Secret));
    }

    [Fact]
    public void AddAuthorizationPolicies_AdminRequiresTheIsAdminClaimSetToTrue()
    {
        var services = new ServiceCollection();
        services.AddAuthorizationPolicies();
        using var provider = services.BuildServiceProvider();

        var policy = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value.GetPolicy(AuthPolicies.Admin);

        policy.Should().NotBeNull();
        policy.Requirements.Should().ContainSingle().Which.Should().BeOfType<ClaimsAuthorizationRequirement>()
            .Which.Should().Match<ClaimsAuthorizationRequirement>(r =>
                r.ClaimType == AppClaimTypes.IsAdmin && r.AllowedValues!.Single() == "true");
    }

    // ==========================================================================
    // Start-up validation
    // ==========================================================================

    private const string ValidSecret = "0123456789abcdef0123456789abcdef";

    private static Action StartAuthServices(Dictionary<string, string?> jwt)
    {
        var provider = AuthServices(Configuration(jwt)).BuildServiceProvider();
        return () =>
        {
            using (provider)
                provider.GetRequiredService<IStartupValidator>().Validate();
        };
    }

    [Fact]
    public void AddAuthServices_RegistersWithoutAnyJwtConfiguration()
    {
        var register = () => AuthServices(new ConfigurationBuilder().Build());

        register.Should().NotThrow();
    }

    [Fact]
    public void AddAuthServices_ValidJwtConfiguration_PassesStartupValidation()
        => StartAuthServices([]).Should().NotThrow();

    [Theory]
    [InlineData("Jwt:Secret", null, "Jwt:Secret configuration is missing.")]
    [InlineData("Jwt:Secret", "", "Jwt:Secret configuration is missing.")]
    [InlineData("Jwt:Issuer", null, "Jwt:Issuer is missing.")]
    [InlineData("Jwt:Issuer", "", "Jwt:Issuer is missing.")]
    [InlineData("Jwt:Audience", null, "Jwt:Audience is missing.")]
    [InlineData("Jwt:Audience", "", "Jwt:Audience is missing.")]
    public void AddAuthServices_MissingJwtSetting_FailsAtStart(string key, string? value, string failure)
        => StartAuthServices(new() { [key] = value })
            .Should().Throw<OptionsValidationException>().Which.Failures.Should().Equal(failure);

    [Fact]
    public void JwtSettingsValidator_CompleteSettings_Succeeds()
        => new JwtSettingsValidator()
            .Validate(null, new JwtSettings { Secret = ValidSecret, Issuer = "iss", Audience = "aud" })
            .Succeeded.Should().BeTrue();

    [Fact]
    public void AddAuthServices_SecretOneByteShortOfMinimum_FailsAtStart()
        => StartAuthServices(new() { ["Jwt:Secret"] = ValidSecret[1..] })
            .Should().Throw<OptionsValidationException>().Which.Failures.Should().Equal("Jwt:Secret must be at least 32 bytes.");

    [Fact]
    public void AddAuthServices_SecretOfExactlyThirtyTwoBytes_PassesStartupValidation()
        => StartAuthServices(new() { ["Jwt:Secret"] = ValidSecret }).Should().NotThrow();

    [Fact]
    public void AddAuthServices_SecretLengthCountedInUtf8Bytes_AcceptsSixteenTwoByteCharacters()
        => StartAuthServices(new() { ["Jwt:Secret"] = new string('é', 16) }).Should().NotThrow();

    [Fact]
    public void AddAuthServices_EveryMissingJwtSetting_IsReportedAtOnce()
        => StartAuthServices(new() { ["Jwt:Secret"] = null, ["Jwt:Issuer"] = null, ["Jwt:Audience"] = null })
            .Should().Throw<OptionsValidationException>().Which.Failures.Should().Equal(
                "Jwt:Secret configuration is missing.", "Jwt:Issuer is missing.", "Jwt:Audience is missing.");

    private static Dictionary<string, string?> DatabaseValues() => new()
    {
        ["POSTGRES_CONNECTION:HOST"] = "db.internal",
        ["POSTGRES_CONNECTION:DBNAME"] = "anichron",
        ["POSTGRES_CONNECTION:USER"] = "app",
        ["POSTGRES_CONNECTION:PASSWORD"] = "pw",
    };

    private static ServiceProvider DatabaseServices(Dictionary<string, string?> values)
    {
        var services = new ServiceCollection();
        services.AddDatabase(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddDatabase_RegistersWithoutAnyConnectionConfiguration()
    {
        var register = () => new ServiceCollection().AddDatabase(new ConfigurationBuilder().Build());

        register.Should().NotThrow();
    }

    [Fact]
    public void AddDatabase_CompleteConnection_PassesStartupValidation()
    {
        using var provider = DatabaseServices(DatabaseValues());

        var start = () => provider.GetRequiredService<IStartupValidator>().Validate();

        start.Should().NotThrow();
    }

    [Fact]
    public void AddDatabase_MissingHost_FailsAtStart()
    {
        var values = DatabaseValues();
        values.Remove("POSTGRES_CONNECTION:HOST");
        using var provider = DatabaseServices(values);

        var start = () => provider.GetRequiredService<IStartupValidator>().Validate();

        start.Should().Throw<InvalidOperationException>().WithMessage("Postgres Host missing");
    }

    // ==========================================================================
    // Database
    // ==========================================================================

    [Fact]
    public void AddDatabase_UsesNpgsqlWithTheConfiguredConnection()
    {
        using var provider = DatabaseServices(DatabaseValues());
        using var scope = provider.CreateScope();

        var database = scope.ServiceProvider.GetRequiredService<AnichronDbContext>().Database;

        database.ProviderName.Should().Be("Npgsql.EntityFrameworkCore.PostgreSQL");
        database.GetConnectionString().Should().Contain("Host=db.internal").And.Contain("Database=anichron");
    }
}
