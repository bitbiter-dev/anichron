using Anichron.Core.Data.Repository;
using ArchUnitNET.Fluent;
using System.IO.Abstractions;
using static Anichron.Architecture.Tests.Unit.ArchitectureModel;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Anichron.Architecture.Tests.Unit;

internal sealed record ArchitectureRule(string Name, IArchRule Rule);

internal static class ArchitectureRules
{
    public const string CoreIsSelfContained = "core-is-self-contained";
    public const string ApiReadsNoFileSystem = "api-reads-no-file-system";
    public const string NoNotImplementedException = "no-not-implemented-exception";
    public const string OnlyWorkerWritesMedia = "only-worker-writes-media";

    public static IReadOnlyList<ArchitectureRule> All { get; } =
    [
        new(CoreIsSelfContained,
            Types().That().ResideInAssembly(CoreAssembly)
                .Should().NotDependOnAnyTypesThat()
                .ResideInAssembly(ApiAssembly, InfrastructureAssembly, WorkerAssembly)),
        new(ApiReadsNoFileSystem,
            Types().That().ResideInAssembly(ApiAssembly)
                .Should().NotDependOnAny(typeof(IFileSystem))),
        new(NoNotImplementedException,
            Types().That().ResideInAssembly(ApiAssembly, CoreAssembly, InfrastructureAssembly, WorkerAssembly)
                .Should().NotDependOnAny(typeof(NotImplementedException))),
        new(OnlyWorkerWritesMedia,
            Types().That().ResideInAssembly(ApiAssembly, InfrastructureAssembly)
                .Should().NotDependOnAny(typeof(IMediaAssetRepository))),
    ];

    public static ArchitectureRule Named(string name) => All.Single(rule => rule.Name == name);
}
