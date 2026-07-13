using ScreenMind.Core.Ai;

namespace ScreenMind.Core.State;

public sealed class MainAnalysisStateMachine
{
    private readonly object gate = new();
    private readonly IClock clock;
    private AnalysisState current;

    public MainAnalysisStateMachine(IClock clock)
    {
        this.clock = clock;
        current = new AnalysisState.Idle(clock.GetUtcNow());
    }

    public AnalysisState Current
    {
        get
        {
            lock (gate)
            {
                return current;
            }
        }
    }

    public AnalysisStateKind CurrentKind => Current.Kind;

    public AnalysisState StartSelecting() => TransitionTo(AnalysisStateKind.Selecting);

    public AnalysisState StartCapturing() => TransitionTo(AnalysisStateKind.Capturing);

    public AnalysisState StartPreprocessing() => TransitionTo(AnalysisStateKind.Preprocessing);

    public AnalysisState StartSending() => TransitionTo(AnalysisStateKind.Sending);

    public AnalysisState StartStreaming() => TransitionTo(AnalysisStateKind.Streaming);

    public AnalysisState Complete(AiResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return TransitionTo(new AnalysisState.Completed(result, clock.GetUtcNow()));
    }

    public AnalysisState Fail(AiError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return TransitionTo(new AnalysisState.Failed(error, clock.GetUtcNow()));
    }

    public AnalysisState Cancel() => TransitionTo(AnalysisStateKind.Cancelling);

    public AnalysisState ResetToIdle() => TransitionTo(new AnalysisState.Idle(clock.GetUtcNow()));

    private AnalysisState TransitionTo(AnalysisStateKind target)
    {
        AnalysisState next = target switch
        {
            AnalysisStateKind.Idle => new AnalysisState.Idle(clock.GetUtcNow()),
            AnalysisStateKind.Selecting => new AnalysisState.Selecting(clock.GetUtcNow()),
            AnalysisStateKind.Capturing => new AnalysisState.Capturing(clock.GetUtcNow()),
            AnalysisStateKind.Preprocessing => new AnalysisState.Preprocessing(clock.GetUtcNow()),
            AnalysisStateKind.Sending => new AnalysisState.Sending(clock.GetUtcNow()),
            AnalysisStateKind.Streaming => new AnalysisState.Streaming(clock.GetUtcNow()),
            AnalysisStateKind.Cancelling => new AnalysisState.Cancelling(clock.GetUtcNow()),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Target state requires payload."),
        };

        return TransitionTo(next);
    }

    private AnalysisState TransitionTo(AnalysisState next)
    {
        lock (gate)
        {
            if (!CanTransition(current.Kind, next.Kind))
            {
                throw new InvalidOperationException(
                    $"Invalid analysis state transition from {current.Kind} to {next.Kind}.");
            }

            current = next;
            return current;
        }
    }

    private static bool CanTransition(AnalysisStateKind from, AnalysisStateKind to)
    {
        if (to == AnalysisStateKind.Cancelling)
        {
            return from is AnalysisStateKind.Selecting
                or AnalysisStateKind.Capturing
                or AnalysisStateKind.Preprocessing
                or AnalysisStateKind.Sending
                or AnalysisStateKind.Streaming;
        }

        return (from, to) switch
        {
            (AnalysisStateKind.Idle, AnalysisStateKind.Selecting) => true,
            (AnalysisStateKind.Idle, AnalysisStateKind.Capturing) => true,
            (AnalysisStateKind.Idle, AnalysisStateKind.Preprocessing) => true,
            (AnalysisStateKind.Selecting, AnalysisStateKind.Capturing) => true,
            (AnalysisStateKind.Selecting, AnalysisStateKind.Preprocessing) => true,
            (AnalysisStateKind.Capturing, AnalysisStateKind.Preprocessing) => true,
            (AnalysisStateKind.Preprocessing, AnalysisStateKind.Sending) => true,
            (AnalysisStateKind.Sending, AnalysisStateKind.Streaming) => true,
            (AnalysisStateKind.Sending, AnalysisStateKind.Completed) => true,
            (AnalysisStateKind.Streaming, AnalysisStateKind.Completed) => true,
            (AnalysisStateKind.Cancelling, AnalysisStateKind.Idle) => true,
            (AnalysisStateKind.Completed, AnalysisStateKind.Idle) => true,
            (AnalysisStateKind.Failed, AnalysisStateKind.Idle) => true,
            (_, AnalysisStateKind.Failed) when from != AnalysisStateKind.Idle => true,
            _ => false,
        };
    }
}
