using ScreenMind.Core.Ai;

namespace ScreenMind.Core.State;

public abstract record AnalysisState(AnalysisStateKind Kind, DateTimeOffset EnteredAt)
{
    public sealed record Idle(DateTimeOffset Entered) : AnalysisState(AnalysisStateKind.Idle, Entered);

    public sealed record Selecting(DateTimeOffset Entered) : AnalysisState(AnalysisStateKind.Selecting, Entered);

    public sealed record Capturing(DateTimeOffset Entered) : AnalysisState(AnalysisStateKind.Capturing, Entered);

    public sealed record Preprocessing(DateTimeOffset Entered) : AnalysisState(AnalysisStateKind.Preprocessing, Entered);

    public sealed record Sending(DateTimeOffset Entered) : AnalysisState(AnalysisStateKind.Sending, Entered);

    public sealed record Streaming(DateTimeOffset Entered) : AnalysisState(AnalysisStateKind.Streaming, Entered);

    public sealed record Completed(AiResult Result, DateTimeOffset Entered)
        : AnalysisState(AnalysisStateKind.Completed, Entered);

    public sealed record Cancelling(DateTimeOffset Entered) : AnalysisState(AnalysisStateKind.Cancelling, Entered);

    public sealed record Failed(AiError Error, DateTimeOffset Entered)
        : AnalysisState(AnalysisStateKind.Failed, Entered);
}

