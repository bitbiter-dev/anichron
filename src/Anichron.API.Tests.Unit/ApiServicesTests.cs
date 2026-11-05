using Anichron.API.Endpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anichron.API.Tests.Unit;

public sealed class ApiServicesTests
{
    [Theory]
    [InlineData(typeof(IAuthResponseMapper), typeof(AuthResponseMapper), ServiceLifetime.Singleton)]
    [InlineData(typeof(WorkItemEvents), typeof(WorkItemEvents), ServiceLifetime.Scoped)]
    public void AddApiSurface_RegistersWhatTheEndpointsUse(Type service, Type implementation, ServiceLifetime lifetime)
        => new ServiceCollection().AddApiSurface(new ConfigurationBuilder().Build())
            .Should().ContainSingle(d => d.ServiceType == service)
            .Which.Should().Match<ServiceDescriptor>(d => d.ImplementationType == implementation && d.Lifetime == lifetime);
}
