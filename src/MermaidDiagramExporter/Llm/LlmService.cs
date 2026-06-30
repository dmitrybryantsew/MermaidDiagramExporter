using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MermaidDiagramExporter.Llm;

/// <summary>
/// Calls an OpenAI-compatible /v1/chat/completions endpoint.
/// Works with: OpenRouter, Chutes, NIM, Ollama, openai-nim-proxy,
/// and any other OpenAI-shaped API.
///
/// Supports streaming via <see cref="ChatStreamAsync"/> for real-time
/// token output, and non-streaming via <see cref="ChatAsync"/>.
/// </summary>
public sealed class LlmService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    private readonly LlmSettings _settings;
    private readonly HttpClient _http;

    public LlmService(LlmSettings settings, HttpClient? httpClient = null)
    {
        _settings = settings;
        _http = httpClient ?? new HttpClient();
    }

    /// <summary>
    /// Sends a chat completion request and returns the assistant's
    /// message content as a string. Throws on HTTP errors.
    /// </summary>
    public async Task<string> ChatAsync(
        IReadOnlyList<LlmChatMessage> messages,
        CancellationToken ct = default)
    {
        var request = new ChatRequestDto
        {
            Model = _settings.Model,
            Messages = messages.Select(m => new ChatMessageDto { Role = m.Role, Content = m.Content }).ToList(),
            MaxTokens = _settings.MaxTokens,
            Temperature = _settings.Temperature,
            Stream = false
        };

        var url = $"{_settings.EffectiveBaseUrl}/chat/completions";
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_settings.TimeoutSeconds));

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);

        // OpenRouter wants these headers (other providers ignore them)
        if (_settings.Provider == LlmProvider.OpenRouter)
        {
            httpRequest.Headers.TryAddWithoutValidation("HTTP-Referer", "http://localhost/mermaid-exporter");
            httpRequest.Headers.TryAddWithoutValidation("X-Title", "Mermaid Diagram Exporter");
        }

        using var response = await _http.SendAsync(httpRequest, timeoutCts.Token);
        response.EnsureSuccessStatusCode();

        var chatResponse = await response.Content.ReadFromJsonAsync<ChatResponseDto>(JsonOptions, timeoutCts.Token)
            ?? throw new InvalidOperationException("Empty response from LLM");

        return chatResponse.Choices?.FirstOrDefault()?.Message?.Content ?? "";
    }

    /// <summary>
    /// Sends a streaming chat completion request. Yields each token
    /// delta as a string. The final yield is the complete accumulated
    /// response; the caller should stop iterating after that.
    /// </summary>
    public async IAsyncEnumerable<string> ChatStreamAsync(
        IReadOnlyList<LlmChatMessage> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken ct = default)
    {
        var request = new ChatRequestDto
        {
            Model = _settings.Model,
            Messages = messages.Select(m => new ChatMessageDto { Role = m.Role, Content = m.Content }).ToList(),
            MaxTokens = _settings.MaxTokens,
            Temperature = _settings.Temperature,
            Stream = true
        };

        var url = $"{_settings.EffectiveBaseUrl}/chat/completions";
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_settings.TimeoutSeconds));

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
        using var reader = new System.IO.StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(timeoutCts.Token)) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith(":")) continue; // SSE comment

            // SSE format: "data: {json}" or "data: [DONE]"
            const string prefix = "data: ";
            if (!line.StartsWith(prefix)) continue;
            var payload = line.Substring(prefix.Length).Trim();
            if (payload == "[DONE]") yield break;

            StreamChunkDto? chunk;
            try { chunk = JsonSerializer.Deserialize<StreamChunkDto>(payload, JsonOptions); }
            catch { continue; }

            var delta = chunk?.Choices?.FirstOrDefault()?.Delta?.Content;
            if (!string.IsNullOrEmpty(delta)) yield return delta;
        }
    }

    // ── Wire-format DTOs ──

    private sealed class ChatRequestDto
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("messages")] public List<ChatMessageDto> Messages { get; set; } = new();
        [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; }
        [JsonPropertyName("temperature")] public double Temperature { get; set; }
        [JsonPropertyName("stream")] public bool Stream { get; set; }
    }

    private sealed class ChatMessageDto
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "";
        [JsonPropertyName("content")] public string Content { get; set; } = "";
    }

    private sealed class ChatResponseDto
    {
        [JsonPropertyName("choices")] public List<ChatChoiceDto>? Choices { get; set; }
    }

    private sealed class ChatChoiceDto
    {
        [JsonPropertyName("message")] public ChatMessageDto? Message { get; set; }
        [JsonPropertyName("finish_reason")] public string? FinishReason { get; set; }
    }

    private sealed class StreamChunkDto
    {
        [JsonPropertyName("choices")] public List<StreamChoiceDto>? Choices { get; set; }
    }

    private sealed class StreamChoiceDto
    {
        [JsonPropertyName("delta")] public ChatMessageDto? Delta { get; set; }
    }
}
