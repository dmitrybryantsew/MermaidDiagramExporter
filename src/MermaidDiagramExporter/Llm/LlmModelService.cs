using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MermaidDiagramExporter.Llm;

/// <summary>
/// Fetches the available model list from an OpenAI-compatible
/// /v1/models endpoint. Used to populate the model picker dropdown.
/// </summary>
public sealed class LlmModelService
{
    private readonly LlmSettings _settings;
    private readonly HttpClient _http;

    public LlmModelService(LlmSettings settings, HttpClient? httpClient = null)
    {
        _settings = settings;
        _http = httpClient ?? new HttpClient();
    }

    /// <summary>
    /// Returns the list of model IDs available on the configured provider.
    /// Returns an empty list on failure (network or auth) — callers should
    /// fall back to manual model entry.
    /// </summary>
    public async Task<List<string>> ListModelsAsync(CancellationToken ct = default)
    {
        var url = $"{_settings.EffectiveBaseUrl}/models";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);

        try
        {
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return new List<string>();

            var body = await response.Content.ReadAsStringAsync(ct);
            var doc = JsonSerializer.Deserialize<ModelsResponseDto>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return doc?.Data?.Select(m => m.Id).Where(s => !string.IsNullOrWhiteSpace(s)).ToList()
                   ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private sealed class ModelsResponseDto
    {
        [JsonPropertyName("data")] public List<ModelDto>? Data { get; set; }
    }

    private sealed class ModelDto
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
    }
}
