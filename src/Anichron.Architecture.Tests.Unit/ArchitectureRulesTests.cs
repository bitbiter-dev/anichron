using ArchUnitNET.Domain;

namespace Anichron.Architecture.Tests.Unit;

public sealed class ArchitectureRulesTests
{
    public static TheoryData<string> RuleNames => [.. ArchitectureRules.All.Select(rule => rule.Name)];

    private static HashSet<string> Violators(string rule)
        => ArchitectureRules.Named(rule).Rule.Evaluate(ArchitectureModel.Instance)
            .Where(result => !result.Passed)
            .Select(result => ((IHasName)result.EvaluatedObject).FullName)
            .ToHashSet();

    [Theory]
    [MemberData(nameof(RuleNames))]
    public void Rule_HasNoViolationsBeyondItsExceptions(string rule)
    {
        var unexcused = Violators(rule).Except(ArchitectureExceptions.TypesFor(rule));

        unexcused.Should().BeEmpty(
            "rule '{0}' is broken by these types; fix them, or add an exception linked to the open issue that fixes them",
            rule);
    }

    [Theory]
    [MemberData(nameof(RuleNames))]
    public void EveryException_StillExcusesARealViolation(string rule)
    {
        var stale = ArchitectureExceptions.TypesFor(rule).Except(Violators(rule));

        stale.Should().BeEmpty("these exceptions to rule '{0}' no longer excuse anything; delete them", rule);
    }

    [Fact]
    public void EveryException_NamesAKnownRuleAnIssueAndAReason()
        => ArchitectureExceptions.All.Should().AllSatisfy(exception =>
        {
            RuleNames.Select(row => row.Data).Should().Contain(exception.Rule);
            exception.Issue.Should().BePositive();
            exception.Reason.Should().NotBeNullOrWhiteSpace();
        });
}
