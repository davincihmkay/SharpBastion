using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SharpBastion.ClientRequestObject;
using SharpBastion.ClientResponseObject;
using SharpBastion.Interface;

namespace SharpBastion.Client;

public class LmStudioClient : ILmStudioClient
{
    private readonly HttpClient _httpClient;
    private readonly HttpClient _httpClientHealthCheck;
    private static string LMSTUDIO_URL = "http://localhost:1234/api/v1/chat";

    public LmStudioClient(IHttpClientFactory clientFactory)
    {
        _httpClient = clientFactory.CreateClient();
        _httpClientHealthCheck = clientFactory.CreateClient("HealthCheck");
        _httpClient.Timeout =  new TimeSpan(0, 5, 0);
    }

    public LmStudioClientChatResponseObject SendMessageData(LmStudioClientChatPromptClientRequestObject clientRequestObject)
    {
        var uri = new Uri(LMSTUDIO_URL);
        string jsonString = JsonSerializer.Serialize(clientRequestObject, new JsonSerializerOptions() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        var content = new StringContent(jsonString, Encoding.UTF8, "application/json");
        var response =  _httpClient.PostAsync(uri, content).ConfigureAwait(false).GetAwaiter().GetResult();

        response.EnsureSuccessStatusCode();
        
        var chatResponse = response.Content.ReadFromJsonAsync<LmStudioClientChatResponseObject>().Result;
        if (chatResponse is null)
        {
            throw new ArgumentException("No response from chat, exception occurred");
        }
        return chatResponse;
    }

    public async Task<bool> IsOnline()
    {
        try
        {
            var response = await _httpClientHealthCheck.GetAsync("http://localhost:1234/v1/models");
            return response.IsSuccessStatusCode;
        } 
        catch (HttpRequestException e) { return false; }
    }
}