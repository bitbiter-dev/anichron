using Anichron.API.Endpoints;
using Anichron.API.Security;
using Anichron.API.Services;
using Anichron.API.Settings;
using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Infrastructure.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IO.Abstractions;
using System.Net;
using System.Text;
using System.Threading.RateLimiting;

namespace Anichron.API.Infrastructure;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddDatabase(IConfiguration configuration)
        {
            var connectionString = DatabaseConfiguration.GetConnectionString(configuration, new FileSystem());
            return services.AddDbContext<AnichronDbContext>(options =>
                options.UseNpgsql(connectionString, o => o.UseNodaTime()));
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

        public IServiceCollection AddRateLimiting()
        {
            return services.AddRateLimiter(options =>
            {
                options.OnRejected = RateLimiting.OnRejectedAsync;

                options.AddPolicy(AuthRateLimitPolicies.Sensitive, httpContext =>
                    RateLimitPartition.GetSlidingWindowLimiter(
                        partitionKey: RateLimiting.PartitionKey(httpContext),
                        factory: _ => new SlidingWindowRateLimiterOptions
                        {
                            PermitLimit = AppDefaults.RateLimit.Sensitive.PermitLimit,
                            Window = TimeSpan.FromSeconds(AppDefaults.RateLimit.Sensitive.WindowSeconds),
                            SegmentsPerWindow = AppDefaults.RateLimit.Sensitive.Segments,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            QueueLimit = 0,
                        }));

                // Refresh tokens rotate on every use. Tighter policy required.
                options.AddPolicy(AuthRateLimitPolicies.Refresh, httpContext =>
                    RateLimitPartition.GetSlidingWindowLimiter(
                        partitionKey: RateLimiting.PartitionKey(httpContext),
                        factory: _ => new SlidingWindowRateLimiterOptions
                        {
                            PermitLimit = AppDefaults.RateLimit.Refresh.PermitLimit,
                            Window = TimeSpan.FromMinutes(AppDefaults.RateLimit.Refresh.WindowMinutes),
                            SegmentsPerWindow = AppDefaults.RateLimit.Refresh.Segments,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            QueueLimit = 0,
                        }));
            });
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
            services.Configure<JwtSettings>(configuration.GetSection("Jwt"));
            services.Configure<PasswordPolicy>(configuration.GetSection("PasswordPolicy"));
            services.Configure<UsernamePolicy>(configuration.GetSection("UsernamePolicy"));
            services.Configure<CorsSettings>(configuration.GetSection("Cors"));
            services.AddSingleton<IClock>(SystemClock.Instance);
            services.AddSingleton<IGuidFactory, TimeOrderedGuidFactory>();
            services.AddSingleton<IJwtFactory, JwtFactory>();
            services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
            services.AddSingleton<IAuthResponseMapper, AuthResponseMapper>();
            services.AddScoped<IUserRepository, EfUserRepository>();
            services.AddScoped<IRefreshTokenRepository, EfRefreshTokenRepository>();
            services.AddScoped<IInviteRepository, EfInviteRepository>();
            services.AddScoped<IUserStorageConfigRepository, EfUserStorageConfigRepository>();
            // AnichronDbContext is already scoped via AddDbContext; reuse the same instance for IUnitOfWork
            services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AnichronDbContext>());
            services.AddScoped<IRegistrationValidator, RegistrationValidator>();
            services.AddScoped<ILockoutService, LockoutService>();
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
                client.Timeout = TimeSpan.FromSeconds(AppDefaults.Pwned.TimeoutInSeconds);
            }).AddStandardResilienceHandler();

            var jwt = JwtConfiguration.Read(configuration);

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
                        ValidateIssuer = true,
                        ValidIssuer = jwt.Issuer,
                        ValidateAudience = true,
                        ValidAudience = jwt.Audience,
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.Zero,
                    };
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
            services.AddSingleton<IFileSystem, FileSystem>();
            services.AddHealthChecks()
                    .AddDbContextCheck<AnichronDbContext>("database")
                    .AddCheck<ProxyStorageHealthCheck>("proxyStorage")
                    .AddCheck<OriginalsStorageHealthCheck>("originalsStorage");
            return services;
        }
    }
}
