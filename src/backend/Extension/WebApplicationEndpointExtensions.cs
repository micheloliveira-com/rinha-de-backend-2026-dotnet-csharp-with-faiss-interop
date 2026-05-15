using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Polly;

public static class WebApplicationEndpointExtensions
{
    public static void MapEndpoints(
        this WebApplication app)
    {
        app.MapGet("/ready", () => Results.Ok());

        app.MapPost("/fraud-score", (
            FraudRequest fraudRequest,
            FraudService fraudService
        ) =>
        {
            return fraudService.Process(fraudRequest);
        });
    }
}