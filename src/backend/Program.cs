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
var faissUrl = builder.Configuration.GetConnectionString(Constant.FAISS_URL_CONN_STRING_NAME)!;

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

builder.Services.AddHttpClient<FaissClient>(client =>
{
    client.BaseAddress = new Uri(faissUrl!);
    client.Timeout = TimeSpan.FromSeconds(Constant.FAISS_TIMEOUT_SECONDS);
});

if (builder.Environment.IsProduction() || builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.SetMinimumLevel(LogLevel.Error);
}


const int VECTOR_DIM = 14;
const int TOP_K = 5;
const float TOP_K_F = TOP_K;
const int NLIST = 4096;
const int NPROBE = 8;

const string DATA_FILE = "Resources/references.json.gz";
const string INDEX_FILE = "Resources/train/references.faiss";
const string LABELS_FILE = "Resources/train/labels.bin";

Directory.CreateDirectory(Path.GetDirectoryName(INDEX_FILE)!);

bool ONLY_REBUILD =
    Environment.GetEnvironmentVariable("ONLY_REBUILD") == "1";

IndexIVFScalarQuantizer? index = null;
sbyte[]? labels = null;

// ======================================================
// LOAD RAW DATA
// ======================================================
static async Task<(float[][] vectors, sbyte[] labels)>
    LoadDataAsync(string path)
{
    var vectors = new List<float[]>();
    var labelList = new List<sbyte>();

    await using var fs = File.OpenRead(path);

    await using var gzip = new GZipStream(
        fs,
        CompressionMode.Decompress);

    await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable(
        gzip,
        JsonContext.Default.ReferenceItem))
    {
        if (item is null)
        {
            continue;
        }

        vectors.Add(item.Vector);

        sbyte label = 0;

        if (item.Label.ValueKind == JsonValueKind.String)
        {
            var str = item.Label.GetString();

            label = string.Equals(
                str,
                "fraud",
                StringComparison.OrdinalIgnoreCase)
                ? (sbyte)1
                : (sbyte)0;
        }
        else if (item.Label.ValueKind == JsonValueKind.Number)
        {
            label = item.Label.GetInt32() == 1
                ? (sbyte)1
                : (sbyte)0;
        }

        labelList.Add(label);
    }

    return (
        vectors.ToArray(),
        labelList.ToArray());
}

// ======================================================
// SAVE LABELS
// ======================================================

static void SaveLabels(string path, sbyte[] labels)
{
    File.WriteAllBytes(path, labels.Select(x => (byte)x).ToArray());
}


// ======================================================
// LOAD LABELS
// ======================================================

static sbyte[] LoadLabels(string path)
{
    return File.ReadAllBytes(path)
        .Select(x => (sbyte)x)
        .ToArray();
}


// ======================================================
// BUILD + SAVE
// ======================================================

async Task TrainAndSave()
{
    Console.WriteLine("[FAISS] loading raw data...");

    var (vectors, y) = await LoadDataAsync(DATA_FILE);

    Console.WriteLine("[FAISS] creating index...");
    
    var quantizer = new IndexFlatL2(VECTOR_DIM);

    var idx = new IndexIVFScalarQuantizer(
        quantizer,
        VECTOR_DIM,
        NLIST,
        QuantizerType.QT_fp16,
        MetricType.L2);

    idx.Nprobe = NPROBE;

    // ==================================================
    // FLATTEN
    // ==================================================

    var flatVectors = vectors
        .SelectMany(v => v)
        .ToArray();

    Console.WriteLine("[FAISS] training...");

    await idx.TrainAsync(
        vectors.LongLength,
        flatVectors);

    Console.WriteLine("[FAISS] adding...");

    idx.Add(
        vectors.LongLength,
        flatVectors);

    Console.WriteLine("[FAISS] saving index...");

    IndexSerializer.Write(idx, INDEX_FILE);

    Console.WriteLine("[FAISS] saving labels...");

    SaveLabels(LABELS_FILE, y);

    index = idx;
    labels = y;

    Console.WriteLine($"[FAISS] ready: {idx.TotalCount}");
}


// ======================================================
// LOAD SAVED
// ======================================================

void LoadSaved()
{
    Console.WriteLine("[FAISS] loading saved index...");

    index = IndexDeserializer.Read<IndexIVFScalarQuantizer>(INDEX_FILE);

    index.Nprobe = NPROBE;

    Console.WriteLine("[FAISS] loading labels...");

    labels = LoadLabels(LABELS_FILE);

    Console.WriteLine($"[FAISS] ready: {index.TotalCount}");
}


// ======================================================
// BOOTSTRAP
// ======================================================

async Task Bootstrap()
{
    if (ONLY_REBUILD)
    {
        Console.WriteLine(
            "[FAISS] ONLY_REBUILD enabled -> rebuilding index and exiting");

        await TrainAndSave();

        Environment.Exit(0);
    }

    if (File.Exists(INDEX_FILE) &&
        File.Exists(LABELS_FILE))
    {
        LoadSaved();
    }
    else
    {
        await TrainAndSave();
    }
}


builder.Services.AddSingleton(normalization);
builder.Services.AddSingleton(mccRiskConfig);
builder.Services.AddSingleton<VectorService>();
builder.Services.AddSingleton<FraudService>();
builder.Services.AddSingleton<WarmupService>();

var app = builder.Build();

await Bootstrap();

await app.UseWarmupWithRetryAsync(
    retryCount: Constant.WARMUP_RETRY_COUNT,
    delaySeconds: Constant.WARMUP_RETRY_DELAY_SECONDS);


// ======================================================
// SEARCH
// ======================================================
[MethodImpl(MethodImplOptions.AggressiveInlining)]
int Search(Span<float> vector)
{
    Span<float> distances = stackalloc float[TOP_K];
    Span<long> ids = stackalloc long[TOP_K];

    index.Search(
        1,
        vector,
        TOP_K,
        distances,
        ids);

    int fraudCount = 0;
    
    for (int i = 0; i < TOP_K; i++)
    {
        fraudCount += labels[ids[i]];
    }

    return fraudCount;
}



app.UseUnixSocketPermissions(socketPath);

app.MapGet("/ready", () => Results.Ok());

app.MapPost("/fraud-score", (
    FraudRequest fraudRequest,
    VectorService vectorService
) =>
{
    Span<float> vector =
        stackalloc float[VECTOR_DIM];
        
    vectorService
        .BuildVector(fraudRequest, vector);

    var fraudCount =
        Search(vector);

    var score =
        fraudCount / TOP_K_F;

    return Results.Ok(
        new FraudResponse(
            Approved: score < 0.6f,
            FraudScore: score
        ));
});

app.Run();