using Chubby.Core.Model;

namespace Chubby.Core.Rpc;

public class OpenResponse
{
    public bool Created { get; set; }
    public required ClientHandle Handle { get; set; }

}
