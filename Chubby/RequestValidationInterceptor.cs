using Grpc.Core;
using Grpc.Core.Interceptors;
using Chubby.Core.Rpc;

public class RequestValidationInterceptor : Interceptor
{
    private readonly ChubbyRpcProxy chubbyRpcProxy;
    private readonly ChubbyRaftOrchestrator orchestrator;

    public RequestValidationInterceptor(ChubbyRpcProxy chubbyRpcProxy, ChubbyRaftOrchestrator orchestrator)
    {
        this.chubbyRpcProxy = chubbyRpcProxy;
        this.orchestrator = orchestrator;
    }
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        if (!chubbyRpcProxy.IsLeader())
        {
            throw new RpcException(new Grpc.Core.Status(StatusCode.FailedPrecondition, "Not Leader"));
        }
        var headers = context.RequestHeaders;

        var epochEntry = headers.FirstOrDefault(h => h.Key == "epoch");

        if (epochEntry == null)
            throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Missing epoch"));

        if (!long.TryParse(epochEntry.Value, out var epoch))
            throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Invalid epoch"));

        if (!IsValidEpoch(epoch))
            throw new RpcException(new Grpc.Core.Status(StatusCode.FailedPrecondition, "Epoch mismatch"));

        var taskToAwaitBeforeProceeding = orchestrator.GetTaskToAwaitBeforeProceeding();
        if (taskToAwaitBeforeProceeding != null)
        {
            await taskToAwaitBeforeProceeding;
        }
        return await continuation(request, context);
    }

    private bool IsValidEpoch(long epoch)
    {
        return epoch == chubbyRpcProxy.CurrentEpochNumber();
    }

}
