namespace SharpBastion.Views;

public class PendingJobRunView
{
    public readonly string ScriptName;
    public readonly string ScriptAbsolutePath;
    public readonly TimeSpan Interval;

    public PendingJobRunView(string scriptName, string scriptAbsolutePath, TimeSpan interval)
    {
        ScriptName = scriptName;
        ScriptAbsolutePath = scriptAbsolutePath;
        Interval = interval;
    }
}