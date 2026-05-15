using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Polly;

public static class WebApplicationFaissExtensions
{
    public static async Task FaissBootstrap(
        this WebApplication app,
        bool onlyRebuild)
    {
        using var scope = app.Services.CreateScope();
        var warmupService = scope.ServiceProvider.GetRequiredService<FaissService>();
        await warmupService.BootstrapAsync(onlyRebuild);
    }
}