namespace ScreenMind.Core.State;

public enum AnalysisStateKind
{
    Idle,
    Selecting,
    Capturing,
    Preprocessing,
    Sending,
    Streaming,
    Completed,
    Cancelling,
    Failed,
}

