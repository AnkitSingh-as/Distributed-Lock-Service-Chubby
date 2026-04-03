using System.Diagnostics.CodeAnalysis;
using Chubby.Protos;

public class OpenRequestComparer : IEqualityComparer<OpenRequest>
{
    public bool Equals(OpenRequest? x, OpenRequest? y)
    {
        if (x == null || y == null)
        {
            return false;
        }
        if (x.Path != y.Path)
        {
            return false;
        }
        if (x.Intent != y.Intent)
        {
            return false;
        }

        return true;
    }

    public int GetHashCode([DisallowNull] OpenRequest obj)
    {
        var hash = new HashCode();
        hash.Add(obj.Path, StringComparer.Ordinal);
        hash.Add(obj.Intent);
        return hash.ToHashCode();
    }
}

