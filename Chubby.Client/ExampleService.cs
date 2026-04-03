using Chubby.Protos;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public class ExampleService
{
    private readonly IChubby _chubby;
    private readonly string _clientName;
    private readonly ILogger<ExampleService> _logger;

    private string? _sessionId;
    private int _epoch;
    private ClientHandle? _currentHandle;
    private string? _currentPath;
    private bool _exclusiveLockHeld;

    public ExampleService(IChubby chubby, IOptions<ExampleClientOptions> options, ILogger<ExampleService> logger)
    {
        _chubby = chubby;
        _clientName = options.Value.Name;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        if (_sessionId is not null)
        {
            return;
        }

        var session = await _chubby.CreateSessionAsync(new CreateSessionRequest
        {
            Client = new Client { Name = _clientName }
        });

        _sessionId = session.SessionId;
        _epoch = session.EpochNumber;
        _logger.LogInformation("Initialized example session {SessionId} for client {ClientName}.", _sessionId, _clientName);
    }

    public string DescribeSession()
    {
        return _sessionId is null
            ? "Session not initialized."
            : $"Client={_clientName}, Session={_sessionId}, Epoch={_epoch}, Path={_currentPath ?? "<none>"}";
    }

    public async Task UsePathAsync(string path)
    {
        await InitializeAsync();

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be empty.", nameof(path));
        }

        path = path.Trim();

        if (_currentHandle is not null)
        {
            await CloseCurrentHandleAsync();
        }

        var createRequest = BuildOpenRequest(path, createIfMissing: true);
        var createResponse = await _chubby.OpenAsync(createRequest);
        _currentPath = path;

        _logger.LogInformation(
            "Bound example service to path {Path}. Initial open returned handle {HandleId}, created={Created}.",
            path,
            createResponse.Handle.HandleId,
            createResponse.Created == true);

        var cachedOpen = await _chubby.OpenAsync(BuildOpenRequest(path, createIfMissing: false));
        _currentHandle = cachedOpen.Handle;
        _exclusiveLockHeld = false;
        _logger.LogInformation("Using path {Path} with handle {HandleId}.", path, _currentHandle.HandleId);
        _logger.LogInformation(createResponse.Created == true
            ? "Node was created."
            : "Node already existed.");
    }

    public async Task ReadAsync()
    {
        var handle = EnsureHandle();
        var response = await _chubby.GetContentsAndStatAsync(new GetContentsAndStatRequest
        {
            Handle = handle
        });

        _logger.LogInformation("Contents: {Contents}", response.Content.ToStringUtf8());
        _logger.LogInformation(
            "Stat instance={Instance} contentGeneration={ContentGeneration} lockGeneration={LockGeneration}",
            response.Stat.InstanceNumber,
            response.Stat.ContentGenerationNumber,
            response.Stat.LockGenerationNumber);
    }

    public async Task StatAsync()
    {
        var handle = EnsureHandle();
        var response = await _chubby.GetStatAsync(new GetStatRequest
        {
            Handle = handle
        });

        _logger.LogInformation(
            "Stat instance={Instance} contentGeneration={ContentGeneration} lockGeneration={LockGeneration} aclGeneration={AclGeneration} contentLength={ContentLength}",
            response.Stat.InstanceNumber,
            response.Stat.ContentGenerationNumber,
            response.Stat.LockGenerationNumber,
            response.Stat.AclGenerationNumber,
            response.Stat.ContentLength);
    }

    public async Task AcquireAsync()
    {
        var handle = EnsureHandle();
        await _chubby.AcquireAsync(new AcquireRequest
        {
            Handle = handle,
            LockType = LockType.Exclusive
        });

        _exclusiveLockHeld = true;
        _logger.LogInformation("Exclusive lock acquired.");
    }

    public async Task ReleaseAsync()
    {
        var handle = EnsureHandle();
        await _chubby.ReleaseAsync(new ReleaseRequest
        {
            Handle = handle,
            LockType = LockType.Exclusive
        });

        _exclusiveLockHeld = false;
        _logger.LogInformation("Exclusive lock released.");
    }

    public async Task WriteAsync(string text)
    {
        var handle = EnsureHandle();
        var current = await _chubby.GetContentsAndStatAsync(new GetContentsAndStatRequest
        {
            Handle = handle
        });

        await _chubby.SetContentsAsync(new SetContentsRequest
        {
            Handle = handle,
            Content = ByteString.CopyFromUtf8(text),
            ContentGenerationNumber = current.Stat.ContentGenerationNumber
        });

        _logger.LogInformation("Contents updated.");
    }

    public async Task SequencerAsync()
    {
        var handle = EnsureHandle();
        var sequencer = await _chubby.GetSequencerAsync(new GetSequencerRequest
        {
            Handle = handle
        });

        var validation = await _chubby.CheckSequencerAsync(new CheckSequencerRequest
        {
            Handle = handle,
            Sequencer = sequencer.Sequencer
        });

        _logger.LogInformation("Sequencer: {Sequencer}", sequencer.Sequencer);
        _logger.LogInformation("Sequencer valid: {Valid}", validation.Valid);
    }

    public async Task CloseAsync()
    {
        if (_currentHandle is null)
        {
            _logger.LogInformation("No open handle.");
            return;
        }

        await CloseCurrentHandleAsync();
        _logger.LogInformation("Handle closed.");
    }

    private OpenRequest BuildOpenRequest(string path, bool createIfMissing)
    {
        var request = new OpenRequest
        {
            SessionId = _sessionId,
            Path = path,
            Intent = Intent.WriteIntent
        };

        if (createIfMissing)
        {
            request.Create = new CreateRequestPayload
            {
                Content = ByteString.CopyFromUtf8($"Created by {_clientName}"),
                IsEphemeral = false,
                WriteAcl = { _clientName },
                ReadAcl = { _clientName },
                ChangeAcl = { _clientName }
            };
        }

        return request;
    }

    private ClientHandle EnsureHandle()
    {
        if (_sessionId is null)
        {
            throw new InvalidOperationException("Session has not been initialized.");
        }

        if (_currentHandle is null || _currentPath is null)
        {
            throw new InvalidOperationException("No path selected. Use the path command first.");
        }

        return _currentHandle;
    }
    private async Task CloseCurrentHandleAsync()
    {
        if (_currentHandle is null)
        {
            return;
        }

        if (_exclusiveLockHeld)
        {
            await _chubby.ReleaseAsync(new ReleaseRequest
            {
                Handle = _currentHandle,
                LockType = LockType.Exclusive
            });
            _exclusiveLockHeld = false;
        }

        await _chubby.CloseAsync(new CloseRequest
        {
            Handle = _currentHandle
        });

        _logger.LogInformation("Closed handle {HandleId} for path {Path}.", _currentHandle.HandleId, _currentPath);
        _currentHandle = null;
        _currentPath = null;
    }
}
