using Microsoft.Extensions.DependencyInjection;

namespace Anichron.Testing.DependencyInjection;

public static class ScopeFactories
{
    public static IServiceScopeFactory Providing(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }
}
