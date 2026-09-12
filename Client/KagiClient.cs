using System.Net.Http.Headers;
using System.Net.Http.Json;
using SharpBastion.ClientRequestObject;
using SharpBastion.ClientResponseObject;
using SharpBastion.Interface;

namespace SharpBastion.Client;

public class KagiClient : IKagiClient
{
    private const string ApiKeyEnvVar = "KAGI_API_KEY";
    private static readonly Uri BaseUrl = new("https://kagi.com/api/v1/");

    private readonly HttpClient _httpClient;

    public KagiClient(IHttpClientFactory clientFactory)
    {
        var apiKey = Environment.GetEnvironmentVariable(ApiKeyEnvVar);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException(
                $"'{ApiKeyEnvVar}' is not set — SharpBastion cannot authenticate against the Kagi API.\n" +
                $"Obtain a key from https://kagi.com/settings?p=api and set the '{ApiKeyEnvVar}' " +
                "environment variable (per-shell or per-process — never committed to config).");

        _httpClient = clientFactory.CreateClient();
        _httpClient.BaseAddress = BaseUrl;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bot", apiKey);
    }

    public async Task<KagiClientSearchResponseObject> SearchAsync(KagiClientSearchRequestObject clientRequestObject)
    {
        using var httpResponse = await _httpClient.PostAsJsonAsync("search", clientRequestObject);

        if (!httpResponse.IsSuccessStatusCode)
        {
            var errorMessage = await TryReadErrorMessageAsync(httpResponse);
            throw new HttpRequestException(
                $"Kagi search request failed with status {(int)httpResponse.StatusCode} {httpResponse.StatusCode}" +
                (errorMessage is null ? "." : $": {errorMessage}"),
                null,
                httpResponse.StatusCode);
        }

        var result = await httpResponse.Content.ReadFromJsonAsync<KagiClientSearchResponseObject>();
        if (result is null)
            throw new ArgumentException("No response from Kagi search, exception occurred");

        return result;
    }

    private static async Task<string?> TryReadErrorMessageAsync(HttpResponseMessage httpResponse)
    {
        try
        {
            var errorBody = await httpResponse.Content.ReadFromJsonAsync<KagiErrorResponseObject>();
            var firstError = errorBody?.Error?.FirstOrDefault();
            return firstError?.Message ?? firstError?.Code;
        }
        catch
        {
            return null;
        }
    }
}