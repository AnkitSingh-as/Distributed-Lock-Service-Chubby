using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddChubbyClient(builder.Configuration);
builder.Services
    .AddOptions<ExampleClientOptions>()
    .Bind(builder.Configuration.GetSection(ExampleClientOptions.SectionName));
builder.Services.AddSingleton<ExampleService>();
using var host = builder.Build();

var exampleService = host.Services.GetRequiredService<ExampleService>();
var clientOptions = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ExampleClientOptions>>();

Console.WriteLine($"Client name: {clientOptions.Value.Name}");
Console.WriteLine("Press Enter to call ExampleService.DoSomeProcessing(). Type 'q' and press Enter to quit.");

while (true)
{
    var input = Console.ReadLine();
    if (string.Equals(input, "q", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    if (!string.IsNullOrEmpty(input))
    {
        Console.WriteLine("Press Enter to run the command, or type 'q' to quit.");
        continue;
    }

    try
    {
        await exampleService.DoSomeProcessing();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Request failed: {ex.Message}");
    }

    Console.WriteLine("Press Enter to run again, or type 'q' to quit.");
}
