using System;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

public static class WebApplicationBuilderUnixSocketExtensions
{
    public static WebApplicationBuilder UseUnixSocketFromEnv(this WebApplicationBuilder builder, string socketPath)
    {
        if (string.IsNullOrWhiteSpace(socketPath))
            return builder;

        if (File.Exists(socketPath))
        {
            File.Delete(socketPath);
        }

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenUnixSocket(socketPath);
        });

        return builder;
    }
}