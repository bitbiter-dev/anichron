using Anichron.Core;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using Anichron.Testing.DependencyInjection;
using Anichron.Worker.Crawling;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;
using Anichron.Worker.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NodaTime;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Worker.Tests.Unit.Crawling;

internal static class FileIngestionPipelines
{
    public static FailingFiles NoFailures(IFileSystem? fileSystem = null)
        => new(new Dictionary<string, IngestionFailure>(), fileSystem ?? new MockFileSystem(), Substitute.For<IClock>());

    public static Task<CrawlRun> RunAsync(
        this FileIngestionPipeline pipeline, UserStorageConfig config, IWorkProgress progress, CancellationToken ct)
        => pipeline.RunAsync(config, NoFailures(), progress, ct);

    public static FileIngestionPipeline Build(
        IEnumerable<IIngestionMiddleware> middlewares,
        MockFileSystem fileSystem,
        WorkerSettings settings,
        IFileSystem? crawlFileSystem = null,
        FakeLogger<FileIngestionPipeline>? logger = null)
    {
        var directoryStrategy = Substitute.For<IProxyDirectoryStrategy>();
        directoryStrategy.GetDirectory(Arg.Any<Guid>(), Arg.Any<string>()).Returns("ab/cd");
        var runner = new IngestionPipelineRunner(
            middlewares, directoryStrategy, fileSystem, new FakeLogger<IngestionPipelineRunner>());
        var options = Options.Create(settings);
        return new FileIngestionPipeline(
            ScopeFactories.Providing(services => services.AddSingleton<IIngestionPipelineRunner>(runner)),
            crawlFileSystem ?? fileSystem,
            new LivePhotoLinker(fileSystem),
            new CrawlExclusions(options),
            options,
            Substitute.For<IGuidFactory>(),
            logger ?? new FakeLogger<FileIngestionPipeline>());
    }
}
