using Anichron.Core;
using Anichron.Testing.DependencyInjection;
using Anichron.Worker.Crawling;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;
using Anichron.Worker.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Worker.Tests.Unit.Crawling;

internal static class FileIngestionPipelines
{
    public static FileIngestionPipeline Build(
        IEnumerable<IIngestionMiddleware> middlewares,
        MockFileSystem fileSystem,
        WorkerSettings settings,
        IFileSystem? crawlFileSystem = null)
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
            new FakeLogger<FileIngestionPipeline>());
    }
}
