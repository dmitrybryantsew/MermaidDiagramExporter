using System.Collections.Generic;

namespace MermaidDiagramExporter.Llm;

/// <summary>
/// Supported LLM providers. All providers expose an OpenAI-compatible
/// /v1/chat/completions endpoint, but with different default base URLs
/// and key handling.
/// </summary>
public enum LlmProvider
{
    /// <summary>openai-nim-proxy (http://obsidianvault.duckdns.org:3000/v1) — user's self-hosted proxy aggregatin NIM, Chutes, OpenRouter, Ollama.</summary>
    OpenAiNimProxy,

    /// <summary>OpenRouter (https://openrouter.ai/api/v1).</summary>
    OpenRouter,

    /// <summary>Chutes (https://llm.chutes.ai/v1).</summary>
    Chutes,

    /// <summary>Custom OpenAI-compatible endpoint. User configures base URL and API key.</summary>
    Custom
}

/// <summary>
/// User-configurable LLM settings. Persisted in ProjectSettings.
/// </summary>
public sealed class LlmSettings
{
    /// <summary>Selected provider.</summary>
    public LlmProvider Provider { get; set; } = LlmProvider.OpenAiNimProxy;

    /// <summary>Base URL for the /v1 endpoint. Overrides the provider default.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>API key for the selected provider.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Model name (e.g. "nim:z-ai/glm-5.1", "openai/gpt-4o-mini", "Kimi-K2.5-TEE").</summary>
    public string Model { get; set; } = "";

    /// <summary>Max tokens for the response. Default 2048.</summary>
    public int MaxTokens { get; set; } = 2048;

    /// <summary>Sampling temperature (0.0 - 2.0). Default 0.7 for creative generation.</summary>
    public double Temperature { get; set; } = 0.7;

    /// <summary>Request timeout in seconds. Default 120.</summary>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>Provider default base URLs. Override via BaseUrl field.</summary>
    public static string GetDefaultBaseUrl(LlmProvider provider) => provider switch
    {
        LlmProvider.OpenAiNimProxy => "http://obsidianvault.duckdns.org:3000/v1",
        LlmProvider.OpenRouter => "https://openrouter.ai/api/v1",
        LlmProvider.Chutes => "https://llm.chutes.ai/v1",
        LlmProvider.Custom => "http://localhost:11434/v1",
        _ => ""
    };

    /// <summary>Returns the effective base URL (custom if set, otherwise provider default).</summary>
    public string EffectiveBaseUrl =>
        string.IsNullOrWhiteSpace(BaseUrl) ? GetDefaultBaseUrl(Provider) : BaseUrl.TrimEnd('/');

    /// <summary>True if the settings have enough configuration to make a request.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Model) &&
        !string.IsNullOrWhiteSpace(EffectiveBaseUrl);
}

/// <summary>
/// A single message in a chat conversation. Matches the OpenAI Chat Completions
/// message shape: role + content. Tool calls/results are not supported.
/// </summary>
public sealed class LlmChatMessage
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";

    public LlmChatMessage() { }
    public LlmChatMessage(string role, string content)
    {
        Role = role;
        Content = content;
    }

    public static LlmChatMessage System(string content) => new("system", content);
    public static LlmChatMessage User(string content) => new("user", content);
    public static LlmChatMessage Assistant(string content) => new("assistant", content);
}
