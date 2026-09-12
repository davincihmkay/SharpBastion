namespace SharpBastion.Views;

public class PendingKagiSearchView
{
    public readonly string Id;
    public readonly string Query;

    public PendingKagiSearchView(string id, string query)
    {
        Id = id;
        Query = query;
    }
}