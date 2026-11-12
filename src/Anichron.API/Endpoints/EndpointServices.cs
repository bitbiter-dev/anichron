namespace Anichron.API.Endpoints;

public static class EndpointServices
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddEndpointServices()
            => services
                .AddSingleton<ISessionResponseMapper, SessionResponseMapper>()
                .AddSingleton<IAdminResponseMapper, AdminResponseMapper>()
                .AddScoped<WorkItemEvents>();
    }
}
