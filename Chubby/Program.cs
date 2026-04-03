using Chubby.Core.StateMachine;
using Chubby.Core.Utils;
using System.Diagnostics;
using Chubby;
using Chubby.Clocks;
using Chubby.DataSource;
using Chubby.Services;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Raft;
using Raft.Protos;
using Serilog;
using Serilog.Events;
using Chubby.Core.Rpc;
using Microsoft.Extensions.Logging;

internal class Program
{
    private static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var raftConfig = builder.Configuration.GetSection("RaftConfiguration").Get<RaftConfig>();
        var chubbyConfig = builder.Configuration.GetSection("ChubbyConfiguration").Get<ChubbyConfig>();
        if (raftConfig is null)
        {
            throw new InvalidOperationException("Could not find ' Raft Configuration' section in appSettings.json.");
        }
        if (chubbyConfig is null)
        {
            throw new InvalidOperationException("Could not find ' Chubby Configuration' section in appSettings.json.");
        }


        var nodeId = builder.Configuration.GetValue<int>("nodeId");

        if (!raftConfig.Peers.TryGetValue(nodeId, out var selfUrl))
        {
            throw new InvalidOperationException($"Could not find peer URL for nodeId {nodeId} in configuration.");
        }

        builder.Services
            .AddGrpc()
            .AddServiceOptions<ChubbyService>(options =>
            {
                options.Interceptors.Add<RequestValidationInterceptor>();
                options.Interceptors.Add<ExceptionMappingInterceptor>();
            });


        var logLevelStr = builder.Configuration["Logging:LogLevel:Default"] ?? "Information";
        if (!Enum.TryParse<LogEventLevel>(logLevelStr, true, out var minLogEventLevel))
        {
            minLogEventLevel = LogEventLevel.Information;
        }

        Serilog.Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(minLogEventLevel)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(
                path: $"logs/node-{nodeId}.log",
                rollingInterval: RollingInterval.Day,
                outputTemplate: " [{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        var loggerFactory = new Microsoft.Extensions.Logging.LoggerFactory().AddSerilog(Serilog.Log.Logger);

        builder.Host.UseSerilog();
        // TODO: Better handle of creation and addition of chubbyRpcProxy, state machine.
        var chubbyStateMachine = new ChubbyCore( loggerFactory);
        // TODO: expose extension methods to easily plug raft, without client bothering to implement it.
        // The NodeEnvelope will drive the state machine by calling its Apply() method for committed log entries.
        var nodeEnvelope = await NodeEnvelope.CreateAsync(new InMemoryDataSource(), raftConfig, nodeId, chubbyStateMachine, new SystemRaftClock(), loggerFactory);
        builder.Services.AddSingleton(nodeEnvelope);
        builder.Services.AddSingleton<INodeEnvelope>(nodeEnvelope);
        builder.Services.AddSingleton<IServer>(nodeEnvelope);
        var chubbyRpcProxy = new ChubbyRpcProxy(chubbyStateMachine, nodeEnvelope, chubbyConfig, new Logger<ChubbyRpcProxy>(loggerFactory));
        var chubbyRaftOrchestrator = new ChubbyRaftOrchestrator(nodeEnvelope, chubbyRpcProxy);
        builder.Services.AddSingleton<ICheckDigitStrategy, HmacCheckDigitStrategy>();
        builder.Services.AddSingleton(chubbyRpcProxy);
        builder.Services.AddSingleton(chubbyRaftOrchestrator);


        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenLocalhost(
                new Uri(selfUrl).Port,
                listenOptions =>
                {
                    listenOptions.Protocols = HttpProtocols.Http2;
                });
        });

        var app = builder.Build();

        var peerClients = new Dictionary<int, IServer>();
        var peerChannels = new List<GrpcChannel>(); // Keep channels alive to prevent disposal.

        foreach (var peerInfo in raftConfig.Peers)
        {
            if (peerInfo.Key == nodeId) continue;

            var channel = GrpcChannel.ForAddress(peerInfo.Value);
            peerChannels.Add(channel);
            var grpcClient = new RaftServer.RaftServerClient(channel);
            peerClients[peerInfo.Key] = new NodeClientImpl(grpcClient);
        }

        nodeEnvelope.Peers = (peerClients);

        app.Lifetime.ApplicationStopping.Register(() =>
        {
            peerChannels.ForEach(c => c.Dispose());
        });

        app.MapGrpcService<RaftService>();
        app.MapGrpcService<ChubbyService>();
        app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client.");
        await app.RunAsync();


    }
}
