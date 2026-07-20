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

        output.Should().Be("reasoning\nFinal Answer:\nUseful answer");
    }

    [Fact]
    public void QwenRussianReasoningIsBuffered()
    {
        StreamingAnswerFilter filter = new(suppressUntaggedReasoning: true);

        string output = PushAll(filter, "Пользователь просит...\nНужно определить...\nОкончательный ответ:\nНа скриншоте открыт браузер.");

        output.Should().Be("Пользователь просит...\nНужно определить...\nОкончательный ответ:\nНа скриншоте открыт браузер.");
    }

    [Fact]
    public void QwenEnglishReasoningIsBuffered()
    {
        StreamingAnswerFilter filter = new(suppressUntaggedReasoning: true);

        string output = PushAll(filter, "The user asks for analysis.\nWe need to inspect it.\nFinal:\nThe browser is open.");

        output.Should().Be("The user asks for analysis.\nWe need to inspect it.\nFinal:\nThe browser is open.");
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
        StreamingAnswerFilter filter = new();
        filter.Push("Пользователь просит объяснение. Нужно изучить изображение.");
        string output = filter.Complete();

        output.Should().BeEmpty();
        filter.AnswerText.Should().Be("Пользователь просит объяснение. Нужно изучить изображение.");
    }

    [Fact]
    public void UnclosedExplicitReasoningDoesNotLeakIntoAnswer()
    {
        StreamingAnswerFilter filter = new();
        filter.Push("<think>internal only");

        filter.Complete().Should().Be("<think>internal only");
        filter.IsReasoningOnly.Should().BeFalse();
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

        PushAll(filter, "Пользователь просит", " изучить. Фин", "al Answer:", " answer").Should().Be("Пользователь просит изучить. Финal Answer: answer");
    }

    [Fact]
    public void MarkdownInsideFinalAnswerIsPreserved()
    {
        StreamingAnswerFilter filter = new();

        PushAll(filter, "<final>**Heading**\n\n- item</final>").Should().Be("<final>**Heading**\n\n- item</final>");
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
    public void SingleLetterAndOrdinaryContentAreNotReasoning()
    {
        StreamingAnswerFilter filter = new();

        filter.Push("Пользователь просит что-то.").Should().Be("Пользователь просит что-то.");
        filter.AnswerText.Should().Be("Пользователь просит что-то.");
    }

    [Fact]
    public void SingleLetterContentChunkIsNotReasoning()
    {
        StreamingAnswerFilter filter = new();

        filter.Push("П").Should().Be("П");
        filter.AnswerText.Should().Be("П");
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
