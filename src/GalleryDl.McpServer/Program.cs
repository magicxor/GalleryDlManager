using GalleryDl.McpServer.Options;
using GalleryDl.McpServer.Services;
using GalleryDl.McpServer.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Anchor the content root to the binary's directory so appsettings.json is found
// regardless of the working directory the MCP client launches us from.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// Configure all logs to go to stderr (stdout is used for the MCP protocol messages).
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddOptions<GalleryDlApiOptions>()
    .BindConfiguration(GalleryDlApiOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<PathPolicy>();

builder.Services.AddHttpClient<GalleryDlApiClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<GalleryDlApiOptions>>().Value;
    var baseUrl = options.BaseUrl.EndsWith('/') ? options.BaseUrl : options.BaseUrl + '/';
    client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
    client.Timeout = TimeSpan.FromMinutes(options.TimeoutMinutes);
});

// Add the MCP services: the transport to use (stdio) and the tools to register.
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<GalleryTools>();

await builder.Build().RunAsync();
