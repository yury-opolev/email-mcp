using EmailMcp.Gmail;
using EmailMcp.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services
    .AddEmailSecurity()
    .AddGmailProvider(options =>
    {
        var credPath = builder.Configuration["Gmail:CredentialsPath"];
        if (!string.IsNullOrEmpty(credPath))
            options.CredentialsPath = credPath;
    })
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
