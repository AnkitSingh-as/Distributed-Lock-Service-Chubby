using Grpc.Core;
using Grpc.Core.Interceptors;
using Chubby.Core.Rpc;

public class RequestValidationInterceptor : Interceptor
{
    private const string CreateSessionMethodSuffix = "/CreateSession";
    private readonly ChubbyRpcProxy _chubbyRpcProxy;
    private readonly ChubbyRaftOrchestrator _orchestrator;

    public RequestValidationInterceptor(ChubbyRpcProxy chubbyRpcProxy, ChubbyRaftOrchestrator orchestrator)
    {
        _chubbyRpcProxy = chubbyRpcProxy;
        _orchestrator = orchestrator;
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        await ValidateRequestAsync(context);
        return await continuation(request, context);
    }

    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        await ValidateRequestAsync(context);
        await continuation(requestStream, responseStream, context);
    }

    private async Task ValidateRequestAsync(ServerCallContext context)
    {
        ValidateLeader();

        if (!ShouldSkipEpochValidation(context))
        {
            ValidateEpoch(context.RequestHeaders);
        }

        await AwaitOrchestratorAsync();
    }

    private void ValidateLeader()
    {
        if (!_chubbyRpcProxy.IsLeader())
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, _chubbyRpcProxy.GetLeaderAddress() ?? "No Leader"));
        }
    }

    private static bool ShouldSkipEpochValidation(ServerCallContext context)
    {
        return context.Method.EndsWith(CreateSessionMethodSuffix, StringComparison.Ordinal);
    }

    private void ValidateEpoch(Metadata headers)
    {
        var epochEntry = headers.FirstOrDefault(h => h.Key == "epoch");

        if (epochEntry == null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Missing epoch"));
        }

        if (!long.TryParse(epochEntry.Value, out var epoch))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid epoch"));
        }

        if (!IsValidEpoch(epoch))
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Epoch mismatch"));
        }
    }

    private async Task AwaitOrchestratorAsync()
    {
        var taskToAwaitBeforeProceeding = _orchestrator.GetTaskToAwaitBeforeProceeding();
        if (taskToAwaitBeforeProceeding != null)
        {
            await taskToAwaitBeforeProceeding;
        }
    }

    private bool IsValidEpoch(long epoch)
    {
        return epoch == _chubbyRpcProxy.CurrentEpochNumber();
    }
}
