using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Polly;

public static class WebApplicationWarmupExtensions
{
    public static async Task UseWarmupWithRetryAsync(
        this WebApplication app,
        int retryCount,
        double delaySeconds)
    {
        var policy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                retryCount: retryCount,
                sleepDurationProvider: _ => TimeSpan.FromSeconds(delaySeconds),
                onRetry: (exception, timeSpan, retry, context) =>
                {
                    Console.WriteLine(
                        $"Warmup retry {retry}: {exception.GetType().Name} - {exception.Message}");
                });

        await policy.ExecuteAsync(async () =>
        {
            using var scope = app.Services.CreateScope();
            var warmupService = scope.ServiceProvider.GetRequiredService<WarmupService>();
            await warmupService.Warmup();
        });
    }
}