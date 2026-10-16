using Anichron.Core;
using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Middlewares;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;
using Microsoft.Extensions.Logging;
using NodaTime;
using System.IO.Abstractions;

namespace Anichron.Worker.Tests.Unit.Ingestion.Middlewares;

public sealed class AlwaysOnMiddlewareTests
{
    public static TheoryData<string> Names => ["ContentHashing", "IdempotencyCheck", "Logging", "Persistence"];

    private static IIngestionMiddleware Create(string name)
    {
        var fileSystem = Substitute.For<IFileSystem>();
        var repository = Substitute.For<IMediaAssetRepository>();
        return name switch
        {
            "ContentHashing" => new ContentHashingMiddleware(fileSystem),
            "IdempotencyCheck" => new IdempotencyCheckMiddleware(
                repository, Substitute.For<IUnitOfWork>(), fileSystem, Substitute.For<IClock>(),
                Substitute.For<ILogger<IdempotencyCheckMiddleware>>()),
            "Logging" => new LoggingMiddleware(Substitute.For<ILogger<LoggingMiddleware>>()),
            "Persistence" => new PersistenceMiddleware(
                repository, Substitute.For<IUnitOfWork>(), fileSystem, Substitute.For<IClock>(),
                new PairedMovResolver(repository, fileSystem, Substitute.For<IClock>(), Substitute.For<IGuidFactory>()),
                new PairedMovProxyWriter(
                    [], Substitute.For<IProxyDirectoryStrategy>(), null!, repository,
                    Substitute.For<ILogger<PairedMovProxyWriter>>()),
                Substitute.For<ILogger<PersistenceMiddleware>>()),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void CanInvoke_IsTrueForEveryItem(string name)
        => Create(name).CanInvoke(null!).Should().BeTrue();
}
