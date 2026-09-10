using SharpBastion.ClientRequestObject;
using SharpBastion.ClientResponseObject;

namespace SharpBastion.Interface;

public interface IRepomixCliClient
{
    Task<RepomixCliClientPackageRepositoryResponseObject> PackageRepositoryToXml(RepomixCliClientPackageRepositoryRequestObject clientRequestObject);
    bool IsInitialized();
}