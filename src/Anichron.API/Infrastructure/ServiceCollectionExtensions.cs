using Anichron.API.Endpoints;
using Anichron.API.Security;
using Anichron.API.Services;
using Anichron.API.Settings;
using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Work;
using Anichron.Infrastructure.Configuration;
using Anichron.Infrastructure.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IO.Abstractions;
using System.Net;
using System.Text;

namespace Anichron.API.Infrastructure;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddApiSurface(IConfiguration configuration)
            => services
                .AddOpenApi(options => options.AddSchemaTransformer<ProblemCodeSchemaTransformer>())
                .AddProblemDetails()
                .AddDatabase(configuration)
                .AddForwardedHeadersSupport(configuration)
                .AddAuthServices(configuration)
                .AddWorkItemServices()
                .AddAuthorizationPolicies()
                .AddRateLimiting(configuration)
                .AddCorsPolicy(configuration)
                .AddApiHealthChecks();

        public IServiceCollection AddDatabase(IConfiguration configuration)
        {
            services.AddOptions<DatabaseConnectionSettings>()
                .Configure(settings => DatabaseConfiguration.Bind(configuration, new FileSystem(), settings))
                .ValidateOnStart();
            services.AddSingleton<IValidateOptions<DatabaseConnectionSettings>, DatabaseSettingsValidator>();
            return services.AddDbContext<AnichronDbContext>((provider, options) =>
                options.UseNpgsql(provider.GetRequiredService<IOptions<DatabaseConnectionSettings>>().Value.ToConnectionString(),
                    o => o.UseNodaTime()));
        }

        public IServiceCollection AddForwardedHeadersSupport(IConfiguration configuration)
        {
            var knownProxies = ConfigurationList.Parse(configuration["ForwardedHeaders:KnownProxies"], IPAddress.Parse);
            var knownNetworks = ConfigurationList.Parse(configuration["ForwardedHeaders:KnownNetworks"], System.Net.IPNetwork.Parse);

            return services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.ForwardLimit = 1;
                foreach (var proxy in knownProxies)
                    options.KnownProxies.Add(proxy);
                foreach (var network in knownNetworks)
                    options.KnownIPNetworks.Add(network);
            });
        }

        public IServiceCollection AddRateLimiting(IConfiguration configuration)
        {
            services.AddOptions<RateLimitSettings>()
                .Bind(configuration.GetSection("RateLimit"))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            services.AddOptions<RateLimiterOptions>().Configure<IOptions<RateLimitSettings>>((options, settings) =>
            {
                options.AddPolicy(AuthRateLimitPolicies.Sensitive, new SlidingWindowPolicy(
                    settings.Value.SensitivePermitLimit,
                    TimeSpan.FromSeconds(AppDefaults.RateLimit.Sensitive.WindowSeconds),
                    AppDefaults.RateLimit.Sensitive.Segments));

                // Refresh tokens rotate on every use. Tighter policy required.
                options.AddPolicy(AuthRateLimitPolicies.Refresh, new SlidingWindowPolicy(
                    AppDefaults.RateLimit.Refresh.PermitLimit,
                    TimeSpan.FromMinutes(AppDefaults.RateLimit.Refresh.WindowMinutes),
                    AppDefaults.RateLimit.Refresh.Segments));
            });

            return services.AddRateLimiter(_ => { });
        }

        public IServiceCollection AddWorkItemServices()
        {
            services.AddOptions<WorkItemEventsSettings>()
                .BindConfiguration("WorkItemEvents")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            services.AddScoped<IWorkItemReader, EfWorkItemReader>();
            services.AddScoped<IWorkItemCanceller, EfWorkItemCanceller>();
            services.AddScoped<IWorkQueue, EfWorkQueue>();
            services.AddScoped<IProxyOrphanReader, EfProxyOrphanReader>();
            services.AddScoped<WorkItemEvents>();
            return services;
        }

        public IServiceCollection AddCorsPolicy(IConfiguration configuration)
        {
            var allowedOrigins = CorsOrigins.Read(configuration);

            // Always register CORS services so UseCors() is valid in the middleware pipeline.
            services.AddCors(options => CorsOrigins.ConfigureDefaultPolicy(options, allowedOrigins));

            return services;
        }

        public IServiceCollection AddAuthServices(IConfiguration configuration)
        {
            services.AddOptions<JwtSettings>()
                .Bind(configuration.GetSection("Jwt"))
                .ValidateOnStart();
            services.AddSingleton<IValidateOptions<JwtSettings>, JwtSettingsValidator>();
            services.Configure<PasswordPolicy>(configuration.GetSection("PasswordPolicy"));
            services.Configure<UsernamePolicy>(configuration.GetSection("UsernamePolicy"));
            services.AddSingleton<IClock>(SystemClock.Instance);
            services.AddSingleton(DateTimeZoneProviders.Tzdb.GetSystemDefault());
            services.AddSingleton<IGuidFactory, TimeOrderedGuidFactory>();
            services.AddSingleton<IJwtFactory, JwtFactory>();
            services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
            services.AddSingleton<IAuthResponseMapper, AuthResponseMapper>();
            services.AddScoped<IUserRepository, EfUserRepository>();
            services.AddScoped<IRefreshTokenRepository, EfRefreshTokenRepository>();
            services.AddScoped<IInviteRepository, EfInviteRepository>();
            services.AddScoped<IUserStorageConfigRepository, EfUserStorageConfigRepository>();
            services.AddScoped<IMediaAssetReadRepository, EfMediaAssetReadRepository>();
            services.AddScoped<IAssetViewReader, EfAssetViewReader>();
            services.AddScoped<IAssetInteractionWriter, EfAssetInteractionWriter>();
            services.AddSingleton<IProxyStore, ProxyStore>();
            services.AddSingleton<IOriginalStore, OriginalStore>();
            // AnichronDbContext is already scoped via AddDbContext; reuse the same instance for IUnitOfWork
            services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AnichronDbContext>());
            services.AddScoped<IRegistrationValidator, RegistrationValidator>();
            services.AddScoped<ILockoutService, LockoutService>();
            // Stryker disable once Statement
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<ILoginResponseFloor>(sp => new LoginResponseFloor(
                sp.GetRequiredService<TimeProvider>(),
                TimeSpan.FromMilliseconds(configuration.GetValue(
                    "Login:FailureFloorMilliseconds", AppDefaults.Login.FailureFloorMilliseconds))));
            services.AddScoped<ITokenService, TokenService>();
            services.AddScoped<IAuthService, AuthService>();
            services.AddTransient<IBootstrapSeeder, BootstrapSeeder>();
            services.AddScoped<IAdminResetService, AdminResetService>();
            services.AddScoped<IAdminUserService, AdminUserService>();
            services.AddScoped<IAdminStorageConfigService, AdminStorageConfigService>();
            services.AddTransient<IBootstrapResetService, BootstrapResetService>();

            services.AddSingleton(new AuthCookieSettings
            {
                SameSite = CorsOrigins.RefreshCookieSameSite(CorsOrigins.Read(configuration)),
                RefreshTokenDays = configuration.GetValue("Jwt:RefreshTokenDays", AppDefaults.Jwt.RefreshTokenDays),
            });

            services.AddHttpClient<IPwnedPasswordClient, PwnedPasswordClient>(client =>
            {
                client.BaseAddress = new Uri(AppDefaults.Pwned.Url);
                client.DefaultRequestHeaders.Add("Add-Padding", "true");
            }).AddStandardResilienceHandler(options =>
            {
                var timeout = TimeSpan.FromSeconds(AppDefaults.Pwned.TimeoutInSeconds);
                options.TotalRequestTimeout.Timeout = timeout;
                options.AttemptTimeout.Timeout = timeout;
            });

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
            services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
                .Configure<IOptions<JwtSettings>>((options, jwt) =>
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Value.Secret)),
                        ValidateIssuer = true,
                        ValidIssuer = jwt.Value.Issuer,
                        ValidateAudience = true,
                        ValidAudience = jwt.Value.Audience,
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.Zero,
                    });

            return services;
        }

        public IServiceCollection AddAuthorizationPolicies()
        {
            return services.AddAuthorization(options =>
                options.AddPolicy(AuthPolicies.Admin,
                    policy => policy.RequireClaim(AppClaimTypes.IsAdmin, "true")));
        }

        public IServiceCollection AddApiHealthChecks()
        {
            services.AddHealthChecks()
                    .AddDbContextCheck<AnichronDbContext>("database")
                    .AddCheck<ProxyStorageHealthCheck>("proxyStorage")
                    .AddCheck<OriginalsStorageHealthCheck>("originalsStorage");
            return services;
        }
    }
}
