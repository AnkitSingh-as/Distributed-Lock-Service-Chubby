using Chubby.Protos;
using Google.Protobuf;
using Grpc.Core;

public class ExampleService
{
    private const string ClientName = "TestClient";
    private IChubby _chubby;

    public ExampleService(IChubby chubby)
    {
        _chubby = chubby;
    }

    public async Task DoSomeProcessing()
    {
        Console.WriteLine(" I am going to do acquire a lock using chubby");
        var res = await _chubby.CreateSessionAsync(new CreateSessionRequest()
        {
            Client = new Client { Name = ClientName }
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
                WriteAcl = { ClientName },
                ReadAcl = { ClientName },
                ChangeAcl = { ClientName }
            },
        }, headers);


        var contents = await _chubby.GetContentsAndStatAsync(new GetContentsAndStatRequest()
        {
            Handle = response.Handle,
        }, headers);

        Console.WriteLine($"Contents of /example/lock: {contents.Content.ToStringUtf8()}");
    }
}
