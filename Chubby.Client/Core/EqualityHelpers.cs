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
        return obj.Path.GetHashCode();
    }
}

