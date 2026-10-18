using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Classification;
using Anichron.Worker.Ingestion.Middlewares;
using Anichron.Worker.Ingestion.Pipeline;
using Microsoft.Extensions.Logging;
using NodaTime;
using System.IO.Abstractions;

namespace Anichron.Worker.Tests.Unit.Ingestion.Middlewares;

public sealed class AlwaysOnMiddlewareTests
{
    public static TheoryData<string> Names => ["Classification", "ContentHashing", "Logging", "Persistence"];

    private static IIngestionMiddleware Create(string name)
    {
        var fileSystem = Substitute.For<IFileSystem>();
        var repository = Substitute.For<IMediaAssetRepository>();
        var saver = new IngestionSaver(Substitute.For<IUnitOfWork>(), repository);
        return name switch
        {
            "Classification" => new ClassificationMiddleware(
                new IngestionClassifier(repository, fileSystem),
                new LivePhotoPairer(null!, null!, saver, Substitute.For<ILogger<LivePhotoPairer>>()),
                new KnownFileReconciler(Substitute.For<IClock>(), Substitute.For<ILogger<KnownFileReconciler>>()),
                saver),
            "ContentHashing" => new ContentHashingMiddleware(fileSystem),
            "Logging" => new LoggingMiddleware(Substitute.For<ILogger<LoggingMiddleware>>()),
            "Persistence" => new PersistenceMiddleware([], Substitute.For<ILogger<PersistenceMiddleware>>()),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void CanInvoke_IsTrueForEveryItem(string name)
        => Create(name).CanInvoke(null!).Should().BeTrue();
}
