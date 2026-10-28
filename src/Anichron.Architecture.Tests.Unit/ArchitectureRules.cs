using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using ArchUnitNET.Fluent;
using Microsoft.Extensions.Logging;
using System.IO.Abstractions;
using static Anichron.Architecture.Tests.Unit.ArchitectureModel;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Anichron.Architecture.Tests.Unit;

internal sealed record ArchitectureRule(string Name, IArchRule Rule);

internal static class ArchitectureRules
{
    public const string ApiDoesNotUseWorker = "api-does-not-use-worker";
    public const string WorkerDoesNotUseApi = "worker-does-not-use-api";
    public const string LibrariesAreNotWeb = "libraries-are-not-web";
    public const string ApiReadsNoFileSystem = "api-reads-no-file-system";
    public const string ApiReachesDataThroughRepositories = "api-reaches-data-through-repositories";
    public const string OnlyWorkerWritesMedia = "only-worker-writes-media";
    public const string ApiReadsMediaThroughViews = "api-reads-media-through-views";
    public const string ProxiesReachFinalNameOnlyInStagingWriter = "proxies-reach-final-name-only-in-staging-writer";
    public const string LoggingIsSourceGenerated = "logging-is-source-generated";
    public const string NoNotImplementedException = "no-not-implemented-exception";
    public const string DomainHasNoDataAnnotations = "domain-has-no-data-annotations";
    public const string DomainUsesNodaTime = "domain-uses-nodatime";
    public const string OnlyWorkQueueInsertsWorkItems = "only-work-queue-inserts-work-items";
    public const string WorkHandlersLiveInWorker = "work-handlers-live-in-worker";
    public const string WorkItemsChangeOnlyThroughQueueOrCancel = "work-items-change-only-through-queue-or-cancel";
    public const string OpenCvStaysInTheMatcher = "opencv-stays-in-the-matcher";
    public const string OnlyWorkerWritesProxyOrphans = "only-worker-writes-proxy-orphans";

    private const string Domain = "Anichron.Core.Domain";
    private const string ApiWiring = "Anichron.API.Infrastructure";
    private const string StagingWriter = "Anichron.Worker.Ingestion.Proxy.ProxyStagingWriter";
    private const string WorkQueue = "Anichron.Core.Work.EfWorkQueue";
    private const string WorkItemReader = "Anichron.Core.Work.EfWorkItemReader";
    private const string WorkItemCanceller = "Anichron.Core.Work.EfWorkItemCanceller";
    private const string ProxyOrphanReader = "Anichron.Core.Data.Repository.EfProxyOrphanReader";
    private const string ThumbnailMatcher = @"^Anichron\.Worker\.Bursts\.OrbThumbnailMatcher(\+.*)?$";

    public static IReadOnlyList<ArchitectureRule> All { get; } =
    [
        new(ApiDoesNotUseWorker,
            Types().That().ResideInAssembly(ApiAssembly)
                .Should().NotDependOnAnyTypesThat().ResideInAssembly(WorkerAssembly)),
        new(WorkerDoesNotUseApi,
            Types().That().ResideInAssembly(WorkerAssembly)
                .Should().NotDependOnAnyTypesThat().ResideInAssembly(ApiAssembly)),
        new(LibrariesAreNotWeb,
            Types().That().ResideInAssembly(CoreAssembly, InfrastructureAssembly, WorkerAssembly)
                .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching(@"^Microsoft\.AspNetCore(\..*)?$")),
        new(ApiReadsNoFileSystem,
            Types().That().ResideInAssembly(ApiAssembly)
                .Should().NotDependOnAny(typeof(IFileSystem))),
        new(ApiReachesDataThroughRepositories,
            Types().That().ResideInAssembly(ApiAssembly).And().DoNotResideInNamespace(ApiWiring)
                .Should().NotDependOnAny(typeof(AnichronDbContext))),
        new(OnlyWorkerWritesMedia,
            Types().That().ResideInAssembly(ApiAssembly, InfrastructureAssembly)
                .Should().NotDependOnAny(typeof(IMediaAssetRepository))),
        new(ApiReadsMediaThroughViews,
            Types().That().ResideInAssembly(ApiAssembly)
                .Should().NotDependOnAny(typeof(MediaAsset), typeof(Metadata), typeof(ProxyFile), typeof(AssetInteraction))),
        new(ProxiesReachFinalNameOnlyInStagingWriter,
            Types().That().ResideInAssembly(WorkerAssembly).And().DoNotHaveFullName(StagingWriter)
                .Should().NotCallAny(MethodMembers().That().AreDeclaredIn(typeof(IFile))
                    .And().HaveNameMatching(@"^(Move|Copy|Replace)\("))),
        new(LoggingIsSourceGenerated,
            Types().That().ResideInAssembly(ApiAssembly, CoreAssembly, InfrastructureAssembly, WorkerAssembly)
                .Should().NotCallAny(MethodMembers().That().AreDeclaredIn(typeof(LoggerExtensions))
                    .And().HaveNameMatching(@"^Log(Trace|Debug|Information|Warning|Error|Critical)?\("))),
        new(NoNotImplementedException,
            Types().That().ResideInAssembly(ApiAssembly, CoreAssembly, InfrastructureAssembly, WorkerAssembly)
                .Should().NotDependOnAnyTypesThat().HaveFullName(typeof(NotImplementedException).FullName)),
        new(DomainHasNoDataAnnotations,
            Types().That().ResideInNamespace(Domain)
                .Should().NotDependOnAnyTypesThat().ResideInNamespace("System.ComponentModel.DataAnnotations")),
        new(DomainUsesNodaTime,
            Types().That().ResideInNamespace(Domain)
                .Should().NotDependOnAny(typeof(DateTime), typeof(DateTimeOffset))),
        new(OnlyWorkQueueInsertsWorkItems,
            Types().That().ResideInAssembly(ApiAssembly, CoreAssembly, InfrastructureAssembly, WorkerAssembly)
                .And().DoNotHaveFullName(WorkQueue)
                .Should().NotCallAny(MethodMembers().That().AreConstructors().And().AreDeclaredIn(typeof(WorkItem)))),
        new(WorkHandlersLiveInWorker,
            Types().That().ImplementInterface(typeof(IWorkHandler<>))
                .Should().ResideInAssembly(WorkerAssembly)),
        new(WorkItemsChangeOnlyThroughQueueOrCancel,
            Types().That().ResideInAssembly(ApiAssembly, CoreAssembly, InfrastructureAssembly)
                .And().DoNotResideInNamespace(Domain)
                .And().DoNotHaveFullName(typeof(AnichronDbContext).FullName)
                .And().DoNotHaveFullName(WorkQueue)
                .And().DoNotHaveFullName(WorkItemReader)
                .And().DoNotHaveFullName(WorkItemCanceller)
                .Should().NotDependOnAny(typeof(WorkItem))),
        new(OpenCvStaysInTheMatcher,
            Types().That().ResideInAssembly(ApiAssembly, CoreAssembly, InfrastructureAssembly, WorkerAssembly)
                .And().DoNotHaveFullNameMatching(ThumbnailMatcher)
                .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching(@"^OpenCvSharp(\..*)?$")),
        new(OnlyWorkerWritesProxyOrphans,
            Types().That().ResideInAssembly(ApiAssembly, CoreAssembly, InfrastructureAssembly)
                .And().DoNotResideInNamespace(Domain)
                .And().DoNotHaveFullName(typeof(AnichronDbContext).FullName)
                .And().DoNotHaveFullName(ProxyOrphanReader)
                .Should().NotDependOnAny(typeof(ProxyOrphan))),
    ];

    public static ArchitectureRule Named(string name) => All.Single(rule => rule.Name == name);
}
