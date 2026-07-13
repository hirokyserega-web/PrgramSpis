# Architecture

## Phase 01 dependency graph

`ScreenMind.Core` is the bottom layer. It has no references to UI, Windows APIs, HTTP packages, filesystem packages, or concrete provider projects.

`ScreenMind.AI` references `ScreenMind.Core` and owns future AI-facing contracts.

`ScreenMind.UI`, `ScreenMind.Infrastructure`, `ScreenMind.Platform.Windows`, and provider projects reference lower-level contracts only. Windows-specific code is isolated in `ScreenMind.Platform.Windows`.

`ScreenMind.App` is the composition root. It references all runtime modules and wires their dependency-injection registration methods.

## Package policy

Package versions are centralized in `Directory.Packages.props`. Microsoft.Extensions packages are pinned to the `8.x` line for .NET 8 LTS compatibility. Avalonia is pinned to `11.x`.

## Phases 02-05

Core now contains:

- AI/image/message/result/profile/error models
- platform contracts for capture, hotkeys, settings, secrets, tray, and capture exclusion
- `MainAnalysisStateMachine` with explicit transitions and cancellation state
- typed settings defaults and validation

Infrastructure contains JSON settings persistence. Writes use a temporary file and atomic replacement with backup. If the main settings file is corrupted, the store attempts backup recovery before recreating defaults.

Windows platform contains:

- Credential Manager backed `ISecretStore`
- `RegisterHotKey` backed `IHotkeyService` on a dedicated message thread
- `NotifyIcon` backed tray service
- in-memory screen capture for active window, monitor with cursor, explicit monitor, and region targets

Capture implementation note: Windows Graphics Capture is the intended modern capture API for the richer UI pipeline. Current phase uses GDI/System.Drawing fallback because it is small, testable, and keeps bitmap lifetime explicit while the Avalonia capture overlay does not exist yet.

## Phases 06-11

UI now contains a region-selection overlay service and a code-only Avalonia transparent topmost window. It models drag, resize, Enter confirm, and Esc cancel behavior without direct WinAPI calls. Selection results are returned in screen-pixel coordinates so capture services can run after the overlay closes.

Infrastructure contains `SkiaSharpImagePreprocessor`. It decodes in memory, applies optional masks, enforces resize bounds and payload limits, and emits PNG, JPEG, or WebP. Screen buffers are not written to disk.

`ScreenMind.AI` contains the main orchestrator. It keeps one active analysis at a time, preserves provider chunk order, supports cancellation through `CancellationToken`, allows one automatic retry for transient errors, and falls back only for retryable network, timeout, rate limit, and temporary service errors. Auth and configuration errors do not switch providers.

Provider adapters:

- OpenAI uses the official Responses API with SSE streaming and multimodal image input.
- OpenAI-compatible uses chat-completions style streaming with configurable base URL.
- Anthropic uses Messages API streaming with base64 image content blocks.
- Gemini uses `streamGenerateContent` with inline image data and maps safety blocks to `AiErrorKind.SafetyBlocked`.
- Ollama uses local `/api/generate` streaming and `/api/tags` model discovery. It never downloads models or starts external processes.

API references used for adapter contracts:

- https://developers.openai.com/api/reference/resources/responses/methods/create
- https://developers.openai.com/api/reference/resources/responses/streaming-events
- https://platform.claude.com/docs/en/api/messages
- https://platform.claude.com/docs/en/build-with-claude/vision
- https://ai.google.dev/api/generate-content
- https://ai.google.dev/gemini-api/docs/file-input-methods
- https://raw.githubusercontent.com/ollama/ollama/main/docs/api.md

## Phase 12

UI contains a compact overlay service and a code-only topmost window. It provides a non-blocking UI for displaying streamed AI analysis outputs. The VM orchestrates the entire screen capture, image preprocessing, and AI streaming flow using the `MainAnalysisStateMachine`, handling cancellation, retries, copying to clipboard, and error states.

