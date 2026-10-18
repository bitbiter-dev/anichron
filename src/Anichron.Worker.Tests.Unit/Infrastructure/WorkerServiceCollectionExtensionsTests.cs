using Anichron.Core;
using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using Anichron.Worker.Crawling;
using Anichron.Worker.Infrastructure;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Classification;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;
using Anichron.Worker.Maintenance;
using Anichron.Worker.Settings;
using Anichron.Worker.Startup;
using Anichron.Worker.Work;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
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

    [Fact]
    public void AddIngestionServices_RegistersCrawlExclusions_AsSingleton()
    {
        var services = new ServiceCollection();

        services.AddIngestionServices();

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(CrawlExclusions) &&
            d.Lifetime == ServiceLifetime.Singleton);
    }

    // ==========================================================================
    // AddIngestionServices — proxy generators
    // ==========================================================================

    [Fact]
    public void AddIngestionServices_RegistersTwoImageProxyGenerators_AsSingleton()
    {
        var services = new ServiceCollection();

        services.AddIngestionServices();

        services.Count(d =>
            d.ServiceType == typeof(IImageProxyGenerator) &&
            d.Lifetime == ServiceLifetime.Singleton)
        .Should().Be(2);
    }

    [Fact]
    public void AddIngestionServices_RegistersTheBlurhashEncoder_AsSingleton()
    {
        var services = new ServiceCollection();

        services.AddIngestionServices();

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(BlurhashEncoder) &&
            d.Lifetime == ServiceLifetime.Singleton);
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
    public void AddWorkerHostedServices_MigratesFirstThenRunsTheWorkQueueAndTheCrawl()
    {
        var services = new ServiceCollection();

        services.AddWorkerHostedServices();

        services.Where(d => d.ServiceType == typeof(IHostedService)).Select(d => d.ImplementationType).Should().Equal(
            typeof(DatabaseMigratorService), typeof(WorkerInitializer), typeof(WorkScheduler), typeof(WorkExecutor),
            typeof(Worker.Crawling.Worker));
    }

    // ==========================================================================
    // AddWorkQueueServices
    // ==========================================================================

    [Fact]
    public void AddWorkQueueServices_RegistersEachKindsHandlerAsScoped()
    {
        var services = new ServiceCollection();

        services.AddWorkQueueServices();

        services.Where(d => d.Lifetime == ServiceLifetime.Scoped)
            .Select(d => (d.ServiceType, d.ImplementationType)).Should().BeEquivalentTo([
                (typeof(IWorkHandler<TokenCleanupPayload>), typeof(TokenCleanupHandler)),
                (typeof(IWorkHandler<WorkItemRetentionPayload>), typeof(WorkItemRetentionHandler)),
            ]);
    }

    [Fact]
    public void AddWorkQueueServices_RegistersEachPeriodicKindsSchedule()
        => new ServiceCollection().AddWorkQueueServices()
            .Where(d => d.ServiceType == typeof(IWorkSchedule))
            .Select(d => (d.ImplementationType, d.Lifetime)).Should().BeEquivalentTo([
                (typeof(TokenCleanupSchedule), ServiceLifetime.Singleton),
                (typeof(WorkItemRetentionSchedule), ServiceLifetime.Singleton),
            ]);

    [Fact]
    public void AddWorkQueueServices_RegistersTheRegistryAsASingleton()
        => new ServiceCollection().AddWorkQueueServices()
            .Should().ContainSingle(d => d.ServiceType == typeof(WorkHandlerRegistry))
            .Which.Lifetime.Should().Be(ServiceLifetime.Singleton);

    [Fact]
    public void TheWholeWorker_ResolvesEveryHostedServiceWithACompleteKindRegistry()
    {
        var services = DataServices();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddWorkerCoreServices().AddIngestionServices().AddWorkQueueServices().AddWorkerHostedServices();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        var hosted = provider.GetServices<IHostedService>();

        hosted.Should().HaveCount(5);
        provider.GetRequiredService<WorkHandlerRegistry>().Should().NotBeNull();
        using var scope = provider.CreateScope();
        provider.GetServices<WorkHandlerRegistration>().Select(r => r.Kind).Should().BeEquivalentTo(Enum.GetValues<WorkKind>());
        foreach (var registration in provider.GetServices<WorkHandlerRegistration>())
            scope.ServiceProvider.GetRequiredService(typeof(IWorkHandler<>).MakeGenericType(registration.PayloadType));
    }

    // ==========================================================================
    // Remaining registrations
    // ==========================================================================

    public static TheoryData<Type, Type, ServiceLifetime> IngestionRegistrations => new()
    {
        { typeof(ILivePhotoLinker), typeof(LivePhotoLinker), ServiceLifetime.Singleton },
        { typeof(IFileIngestionPipeline), typeof(FileIngestionPipeline), ServiceLifetime.Singleton },
        { typeof(PairedMovResolver), typeof(PairedMovResolver), ServiceLifetime.Scoped },
        { typeof(PairedMovProxyWriter), typeof(PairedMovProxyWriter), ServiceLifetime.Scoped },
        { typeof(IngestionSaver), typeof(IngestionSaver), ServiceLifetime.Scoped },
        { typeof(IngestionClassifier), typeof(IngestionClassifier), ServiceLifetime.Scoped },
        { typeof(LivePhotoPairer), typeof(LivePhotoPairer), ServiceLifetime.Scoped },
        { typeof(KnownFileReconciler), typeof(KnownFileReconciler), ServiceLifetime.Scoped },
        { typeof(IProcessLauncher), typeof(SystemProcessLauncher), ServiceLifetime.Singleton },
        { typeof(IVideoProcessor), typeof(FfmpegVideoProcessor), ServiceLifetime.Singleton },
        { typeof(IImageProcessor), typeof(ImageSharpProcessor), ServiceLifetime.Singleton },
    };

    [Theory]
    [MemberData(nameof(IngestionRegistrations))]
    public void AddIngestionServices_RegistersTheImplementationWithItsLifetime(Type service, Type implementation, ServiceLifetime lifetime)
    {
        var services = new ServiceCollection();

        services.AddIngestionServices();

        services.Should().ContainSingle(d => d.ServiceType == service)
            .Which.Should().Match<ServiceDescriptor>(d => d.ImplementationType == implementation && d.Lifetime == lifetime);
    }

    [Fact]
    public void AddIngestionServices_RegistersAScopedWriterForEveryOutcomeThatNeedsProcessing()
    {
        var services = new ServiceCollection();

        services.AddIngestionServices();

        var writers = services.Where(d => d.ServiceType == typeof(IOutcomeWriter)).ToList();
        writers.Select(d => d.ImplementationType).Should().BeEquivalentTo([typeof(NewContentWriter), typeof(ContentChangedWriter)]);
        writers.Should().OnlyContain(d => d.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddWorkerCoreServices_RegistersTheSystemTimeProvider()
    {
        var services = new ServiceCollection();

        services.AddWorkerCoreServices();

        services.Should().ContainSingle(d => d.ServiceType == typeof(TimeProvider))
            .Which.ImplementationInstance.Should().BeSameAs(TimeProvider.System);
    }

    [Fact]
    public void AddWorkerCoreServices_BindsWorkerSettingsFromTheWorkerSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Worker:CrawlIntervalHours"] = "9" })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        services.AddWorkerCoreServices();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<WorkerSettings>>().Value.CrawlIntervalHours.Should().Be(9);
    }

    public static TheoryData<Type, Type> DataRegistrations => new()
    {
        { typeof(IUserRepository), typeof(EfUserRepository) },
        { typeof(IUserStorageConfigRepository), typeof(EfUserStorageConfigRepository) },
        { typeof(IRefreshTokenRepository), typeof(EfRefreshTokenRepository) },
        { typeof(IMediaAssetRepository), typeof(EfMediaAssetRepository) },
        { typeof(IDatabaseMigrator), typeof(EfDatabaseMigrator) },
        { typeof(IWorkQueue), typeof(EfWorkQueue) },
    };

    [Fact]
    public void AddWorkerDataServices_RegistersTheWorkItemStoreAsASingleton()
        => DataServices().Should().ContainSingle(d => d.ServiceType == typeof(IWorkItemStore))
            .Which.Should().Match<ServiceDescriptor>(d =>
                d.ImplementationType == typeof(EfWorkItemStore) && d.Lifetime == ServiceLifetime.Singleton);

    [Fact]
    public void AddWorkerDataServices_ContextFactoryCreatesNpgsqlContexts()
    {
        using var provider = DataServices().BuildServiceProvider();

        using var context = provider.GetRequiredService<IDbContextFactory<AnichronDbContext>>().CreateDbContext();

        context.Database.GetConnectionString().Should().Contain("Host=db.internal");
    }

    private static ServiceCollection DataServices()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["POSTGRES_CONNECTION:HOST"] = "db.internal",
            ["POSTGRES_CONNECTION:DBNAME"] = "anichron",
            ["POSTGRES_CONNECTION:USER"] = "worker",
            ["POSTGRES_CONNECTION:PASSWORD"] = "pw",
        }).Build();
        var services = new ServiceCollection();
        services.AddWorkerDataServices(configuration);
        return services;
    }

    [Theory]
    [MemberData(nameof(DataRegistrations))]
    public void AddWorkerDataServices_RegistersTheRepositoryAsScoped(Type service, Type implementation)
        => DataServices().Should().ContainSingle(d => d.ServiceType == service)
            .Which.Should().Match<ServiceDescriptor>(d => d.ImplementationType == implementation && d.Lifetime == ServiceLifetime.Scoped);

    [Fact]
    public void AddWorkerDataServices_UnitOfWorkIsTheScopedDbContextOnNpgsql()
    {
        using var provider = DataServices().BuildServiceProvider();
        using var scope = provider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<AnichronDbContext>();

        scope.ServiceProvider.GetRequiredService<IUnitOfWork>().Should().BeSameAs(context);
        context.Database.ProviderName.Should().Be("Npgsql.EntityFrameworkCore.PostgreSQL");
        context.Database.GetConnectionString().Should().Contain("Host=db.internal");
    }
}
