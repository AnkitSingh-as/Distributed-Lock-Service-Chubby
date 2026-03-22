using Grpc.Core;
using Grpc.Core.Interceptors;

public sealed class ExceptionMappingInterceptor : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context);
        }
        catch (Exception ex)
        {
            throw MapException(ex);
        }
    }

    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(requestStream, context);
        }
        catch (Exception ex)
        {
            throw MapException(ex);
        }
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            await continuation(request, responseStream, context);
        }
        catch (Exception ex)
        {
            throw MapException(ex);
        }
    }

    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            await continuation(requestStream, responseStream, context);
        }
        catch (Exception ex)
        {
            throw MapException(ex);
        }
    }

    private static RpcException MapException(Exception exception)
    {
        return exception switch
        {
            RpcException rpcException => rpcException,
            ArgumentException argumentException => new RpcException(new Status(StatusCode.InvalidArgument, argumentException.Message)),
            InvalidOperationException invalidOperationException => new RpcException(new Status(StatusCode.FailedPrecondition, invalidOperationException.Message)),
            KeyNotFoundException keyNotFoundException => new RpcException(new Status(StatusCode.NotFound, keyNotFoundException.Message)),
            OperationCanceledException => new RpcException(new Status(StatusCode.Cancelled, "The operation was cancelled.")),
            _ => new RpcException(new Status(StatusCode.Internal, exception.Message))
        };
    }
}
