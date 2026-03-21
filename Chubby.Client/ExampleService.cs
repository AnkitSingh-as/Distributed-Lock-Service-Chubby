using Chubby.Protos;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Options;

public class ExampleService
{
    private readonly IChubby _chubby;
    private readonly string _clientName;

    public ExampleService(IChubby chubby, IOptions<ExampleClientOptions> options)
    {
        _chubby = chubby;
        _clientName = options.Value.Name;
    }

    public async Task DoSomeProcessing()
    {
        Console.WriteLine(" I am going to do acquire a lock using chubby");
        var res = await _chubby.CreateSessionAsync(new CreateSessionRequest()
        {
            Client = new Client { Name = _clientName }
        });

        var headers = new Metadata
        {
            { "epoch", res.EpochNumber.ToString() }
        };

        var response = await _chubby.OpenAsync(new OpenRequest()
        {
            SessionId = res.SessionId,
            Path = "/example/lock",
            Create = new CreateRequestPayload()
            {
                Content = ByteString.CopyFromUtf8("Initial Content for example/lock"),
                IsEphemeral = false,
                WriteAcl = { _clientName },
                ReadAcl = { _clientName },
                ChangeAcl = { _clientName }
            },
        }, headers);


        var contents = await _chubby.GetContentsAndStatAsync(new GetContentsAndStatRequest()
        {
            Handle = response.Handle,
        }, headers);

        Console.WriteLine($"Contents of /example/lock: {contents.Content.ToStringUtf8()}");
    }
}
