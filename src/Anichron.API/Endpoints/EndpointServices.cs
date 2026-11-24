namespace Anichron.API.Endpoints;

public static class EndpointServices
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddEndpointServices()
            => services
                .AddScoped<ISessionResponseMapper, SessionResponseMapper>()
                .AddScoped<IUserProfiles, UserProfiles>()
                .AddSingleton<IAdminResponseMapper, AdminResponseMapper>()
                .AddScoped<WorkItemEvents>();
    }
}
