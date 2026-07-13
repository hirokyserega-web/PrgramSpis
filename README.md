# ScreenMind

ScreenMind is a Windows-first personal AI assistant for explicit, user-triggered screen analysis. The first release targets Windows 10/11 with .NET 8, Avalonia UI 11, MVVM, typed dependency injection, and provider adapters behind common contracts.

## Current scope

Implemented so far:

- production projects under `src/`
- test projects under `tests/`
- central package management through `Directory.Packages.props`
- shared build rules through `Directory.Build.props`
- basic dependency-injection composition without business implementations
- Core domain models, contracts, and main analysis state machine
- typed settings with JSON persistence, atomic writes, backup, and recovery
- Windows Credential Manager secret store
- Windows global hotkey service based on `RegisterHotKey`
- Windows tray service based on `NotifyIcon`
- Windows active-window and monitor capture service using in-memory PNG payloads
- Avalonia region selection overlay for drag, resize, Enter confirm, and Esc cancel workflows
- in-memory image preprocessing with resize, payload limit, PNG/JPEG/WebP output, and fill/blur/exclude masks
- AI orchestrator with streaming, cancellation, one active analysis gate, retry, and provider fallback rules
- OpenAI, OpenAI-compatible, Anthropic, Gemini, and Ollama provider adapters with mock-tested streaming contracts

## Build

Use .NET 8 SDK.

```powershell
dotnet restore
dotnet format --verify-no-changes
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

## Architecture rules

- `ScreenMind.Core` stays independent from UI, Windows APIs, HTTP, filesystem access, and concrete AI providers.
- `ScreenMind.UI` does not call WinAPI directly.
- Platform-specific functionality lives behind interfaces and platform projects.
- Provider projects plug into `ScreenMind.AI` contracts and use `HttpClientFactory`.
- HTTP adapters receive keys through `ISecretStore`; no API key belongs in JSON, source, or logs.
- `ScreenMind.App` is the composition root.
- Captures, prompts, responses, session history, and API keys must not be logged or persisted between launches.

## Privacy notes

- Capture happens only when application code explicitly calls `IScreenCaptureService`.
- Settings JSON does not contain provider API keys.
- API keys are stored through `ISecretStore`; Windows implementation uses Credential Manager.
- Screenshots are returned as in-memory buffers and are not written to disk by capture services.
- Logs must not include screenshot bytes, prompts, responses, API keys, or Authorization headers.
