using ForgeDeck.Core;

namespace ForgeDeck.Core.Tests;

public class CloseDecisionTests
{
    [Theory]
    [InlineData(CloseBehavior.Ask, true, CloseAction.Exit)]
    [InlineData(CloseBehavior.Exit, true, CloseAction.Exit)]
    [InlineData(CloseBehavior.MinimizeToTray, true, CloseAction.Exit)]
    [InlineData(CloseBehavior.Ask, false, CloseAction.Ask)]
    [InlineData(CloseBehavior.Exit, false, CloseAction.Exit)]
    [InlineData(CloseBehavior.MinimizeToTray, false, CloseAction.HideToTray)]
    public void Resolve_MatchesDecisionTable(CloseBehavior behavior, bool forceExit, CloseAction expected)
        => Assert.Equal(expected, CloseDecision.Resolve(behavior, forceExit));
}
