using System.Text.Json.Serialization;

namespace SharpBastion.ClientRequestObject;

[Serializable]
public class KagiClientSearchRequestObject
{
    [JsonPropertyName("query")]
    public string Query { get; set; }

    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    public KagiClientSearchRequestObject(string query, int limit = 10)
    {
        Query = query;
        Limit = limit;
    }
}