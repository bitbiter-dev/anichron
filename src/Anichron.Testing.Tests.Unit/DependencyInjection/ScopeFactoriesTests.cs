using Anichron.Testing.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Anichron.Testing.Tests.Unit.DependencyInjection;

public sealed class ScopeFactoriesTests
{
    private sealed class Dependency
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    [Fact]
    public void Providing_RegisteredInstance_ResolvesInEveryScope()
    {
        var dependency = new Dependency();
        var factory = ScopeFactories.Providing(services => services.AddSingleton(dependency));

        using var first = factory.CreateScope();
        using var second = factory.CreateScope();

        first.ServiceProvider.GetRequiredService<Dependency>().Should().BeSameAs(dependency);
        second.ServiceProvider.GetRequiredService<Dependency>().Should().BeSameAs(dependency);
    }

    [Fact]
    public void Providing_ScopedRegistration_GivesEachScopeItsOwn()
    {
        var factory = ScopeFactories.Providing(services => services.AddScoped<Dependency>());

        using var first = factory.CreateScope();
        using var second = factory.CreateScope();

        first.ServiceProvider.GetRequiredService<Dependency>()
            .Should().NotBeSameAs(second.ServiceProvider.GetRequiredService<Dependency>());
    }

    [Fact]
    public void Providing_UnregisteredService_FailsToResolve()
    {
        var factory = ScopeFactories.Providing(_ => { });

        using var scope = factory.CreateScope();

        scope.ServiceProvider.Invoking(p => p.GetRequiredService<Dependency>()).Should().Throw<InvalidOperationException>();
    }
}
