using Anichron.API.Endpoints;
using Microsoft.Extensions.DependencyInjection;

namespace Anichron.API.Tests.Unit.Endpoints;

public sealed class EndpointServicesTests
{
    [Theory]
    [InlineData(typeof(ISessionResponseMapper), typeof(SessionResponseMapper), ServiceLifetime.Singleton)]
    [InlineData(typeof(IAdminResponseMapper), typeof(AdminResponseMapper), ServiceLifetime.Singleton)]
    [InlineData(typeof(WorkItemEvents), typeof(WorkItemEvents), ServiceLifetime.Scoped)]
    public void AddEndpointServices_RegistersWhatTheEndpointsUse(Type service, Type implementation, ServiceLifetime lifetime)
        => new ServiceCollection().AddEndpointServices()
            .Should().ContainSingle(d => d.ServiceType == service)
            .Which.Should().Match<ServiceDescriptor>(d => d.ImplementationType == implementation && d.Lifetime == lifetime);
}
