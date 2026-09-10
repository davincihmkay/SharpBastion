using SharpBastion.ClientRequestObject;
using SharpBastion.ClientResponseObject;

namespace SharpBastion.Interface;

public interface IPythonScriptClient
{
    Task<PythonScriptClientRunResponseObject> RunScriptAsync(PythonScriptClientRunRequestObject clientRequestObject);
}