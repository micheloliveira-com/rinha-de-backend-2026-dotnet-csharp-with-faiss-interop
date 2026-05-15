using System;
using System.IO;
using Microsoft.AspNetCore.Builder;

public static class WebApplicationUnixSocketExtensions
{
    public static WebApplication UseUnixSocketPermissions(this WebApplication app, string socketPath)
    {
        if (string.IsNullOrWhiteSpace(socketPath))
            return app;

        app.Lifetime.ApplicationStarted.Register(() =>
        {
            if (!File.Exists(socketPath))
                return;
            #pragma warning disable CA1416
            File.SetUnixFileMode(
                socketPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute
            );
            #pragma warning restore CA1416
        });

        return app;
    }
}