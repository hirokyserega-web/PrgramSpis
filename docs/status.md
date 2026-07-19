# ScreenMind Status

Snapshot of work done and what remains.

## Done

- Phase 01: solution scaffold, production and test projects, central package management, shared build props, README, DI composition root.
- Phase 02: Core models, interfaces, and main analysis state machine.
- Phase 03: typed settings, validation, schema versioning, JSON persistence, atomic write, backup recovery, Windows secret storage.
- Phase 04: global hotkeys and tray integration on Windows.
- Phase 05: active window and monitor capture on Windows, in-memory buffers, cancellation support.
- Phase 06: region selection overlay, drag/resize/confirm/cancel, coordinate handling, preprocessing pipeline, payload limits, masks.
- Phase 07: AI orchestrator with streaming, single active request gate, retry/fallback rules, cancellation, normalized errors.
- Phase 08: OpenAI and OpenAI-compatible provider adapters with streaming and contract tests.
- Phase 09: Anthropic provider adapter with streaming and contract tests.
- Phase 10: Gemini provider adapter with multimodal streaming, safety mapping, and contract tests.
- Phase 11: Ollama provider adapter with local streaming, model discovery, and contract tests.
- Phase 12: compact overlay UI for live streaming, cancel, retry, copy, expand, close, and non-blocking response display.
- Phase 13: full chat view, in-memory session flow, profiles, and settings surfaces.
- Phase 14: window capture exclusion best-effort (via SetWindowDisplayAffinity API), forbidden window/process checks, privacy warning before cloud send.
- Phase 15: structured diagnostics, redaction, disposal/shutdown hardening, and broader test coverage.
- Phase 16: GitHub Actions CI.
- Phase 17: Windows installer (Inno Setup configuration).
- Phase 18: GitHub Releases and auto-update pipeline (automated release workflow).
- Documentation: architecture notes and public scope updated.
- Verification: `dotnet restore`, `dotnet format --verify-no-changes`, `dotnet build -c Release --no-restore`, `dotnet test -c Release --no-build`, publish, and smoke run passed.

## Remaining

- None (all core and additional phases successfully implemented).

## Correction stage 01

- Added persistent `AlwaysOnTop` preference. Chat applies it immediately; compact overlay provides a per-window Pin toggle.
- Added editable system prompt for every selected profile. Same prompt is sent for text-only and screenshot requests through `AiRequest.Profile`.
- Added structured assistant-message rendering for headings, paragraphs, ordered/unordered lists, quotes, and fenced code blocks.
- Improved chat message spacing, line height, code typography, and selectable response text.
- Added regression coverage for default window and prompt preferences.

## Correction stage 02

- Reworked settings from one long scrolling form into focused tabs: AI, Appearance, Capture, Providers, Proxies, Privacy, and Hotkeys.
- Preserved existing provider, managed proxy, cookie, hotkey, privacy, and capture behavior while changing presentation.
- Added bounded settings content width and independent scrolling per section.
- Verified updated desktop application starts and remains running after publish.

## Correction stage 03

- Redesigned chat and compact overlay around a neutral charcoal palette with teal actions and clearer contrast.
- Increased usable chat workspace, tightened sidebar, improved header, composer, spacing, and responsive minimum dimensions.
- Added a proper empty conversation state and larger readable assistant messages.
- Added real in-memory screenshot previews to chat context instead of dimension-only placeholders.
- Added explicit preview bitmap ownership and disposal during session changes and window shutdown.
- Refined compact overlay dimensions, header, action bar, typography, and pin state.

## Correction stage 04

- Fixed active-window capture when a ScreenMind window owns foreground focus by selecting the next visible non-ScreenMind window.
- Removed the swallowed-capture-error path that reopened overlay and attempted to capture ScreenMind itself.
- Capture failures now appear as non-modal inline overlay errors.
- Removed blocking cloud-upload warning flow from chat and compact overlay.
- Added explicit Select area, Active window, and Current monitor actions to main sidebar.
- Main chat hides before window/monitor capture and can be restored correctly from compact overlay.
- Redesigned area-selection overlay with stronger dimming, teal selection border, size badge, crosshair, and concise instructions.
- Local Windows pixel-capture smoke passed and produced an in-memory-compatible PNG.

## Notes

- No screen contents, prompts, responses, or API keys are stored in logs or persisted between runs by the implemented parts.
- UI is code-only Avalonia. Moving large views to AXAML remains useful future cleanup, but is not required for runtime behavior.
- Windows capture exclusion remains best-effort and does not guarantee invisibility in every capture path.
