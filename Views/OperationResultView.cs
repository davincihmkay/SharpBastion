namespace SharpBastion.Views;

public class OperationResultView
{
    public readonly bool Success;
    public readonly string Message;

    public OperationResultView(bool success, string message)
    {
        Success = success;
        Message = message;
    }
}