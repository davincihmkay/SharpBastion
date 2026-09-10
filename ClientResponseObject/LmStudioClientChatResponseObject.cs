namespace SharpBastion.ClientResponseObject;

public class LmStudioClientChatResponseObject
{
    public string? mode_instance_id { get; set; }
    public List<Response> output { get; set; }
    public string? response_id { get; set; }


    public class Response
    {
        public string? type { get; set; }
        public string? content { get; set; }
    }
}