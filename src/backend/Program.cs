using System.IO.Compression;
using System.Net.Sockets;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Faiss.Cpu.Indexes.Approximate;
using Faiss.Cpu.Indexes.Flat;
using Faiss.Cpu.Indexes.IVF;
using Faiss.Cpu.Serializer;
using Faiss.Models;
using Polly;

var builder = WebApplication.CreateBuilder(args);

var socketPath = Environment.GetEnvironmentVariable(Constant.LISTEN_SOCK_ENV_VAR_NAME)!;
bool onlyRebuild =
    Environment.GetEnvironmentVariable(Constant.ONLY_REBUILD_ENV_VAR_NAME) == Constant.ONLY_REBUILD_ENV_VAR_VALUE;

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, JsonContext.Default);
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
});

builder.UseUnixSocketFromEnv(socketPath);

var normalization = JsonSerializer.Deserialize(
    File.ReadAllText(Constant.NORMALIZATION_JSON_FILE_PATH), JsonContext.Default.NormalizationConfig)!;

var mccRiskConfig = new MccRiskConfig
(
    JsonSerializer.Deserialize(
        File.ReadAllText(Constant.RISK_JSON_FILE_PATH), JsonContext.Default.DictionaryStringSingle)!
);

if (builder.Environment.IsProduction() || builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.SetMinimumLevel(LogLevel.Error);
}

builder.Services.AddSingleton(normalization);
builder.Services.AddSingleton(mccRiskConfig);
builder.Services.AddSingleton<VectorService>();
builder.Services.AddSingleton<FraudService>();
builder.Services.AddSingleton<WarmupService>();
builder.Services.AddSingleton<FaissService>();

var app = builder.Build();

await app.FaissBootstrap(onlyRebuild);

await app.UseWarmupWithRetryAsync(
    retryCount: Constant.WARMUP_RETRY_COUNT,
    delaySeconds: Constant.WARMUP_RETRY_DELAY_SECONDS);

app.UseUnixSocketPermissions(socketPath);

app.MapEndpoints();

app.Run();