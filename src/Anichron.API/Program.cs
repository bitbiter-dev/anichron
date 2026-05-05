using Anichron.API.Endpoints;
using Anichron.Core.Data;
using Anichron.Core.Services;
using Anichron.Infrastructure.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NodaTime;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// Database
var databaseConnection = DatabaseConfiguration.GetConnectionString(builder.Configuration);
builder.Services.AddDbContext<AnichronDbContext>(options =>
    options.UseNpgsql(databaseConnection, o => o.UseNodaTime()));

// Auth services
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddSingleton<IClock>(SystemClock.Instance);
builder.Services.AddScoped<IAuthService, AuthService>();

// JWT bearer authentication
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret configuration is missing.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api/v1");
api.MapAuthEndpoints();

await app.RunAsync();
