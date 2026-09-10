namespace SharpBastion.ClientRequestObject;

[Serializable]
public class LmStudioClientChatPromptClientRequestObject
{
        public string? model { get; set; }
        public string? system_prompt { get; set; }
        // public Dictionary<string, string> input { get; set; }
        public string input { get; set; }
        public string previous_response_id { get; set; }
             
        public LmStudioClientChatPromptClientRequestObject(string modelName, string systemPrompt, string prompt, string responseId)
        {
            this.model = modelName;
            this.system_prompt = systemPrompt;
            this.previous_response_id = responseId;
            // this.input = new Dictionary<string, string>()
            // {
            //     { "type", "text" },
            //     { "content", prompt }
            // };
            this.input = prompt;
        }
        
        public LmStudioClientChatPromptClientRequestObject(){}

        public LmStudioClientChatPromptClientRequestObject(string modelName, string prompt, string responseId)
        {
            this.model = modelName;
            this.previous_response_id = responseId;
            // this.input = new Dictionary<string, string>()
            // {
            //     { "type", "text" },
            //     { "content", prompt }
            // };        
            this.input = prompt;
        }
}