namespace SharpBastion.ClientResponseObject;

public class PythonScriptClientRunResponseObject
{
    public string StandardOutput { get; set; }
    public string StandardError { get; set; }
    public int ExitCode { get; set; }
}