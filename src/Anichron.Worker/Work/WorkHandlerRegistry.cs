using Anichron.Core.Domain;
using Anichron.Core.Work;
using System.Reflection;

namespace Anichron.Worker.Work;

public delegate Task<object?> WorkDispatch(
    IServiceProvider services, string payload, IWorkProgress progress, CancellationToken ct);

public sealed record WorkHandlerRegistration(WorkKind Kind, Type PayloadType, WorkDispatch Run)
{
    public static WorkHandlerRegistration For<TPayload>()
        where TPayload : IWorkPayload
        => new(TPayload.Kind, typeof(TPayload), (services, payload, progress, ct)
            => services.GetRequiredService<IWorkHandler<TPayload>>()
                .RunAsync(WorkJson.Deserialize<TPayload>(payload), progress, ct));
}

public sealed class WorkHandlerRegistry
{
    private readonly Dictionary<WorkKind, WorkHandlerRegistration> handlers;

    public WorkHandlerRegistry(IEnumerable<WorkHandlerRegistration> registrations)
        : this(registrations, PayloadTypesIn(typeof(IWorkPayload).Assembly))
    {
    }

    internal WorkHandlerRegistry(IEnumerable<WorkHandlerRegistration> registrations, IEnumerable<Type> payloadTypes)
    {
        var registered = registrations.ToList();
        var payloads = payloadTypes.ToList();
        var problems = Enum.GetValues<WorkKind>()
            .SelectMany(kind => Problems(kind, payloads.Where(t => KindOf(t) == kind).ToList(),
                registered.Where(r => r.Kind == kind).ToList()))
            .ToList();
        if (problems.Count > 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, problems));

        handlers = registered.ToDictionary(r => r.Kind);
    }

    public Task<object?> RunAsync(
        ClaimedWork work, IServiceProvider services, IWorkProgress progress, CancellationToken ct)
        => handlers[work.Kind].Run(services, work.Payload, progress, ct);

    internal static IEnumerable<Type> PayloadTypesIn(Assembly assembly)
        => assembly.GetTypes().Where(t => t is { IsAbstract: false, IsInterface: false }
            && t.IsAssignableTo(typeof(IWorkPayload)));

    private static IEnumerable<string> Problems(
        WorkKind kind, List<Type> payloads, List<WorkHandlerRegistration> registrations)
    {
        if (payloads.Count != 1)
            yield return $"WorkKind.{kind} needs exactly one payload type, found {Names(payloads)}.";
        if (registrations.Count != 1)
            yield return $"WorkKind.{kind} needs exactly one handler, found {Names(registrations.Select(r => r.PayloadType))}.";
        else if (payloads is [var payload] && registrations[0].PayloadType != payload)
            yield return $"WorkKind.{kind}'s handler takes {registrations[0].PayloadType.Name}, not its payload {payload.Name}.";
    }

    private static string Names(IEnumerable<Type> types)
        => types.Select(t => t.Name).ToList() is { Count: > 0 } names ? string.Join(", ", names) : "none";

    private static WorkKind KindOf(Type payloadType)
        => (WorkKind)typeof(PayloadKind)
            .GetMethod(nameof(PayloadKind.Of))!
            .MakeGenericMethod(payloadType)
            .Invoke(null, null)!;

    private static class PayloadKind
    {
        public static WorkKind Of<TPayload>()
            where TPayload : IWorkPayload
            => TPayload.Kind;
    }
}
