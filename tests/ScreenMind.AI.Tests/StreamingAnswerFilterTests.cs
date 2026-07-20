using FluentAssertions;
using ScreenMind.Core.Ai;

namespace ScreenMind.AI.Tests;

public sealed class StreamingAnswerFilterTests
{
    [Fact]
    public void ThinkBlockIsHidden()
    {
        StreamingAnswerFilter filter = new();

        string output = PushAll(filter, "<think>internal reasoning</think>Actual answer");

        output.Should().Be("Actual answer");
    }

    [Fact]
    public void SplitThinkTagsAcrossChunksAreHandled()
    {
        StreamingAnswerFilter filter = new();

        string output = PushAll(filter, "<thi", "nk>reason", "ing</th", "ink>Answer");

        output.Should().Be("Answer");
    }

    [Fact]
    public void ThoughtAndAnalysisTagsAreHidden()
    {
        StreamingAnswerFilter filter = new();

        string output = PushAll(filter, "<thought>x</thought>A<analysis>y</analysis>B<reasoning>z</reasoning>C");

        output.Should().Be("ABC");
    }

    [Fact]
    public void FinalAnswerMarkerReturnsTextAfterMarker()
    {
        StreamingAnswerFilter filter = new(suppressUntaggedReasoning: true);

        string output = PushAll(filter, "reasoning\nFinal Answer:\nUseful answer");

        output.Should().Be("Useful answer");
    }

    [Fact]
    public void QwenRussianReasoningIsBuffered()
    {
        StreamingAnswerFilter filter = new(suppressUntaggedReasoning: true);

        string output = PushAll(filter, "Пользователь просит...\nНужно определить...\nОкончательный ответ:\nНа скриншоте открыт браузер.");

        output.Should().Be("На скриншоте открыт браузер.");
    }

    [Fact]
    public void QwenEnglishReasoningIsBuffered()
    {
        StreamingAnswerFilter filter = new(suppressUntaggedReasoning: true);

        string output = PushAll(filter, "The user asks for analysis.\nWe need to inspect it.\nFinal:\nThe browser is open.");

        output.Should().Be("The browser is open.");
    }

    [Fact]
    public void NormalAnswerStreamsImmediately()
    {
        StreamingAnswerFilter filter = new();

        filter.Push("На скриншоте ").Should().Be("На скриншоте ");
        filter.Push("открыт браузер.").Should().Be("открыт браузер.");
    }

    [Fact]
    public void NoFinalMarkerDoesNotLoseText()
    {
        StreamingAnswerFilter filter = new(suppressUntaggedReasoning: true);
        filter.Push("Пользователь просит объяснение. Нужно изучить изображение.");

        string output = filter.Complete();

        output.Should().NotBeEmpty();
        filter.AnswerText.Should().Contain("Пользователь просит");
    }

    [Fact]
    public void ReasoningOnlyResponseReturnsControlledFallbackState()
    {
        StreamingAnswerFilter filter = new();
        filter.Push("<think>internal only");

        filter.Complete().Should().BeEmpty();
        filter.IsReasoningOnly.Should().BeTrue();
    }

    [Fact]
    public void MultipleThinkBlocksAreHidden()
    {
        StreamingAnswerFilter filter = new();

        PushAll(filter, "<think>a</think>one<think>b</think>two").Should().Be("onetwo");
    }

    [Fact]
    public void TagsAreCaseInsensitive()
    {
        StreamingAnswerFilter filter = new();

        PushAll(filter, "<THINK>x</tHiNk>Answer").Should().Be("Answer");
    }

    [Fact]
    public void MarkerSplitAcrossChunksIsHandled()
    {
        StreamingAnswerFilter filter = new(suppressUntaggedReasoning: true);

        PushAll(filter, "Пользователь просит", " изучить. Фин", "al Answer:", " answer").Should().Be(" answer");
    }

    [Fact]
    public void MarkdownInsideFinalAnswerIsPreserved()
    {
        StreamingAnswerFilter filter = new();

        PushAll(filter, "<final>**Heading**\n\n- item</final>").Should().Be("**Heading**\n\n- item");
    }

    [Fact]
    public void CodeBlocksInsideAnswerArePreserved()
    {
        StreamingAnswerFilter filter = new();

        PushAll(filter, "<think>x</think>```csharp\nvar x = 1;\n```").Should().Be("```csharp\nvar x = 1;\n```");
    }

    [Fact]
    public void EmptyChunksDoNotBreakState()
    {
        StreamingAnswerFilter filter = new();

        filter.Push(string.Empty).Should().BeEmpty();
        PushAll(filter, "<thi", string.Empty, "nk>x</think>", string.Empty, "answer").Should().Be("answer");
    }

    [Fact]
    public void CancellationDoesNotPublishBufferedReasoning()
    {
        StreamingAnswerFilter filter = new(suppressUntaggedReasoning: true);

        filter.Push("Пользователь просит что-то.").Should().BeEmpty();
        filter.AnswerText.Should().BeEmpty();
    }

    [Fact]
    public void OtherProvidersDoNotUseQwenHeuristics()
    {
        StreamingAnswerFilter filter = new();

        PushAll(filter, "Пользователь просит полезный ответ.").Should().Be("Пользователь просит полезный ответ.");
    }

    private static string PushAll(StreamingAnswerFilter filter, params string[] chunks)
    {
        string output = string.Concat(chunks.Select(filter.Push));
        return output + filter.Complete();
    }
}
