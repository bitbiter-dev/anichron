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
using System.Text;
using System.Threading.RateLimiting;
using static System.Globalization.CultureInfo;

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

        public IServiceCollection AddForwardedHeadersSupport()
        {
            return services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                // Clearing defaults ensures the Docker Compose gateway is trusted without
                // listing its IP explicitly, while blocking header injection from the internet.
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
            });
        }

        public IServiceCollection AddRateLimiting()
        {
            return services.AddRateLimiter(options =>
            {
                options.OnRejected = async (context, token) =>
                {
                    context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    {
                        context.HttpContext.Response.Headers.RetryAfter =
                            ((int)retryAfter.TotalSeconds).ToString(InvariantCulture);
                    }

                    await context.HttpContext.Response.WriteAsJsonAsync(
                        new { error = AuthMessages.TooManyRequests }, token);
                };

                // RemoteIpAddress is populated by UseForwardedHeaders before this middleware runs.
                // Requests with no resolvable IP share a tight "unresolved" bucket to prevent IP-hiding abuse.
                options.AddPolicy(AuthRateLimitPolicies.Sensitive, httpContext =>
                    RateLimitPartition.GetSlidingWindowLimiter(
                        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unresolved",
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
                        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unresolved",
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
            var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

            // Always register CORS services so UseCors() is valid in the middleware pipeline.
            // With no configured origins the default policy allows nothing (same-origin behavior).
            services.AddCors(options =>
            {
                if (allowedOrigins.Length > 0)
                {
                    options.AddDefaultPolicy(policy =>
                        policy.WithOrigins(allowedOrigins)
                            .AllowAnyHeader()
                            .AllowAnyMethod()
                            .AllowCredentials());
                }
            });

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
            // AnichronDbContext is already scoped via AddDbContext; reuse the same instance for IUnitOfWork
            services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AnichronDbContext>());
            services.AddScoped<IRegistrationValidator, RegistrationValidator>();
            services.AddScoped<ITokenService, TokenService>();
            services.AddScoped<IAuthService, AuthService>();
            services.AddTransient<IBootstrapSeeder, BootstrapSeeder>();
            services.AddScoped<IAdminResetService, AdminResetService>();
            services.AddScoped<IAdminUserService, AdminUserService>();
            services.AddTransient<IBootstrapResetService, BootstrapResetService>();

            // SameSite=None is required when the UI and API are on different origins so browsers
            // send the cookie on cross-origin requests. SameSite=Strict is safer for same-origin.
            var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
            services.AddSingleton(new AuthCookieSettings
            {
                SameSite = allowedOrigins.Length > 0 ? SameSiteMode.None : SameSiteMode.Strict,
                RefreshTokenDays = configuration.GetValue("Jwt:RefreshTokenDays", AppDefaults.Jwt.RefreshTokenDays),
            });

            services.AddHttpClient<IPwnedPasswordClient, PwnedPasswordClient>(client =>
            {
                client.BaseAddress = new Uri(AppDefaults.Pwned.Url);
                client.DefaultRequestHeaders.Add("Add-Padding", "true");
                client.Timeout = TimeSpan.FromSeconds(AppDefaults.Pwned.TimeoutInSeconds);
            }).AddStandardResilienceHandler();

            var jwtSecret = configuration["Jwt:Secret"]
                            ?? throw new InvalidOperationException("Jwt:Secret configuration is missing.");
            var jwtIssuer = configuration["Jwt:Issuer"]
                            ?? throw new InvalidOperationException("Jwt:Issuer is missing.");
            var jwtAudience = configuration["Jwt:Audience"]
                              ?? throw new InvalidOperationException("Jwt:Audience is missing.");

            if (Encoding.UTF8.GetByteCount(jwtSecret) < 32)
                throw new InvalidOperationException("Jwt:Secret must be at least 32 bytes.");

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
                        ValidateIssuer = true,
                        ValidIssuer = jwtIssuer,
                        ValidateAudience = true,
                        ValidAudience = jwtAudience,
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
                    .AddCheck<ProxyStorageHealthCheck>("proxyStorage");
            return services;
        }
    }
}
