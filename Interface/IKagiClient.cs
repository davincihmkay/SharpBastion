using SharpBastion.ClientRequestObject;
using SharpBastion.ClientResponseObject;

namespace SharpBastion.Interface;

public interface IKagiClient
{
    Task<KagiClientSearchResponseObject> SearchAsync(KagiClientSearchRequestObject clientRequestObject);
}