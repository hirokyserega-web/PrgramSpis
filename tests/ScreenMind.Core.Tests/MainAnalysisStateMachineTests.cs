using FluentAssertions;
using ScreenMind.Core.Ai;
using ScreenMind.Core.State;

namespace ScreenMind.Core.Tests;

public sealed class MainAnalysisStateMachineTests
{
    private readonly FixedClock clock = new();

    [Fact]
    public void StateMachineShouldAllowMainHappyPath()
    {
        MainAnalysisStateMachine stateMachine = new(clock);

        stateMachine.StartCapturing();
        stateMachine.StartPreprocessing();
        stateMachine.StartSending();
        stateMachine.StartStreaming();
        stateMachine.Complete(new AiResult("done", new AiUsage()));

        stateMachine.CurrentKind.Should().Be(AnalysisStateKind.Completed);
    }

    [Fact]
    public void StateMachineShouldRejectParallelMainRequest()
    {
        MainAnalysisStateMachine stateMachine = new(clock);

        stateMachine.StartCapturing();

        Action action = () => stateMachine.StartCapturing();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Capturing to Capturing*");
    }

    [Fact]
    public void StateMachineShouldSupportCancellationFromActiveStates()
    {
        MainAnalysisStateMachine stateMachine = new(clock);

        stateMachine.StartCapturing();
        stateMachine.Cancel();
        stateMachine.ResetToIdle();

        stateMachine.CurrentKind.Should().Be(AnalysisStateKind.Idle);
    }

    [Fact]
    public void StateMachineShouldRejectInvalidDirectTransition()
    {
        MainAnalysisStateMachine stateMachine = new(clock);

        Action action = () => stateMachine.StartStreaming();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Idle to Streaming*");
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset GetUtcNow() => new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }
}

