using FluentAssertions;
using ScreenMind.AI;
using ScreenMind.Core.Ai;
using ScreenMind.Core.Imaging;

namespace ScreenMind.AI.Tests;

public sealed class AiOrchestratorTests
{
    [Fact]
    public async Task AnalyzeAsyncShouldPreserveProviderStreamOrder()
    {
        FakeProvider provider = new(
            "primary",
            Emit(
                new AiStreamEvent.TextDelta("one", DateTimeOffset.UtcNow),
                new AiStreamEvent.TextDelta("two", DateTimeOffset.UtcNow),
                new AiStreamEvent.Completed(new AiUsage(), DateTimeOffset.UtcNow)));
        using AiOrchestrator orchestrator = CreateOrchestrator(provider);

        IReadOnlyList<AiStreamEvent> events = await CollectAsync(orchestrator.AnalyzeAsync(CreateRequest(), CancellationToken.None));

        events.Should().ContainInOrder(
            events.OfType<AiStreamEvent.TextDelta>().First(delta => delta.Text == "one"),
            events.OfType<AiStreamEvent.TextDelta>().First(delta => delta.Text == "two"),
            events.OfType<AiStreamEvent.Completed>().Single());
    }

    [Fact]
    public async Task AnalyzeAsyncShouldRetryTransientFailureOnce()
    {
        FakeProvider provider = new(
            "primary",
            Emit(new AiStreamEvent.Failed(new AiError(AiErrorKind.Network, "network", IsRetryable: true), DateTimeOffset.UtcNow)),
            Emit(
                new AiStreamEvent.TextDelta("ok", DateTimeOffset.UtcNow),
                new AiStreamEvent.Completed(new AiUsage(), DateTimeOffset.UtcNow)));
        using AiOrchestrator orchestrator = CreateOrchestrator(provider);

        IReadOnlyList<AiStreamEvent> events = await CollectAsync(orchestrator.AnalyzeAsync(CreateRequest(), CancellationToken.None));

        provider.Requests.Should().HaveCount(2);
        events.OfType<AiStreamEvent.TextDelta>().Single().Text.Should().Be("ok");
        events.OfType<AiStreamEvent.Failed>().Should().BeEmpty();
    }

    [Fact]
    public async Task AnalyzeAsyncShouldFallbackAfterRetryableFailure()
    {
        FakeProvider primary = new(
            "primary",
            Emit(new AiStreamEvent.Failed(new AiError(AiErrorKind.RateLimit, "slow", IsRetryable: true), DateTimeOffset.UtcNow)),
            Emit(new AiStreamEvent.Failed(new AiError(AiErrorKind.RateLimit, "slow", IsRetryable: true), DateTimeOffset.UtcNow)));
        FakeProvider fallback = new(
            "fallback",
            Emit(
                new AiStreamEvent.TextDelta("fallback", DateTimeOffset.UtcNow),
                new AiStreamEvent.Completed(new AiUsage(), DateTimeOffset.UtcNow)));
        using AiOrchestrator orchestrator = CreateOrchestrator(primary, fallback);
        AiRequest request = CreateRequest(CreateProfile() with
        {
            FallbackProviderIds = ["fallback"],
            ProviderModelIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["fallback"] = "fallback-model",
            },
        });

        IReadOnlyList<AiStreamEvent> events = await CollectAsync(orchestrator.AnalyzeAsync(request, CancellationToken.None));

        primary.Requests.Should().HaveCount(2);
        fallback.Requests.Should().HaveCount(1);
        fallback.Requests.Single().Profile.ModelId.Should().Be("fallback-model");
        events.OfType<AiStreamEvent.TextDelta>().Single().Text.Should().Be("fallback");
    }

    [Fact]
    public async Task AnalyzeAsyncShouldNotFallbackOnAuthFailure()
    {
        FakeProvider primary = new(
            "primary",
            Emit(new AiStreamEvent.Failed(new AiError(AiErrorKind.Auth, "bad key"), DateTimeOffset.UtcNow)));
        FakeProvider fallback = new(
            "fallback",
            Emit(new AiStreamEvent.TextDelta("must not run", DateTimeOffset.UtcNow)));
        using AiOrchestrator orchestrator = CreateOrchestrator(primary, fallback);
        AiRequest request = CreateRequest(CreateProfile() with { FallbackProviderIds = ["fallback"] });

        IReadOnlyList<AiStreamEvent> events = await CollectAsync(orchestrator.AnalyzeAsync(request, CancellationToken.None));

        fallback.Requests.Should().BeEmpty();
        events.OfType<AiStreamEvent.Failed>().Single().Error.Kind.Should().Be(AiErrorKind.Auth);
    }

    [Fact]
    public async Task AnalyzeAsyncShouldAllowOnlyOneMainRequest()
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeProvider provider = new("primary", BlockingStream(entered, release));
        using AiOrchestrator orchestrator = CreateOrchestrator(provider);

        await using IAsyncEnumerator<AiStreamEvent> first = orchestrator
            .AnalyzeAsync(CreateRequest(), CancellationToken.None)
            .GetAsyncEnumerator();
        Task<bool> firstMove = first.MoveNextAsync().AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        IReadOnlyList<AiStreamEvent> second = await CollectAsync(orchestrator.AnalyzeAsync(CreateRequest(), CancellationToken.None));
        release.SetResult();
        (await firstMove.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();

        second.OfType<AiStreamEvent.Failed>().Single().Error.Kind.Should().Be(AiErrorKind.Configuration);
    }

    private static AiOrchestrator CreateOrchestrator(params IAiProvider[] providers)
    {
        return new AiOrchestrator(new AiProviderRegistry(providers));
    }

    private static AiRequest CreateRequest(AiProfile? profile = null)
    {
        return new AiRequest(
            profile ?? CreateProfile(),
            new ScreenImage([1, 2, 3], "image/png", ScreenImageFormat.Png, 1, 1, DateTimeOffset.UtcNow),
            "what is on screen?",
            []);
    }

    private static AiProfile CreateProfile()
    {
        return new AiProfile("default", "Default", "primary", "primary-model", "system");
    }

    private static async Task<IReadOnlyList<AiStreamEvent>> CollectAsync(IAsyncEnumerable<AiStreamEvent> stream)
    {
        List<AiStreamEvent> events = [];
        await foreach (AiStreamEvent streamEvent in stream.ConfigureAwait(false))
        {
            events.Add(streamEvent);
        }

        return events;
    }

    private static async IAsyncEnumerable<AiStreamEvent> Emit(params AiStreamEvent[] events)
    {
        await Task.Yield();
        foreach (AiStreamEvent streamEvent in events)
        {
            yield return streamEvent;
        }
    }

    private static async IAsyncEnumerable<AiStreamEvent> BlockingStream(
        TaskCompletionSource entered,
        TaskCompletionSource release)
    {
        entered.SetResult();
        await release.Task.ConfigureAwait(false);
        yield return new AiStreamEvent.Completed(new AiUsage(), DateTimeOffset.UtcNow);
    }

    private sealed class FakeProvider : IAiProvider
    {
        private readonly Queue<IAsyncEnumerable<AiStreamEvent>> streams;

        public FakeProvider(string id, params IAsyncEnumerable<AiStreamEvent>[] streams)
        {
            Id = id;
            this.streams = new Queue<IAsyncEnumerable<AiStreamEvent>>(streams);
        }

        public string Id { get; }

        public List<AiRequest> Requests { get; } = [];

        public IAsyncEnumerable<AiStreamEvent> StreamAsync(AiRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return streams.Dequeue();
        }

        public Task<IReadOnlyList<AiModel>> GetModelsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<AiModel>>(Array.Empty<AiModel>());

        public Task TestConnectionAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
