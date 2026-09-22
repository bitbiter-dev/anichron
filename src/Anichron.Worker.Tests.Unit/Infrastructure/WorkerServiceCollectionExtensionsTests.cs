using Anichron.Core;
using Anichron.Worker.Crawling;
using Anichron.Worker.Infrastructure;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NodaTime;
using System.IO.Abstractions;

namespace Anichron.Worker.Tests.Unit.Infrastructure;

public sealed class WorkerServiceCollectionExtensionsTests
{
    // ==========================================================================
    // AddWorkerCoreServices
    // ==========================================================================

    [Fact]
    public void AddWorkerCoreServices_RegistersGuidFactory_AsSingleton()
    {
        var services = new ServiceCollection();

        services.AddWorkerCoreServices();

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IGuidFactory) &&
            d.ImplementationType == typeof(TimeOrderedGuidFactory) &&
            d.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddWorkerCoreServices_RegistersClock_AsSingleton()
    {
        var services = new ServiceCollection();

        services.AddWorkerCoreServices();

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IClock) &&
            d.ImplementationInstance is SystemClock &&
            d.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddWorkerCoreServices_RegistersWorkerState_AsSingleton()
    {
        var services = new ServiceCollection();

        services.AddWorkerCoreServices();

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(WorkerState) &&
            d.ImplementationType == typeof(WorkerState) &&
            d.Lifetime == ServiceLifetime.Singleton);
    }

    // ==========================================================================
    // AddIngestionServices — middleware
    // ==========================================================================

    [Fact]
    public void AddIngestionServices_RegistersSevenMiddlewares_AsScoped()
    {
        var services = new ServiceCollection();

        services.AddIngestionServices();

        services.Count(d =>
            d.ServiceType == typeof(IIngestionMiddleware) &&
            d.Lifetime == ServiceLifetime.Scoped)
        .Should().Be(7);
    }

    [Fact]
    public void AddIngestionServices_RegistersPipelineRunner_AsScoped()
    {
        var services = new ServiceCollection();

        services.AddIngestionServices();

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IIngestionPipelineRunner) &&
            d.ImplementationType == typeof(IngestionPipelineRunner) &&
            d.Lifetime == ServiceLifetime.Scoped);
    }

    // ==========================================================================
    // AddIngestionServices — proxy generators
    // ==========================================================================

    [Fact]
    public void AddIngestionServices_RegistersThreeImageProxyGenerators_AsSingleton()
    {
        var services = new ServiceCollection();

        services.AddIngestionServices();

        services.Count(d =>
            d.ServiceType == typeof(IImageProxyGenerator) &&
            d.Lifetime == ServiceLifetime.Singleton)
        .Should().Be(3);
    }

    [Fact]
    public void AddIngestionServices_RegistersOneVideoProxyGenerator_AsSingleton()
    {
        var services = new ServiceCollection();

        services.AddIngestionServices();

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IVideoProxyGenerator) &&
            d.ImplementationType == typeof(Video720PGenerator) &&
            d.Lifetime == ServiceLifetime.Singleton);
    }

    // ==========================================================================
    // AddIngestionServices — infrastructure
    // ==========================================================================

    [Fact]
    public void AddIngestionServices_RegistersFileSystem_AsSingleton()
    {
        var services = new ServiceCollection();

        services.AddIngestionServices();

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IFileSystem) &&
            d.ImplementationType == typeof(FileSystem) &&
            d.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddIngestionServices_RegistersProxyDirectoryStrategy_AsSingleton()
    {
        var services = new ServiceCollection();

        services.AddIngestionServices();

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IProxyDirectoryStrategy) &&
            d.ImplementationType == typeof(TwoLevelHexShardStrategy) &&
            d.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddIngestionServices_RegistersProxyStagingWriter_AsSingleton()
    {
        var services = new ServiceCollection();

        services.AddIngestionServices();

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(ProxyStagingWriter) &&
            d.Lifetime == ServiceLifetime.Singleton);
    }

    // ==========================================================================
    // AddWorkerHostedServices
    // ==========================================================================

    [Fact]
    public void AddWorkerHostedServices_RegistersFourHostedServices()
    {
        var services = new ServiceCollection();

        services.AddWorkerHostedServices();

        services.Count(d => d.ServiceType == typeof(IHostedService)).Should().Be(4);
    }
}
