namespace SharpBastion.ClientRequestObject;

[Serializable]
public class PythonScriptClientRunRequestObject
{
    public string scriptPath { get; set; }
    public string workingDirectory { get; set; }

    public PythonScriptClientRunRequestObject(string scriptPath, string workingDirectory)
    {
        this.scriptPath = scriptPath;
        this.workingDirectory = workingDirectory;
    }
}