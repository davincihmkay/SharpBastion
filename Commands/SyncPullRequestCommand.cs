using SharpBastion.RequestObject;
using SharpBastion.ValueObjects;

namespace SharpBastion.Commands;

public class SyncPullRequestCommand
{
    public required LocalPath RepositoryPath;
    public required int PullRequestNumber;

    public static SyncPullRequestCommand PopulateFromRequestObject(SyncPullRequestRequestObject requestObject)
    {
        if (requestObject.PullRequestNumber <= 0)
            throw new ArgumentException($"Pull request number must be positive. Got: {requestObject.PullRequestNumber}.");

        return new SyncPullRequestCommand
        {
            RepositoryPath = new LocalPath(requestObject.RepositoryPath),
            PullRequestNumber = requestObject.PullRequestNumber
        };
    }
}