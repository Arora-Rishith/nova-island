namespace NovaIsland;

public partial class MainWindow
{
    private readonly Services.FocusSession focusSession = new();
    private object GetFocusState()
    {
        var state = focusSession.Snapshot();
        return new { phase = state.Phase, remaining = state.Remaining, duration = state.Duration };
    }
}
