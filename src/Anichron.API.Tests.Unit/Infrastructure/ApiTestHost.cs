using Anichron.API.Endpoints;
using Anichron.API.Services;
using Anichron.Core.Data.Repository;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Anichron.API.Tests.Unit.Infrastructure;

internal static class ApiTestHost
{
    public static WebApplicationBuilder CreateBuilder(string? webRootPath = null)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { WebRootPath = webRootPath });
        builder.Services.AddHealthChecks();
        builder.Services.AddSingleton(Substitute.For<IAuthService>());
        builder.Services.AddSingleton(Substitute.For<IAuthResponseMapper>());
        builder.Services.AddSingleton(Substitute.For<IAdminResetService>());
        builder.Services.AddSingleton(Substitute.For<IAdminUserService>());
        builder.Services.AddSingleton(Substitute.For<IAdminStorageConfigService>());
        builder.Services.AddSingleton(Substitute.For<IUserRepository>());
        builder.Services.AddSingleton(Substitute.For<IMediaAssetReadRepository>());
        return builder;
    }
}
