using ArchUnitNET.Loader;
using System.IO.Abstractions;
using System.Reflection;
using ArchUnitArchitecture = ArchUnitNET.Domain.Architecture;

namespace Anichron.Architecture.Tests.Unit;

internal static class ArchitectureModel
{
    private static readonly Lazy<ArchUnitArchitecture> Loaded = new(Load);

    public static ArchUnitArchitecture Instance => Loaded.Value;

    public static Assembly ApiAssembly => Assembly.Load("Anichron.API");
    public static Assembly CoreAssembly => Assembly.Load("Anichron.Core");
    public static Assembly InfrastructureAssembly => Assembly.Load("Anichron.Infrastructure");
    public static Assembly WorkerAssembly => Assembly.Load("Anichron.Worker");

    private static ArchUnitArchitecture Load()
        => new ArchLoader()
            .LoadAssemblies(ApiAssembly, CoreAssembly, InfrastructureAssembly, WorkerAssembly, typeof(IFileSystem).Assembly)
            .Build();
}
