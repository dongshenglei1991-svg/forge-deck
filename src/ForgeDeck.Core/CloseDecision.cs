namespace ForgeDeck.Core;

public enum CloseAction { Ask, Exit, HideToTray }

public static class CloseDecision
{
    public static CloseAction Resolve(CloseBehavior behavior, bool forceExit)
        => forceExit ? CloseAction.Exit
         : behavior == CloseBehavior.MinimizeToTray ? CloseAction.HideToTray
         : behavior == CloseBehavior.Ask ? CloseAction.Ask
         : CloseAction.Exit;
}
