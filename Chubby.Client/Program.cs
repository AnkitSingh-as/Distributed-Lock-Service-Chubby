using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);
// builder.Logging.ClearProviders();
// builder.Logging.AddSimpleConsole(options =>
// {
//     options.TimestampFormat = "HH:mm:ss ";
//     options.SingleLine = true;
// });
builder.Services.AddChubbyClient(builder.Configuration);
builder.Services
    .AddOptions<ExampleClientOptions>()
    .Bind(builder.Configuration.GetSection(ExampleClientOptions.SectionName));
builder.Services.AddSingleton<ExampleService>();
using var host = builder.Build();

var exampleService = host.Services.GetRequiredService<ExampleService>();
var clientOptions = host.Services.GetRequiredService<IOptions<ExampleClientOptions>>();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ChubbyClientCli");

await exampleService.InitializeAsync();

Console.WriteLine($"Client name: {clientOptions.Value.Name}");
Console.WriteLine(exampleService.DescribeSession());
// Console.
Console.WriteLine("Enter path to work with: ");
var initialPath = Console.ReadLine();

if (string.IsNullOrWhiteSpace(initialPath))
{
    initialPath = "/example/demo.txt";
}

await exampleService.UsePathAsync(initialPath);
PrintHelp(logger);

while (true)
{
    Console.Write("> ");
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input))
    {
        continue;
    }

    var command = input.Trim();
    if (string.Equals(command, "q", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "quit", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "exit", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    try
    {
        if (string.Equals(command, "help", StringComparison.OrdinalIgnoreCase))
        {
            PrintHelp(logger);
        }
        else if (string.Equals(command, "info", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(exampleService.DescribeSession());
        }
        else if (string.Equals(command, "read", StringComparison.OrdinalIgnoreCase))
        {
            await exampleService.ReadAsync();
        }
        else if (string.Equals(command, "stat", StringComparison.OrdinalIgnoreCase))
        {
            await exampleService.StatAsync();
        }
        else if (string.Equals(command, "lock", StringComparison.OrdinalIgnoreCase))
        {
            await exampleService.AcquireAsync();
        }
        else if (string.Equals(command, "unlock", StringComparison.OrdinalIgnoreCase))
        {
            await exampleService.ReleaseAsync();
        }
        else if (string.Equals(command, "seq", StringComparison.OrdinalIgnoreCase))
        {
            await exampleService.SequencerAsync();
        }
        else if (string.Equals(command, "close", StringComparison.OrdinalIgnoreCase))
        {
            await exampleService.CloseAsync();
        }
        else if (command.StartsWith("path ", StringComparison.OrdinalIgnoreCase))
        {
            await exampleService.UsePathAsync(command["path ".Length..].Trim());
        }
        else if (command.StartsWith("write ", StringComparison.OrdinalIgnoreCase))
        {
            await exampleService.WriteAsync(command["write ".Length..]);
        }
        else
        {
            logger.LogWarning("Unknown command. Type `help`.");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Request failed: {Message}", ex.Message);
    }
}

await exampleService.CloseAsync();

static void PrintHelp(ILogger logger)
{
    Console.WriteLine("Commands:");
    Console.WriteLine("  help          Show commands");
    Console.WriteLine("  info          Show current session/path");
    Console.WriteLine("  path <path>   Switch to another path");
    Console.WriteLine("  read          Read contents");
    Console.WriteLine("  stat          Read stat");
    Console.WriteLine("  write <text>  Write contents");
    Console.WriteLine("  lock          Acquire exclusive lock");
    Console.WriteLine("  unlock        Release exclusive lock");
    Console.WriteLine("  seq           Get and validate sequencer");
    Console.WriteLine("  close         Close current handle");
    Console.WriteLine("  quit          Exit");
}
