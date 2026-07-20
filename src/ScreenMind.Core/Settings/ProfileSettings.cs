using ScreenMind.Core.Ai;
using System.Collections.Generic;

namespace ScreenMind.Core.Settings;

public sealed class ProfileSettings
{
    public string SelectedProfileId { get; set; } = "universal";

    public List<AiProfile> Items { get; set; } = [];

    public static ProfileSettings CreateDefault()
    {
        return new ProfileSettings
        {
            Items =
            [
                new("universal", "Universal", "openai", "gpt-4o-mini", "Analyze the screenshot and answer clearly."),
                new("programming", "Programming", "openai", "gpt-4o-mini", "Focus on code, errors, architecture, and exact fixes."),
                new("explain", "Explain", "openai", "gpt-4o-mini", "Explain what is visible in concise, plain language."),
                new("brief", "Brief", "openai", "gpt-4o-mini", "Answer briefly. Keep only decisive details."),
                new("translate", "Translate", "openai", "gpt-4o-mini", "Translate visible text and preserve formatting where useful."),
                new("document", "Document", "openai", "gpt-4o-mini", "Extract document structure, key facts, and action items."),
                new("uiux", "UI/UX", "openai", "gpt-4o-mini", "Review UI quality, hierarchy, accessibility, and usability."),
                new("qwen-free", "Qwen Free (FreeQwenApi)", "openai-compatible", "qwen3.7-max", "Analyze the screenshot and answer clearly."),
                new("qwen3.8-max-preview", "Qwen 3.8 Max Preview (FreeQwenApi)", "openai-compatible", "qwen3.8-max-preview", "Analyze the screenshot and answer clearly."),
                new("qwen-vl", "Qwen Vision (FreeQwenApi)", "openai-compatible", "qwen3.8-max-preview", "Analyze the image and describe what you see in detail."),
                new("deepseek-free", "Deepseek Free (FreeDeepseekAPI)", "openai-compatible", "deepseek-reasoner", "Analyze the screenshot and solve step-by-step."),
                new("kimi-free", "Kimi Free (FreeGLMKimiAPI)", "openai-compatible", "kimi", "Analyze the screenshot and translate or summarize clearly."),
                new("notion-free", "Notion AI (notion-2api)", "openai-compatible", "claude-sonnet-4.5", "Analyze the screenshot and answer clearly."),
            ],
        };
    }
}
