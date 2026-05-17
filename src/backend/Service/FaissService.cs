using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Faiss.Cpu.Indexes.Flat;
using Faiss.Cpu.Indexes.IVF;
using Faiss.Cpu.Indexes.Sharding;
using Faiss.Cpu.Serializer;
using Faiss.Models;

public class FaissService
{
    private sbyte[]? Labels { get; set; }

    private IndexShards? ShardedIndex { get; set; }

    private readonly int _shardCount;

    public FaissService()
    {
        _shardCount = 2;
    }

    public async Task BootstrapAsync(bool onlyRebuild)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(Constant.INDEX_FILE_PATH)!);

        if (onlyRebuild)
        {
            Console.WriteLine(
                "[FAISS] ONLY_REBUILD enabled -> rebuilding index");

            await TrainAndSaveAsync();

            Environment.Exit(0);
        }

        bool allShardsExist = true;

        for (int i = 0; i < _shardCount; i++)
        {
            if (!File.Exists(GetShardIndexPath(i)))
            {
                allShardsExist = false;
                break;
            }
        }

        if (allShardsExist &&
            File.Exists(Constant.LABELS_FILE_PATH))
        {
            LoadSaved();
        }
        else
        {
            await TrainAndSaveAsync();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Search(Span<float> vector)
    {
        Span<float> distances =
            stackalloc float[Constant.TOP_K];

        Span<long> ids =
            stackalloc long[Constant.TOP_K];

        ShardedIndex!.Search(
            1,
            vector,
            Constant.TOP_K,
            distances,
            ids);

        int fraudCount = 0;

        for (int i = 0; i < Constant.TOP_K; i++)
        {
            long id = ids[i];

            if (id < 0 ||
                id >= Labels!.Length)
            {
                continue;
            }

            fraudCount += Labels[id];
        }

        return fraudCount;
    }

    private async Task<(float[][] vectors, sbyte[] labels)>
        LoadDataAsync(string path)
    {
        var vectors = new List<float[]>();

        var labelList = new List<sbyte>();

        await using var fs =
            File.OpenRead(path);

        await using var gzip =
            new GZipStream(
                fs,
                CompressionMode.Decompress);

        await foreach (var item in JsonSerializer
            .DeserializeAsyncEnumerable(
                gzip,
                JsonContext.Default.ReferenceItem))
        {
            if (item is null)
            {
                continue;
            }

            vectors.Add(item.Vector);

            sbyte label = string.Equals(
                item.Label,
                Constant.REFERENCES_FRAUD_VALUE,
                StringComparison.OrdinalIgnoreCase)
                ? (sbyte)1
                : (sbyte)0;

            labelList.Add(label);
        }

        return (
            vectors.ToArray(),
            labelList.ToArray());
    }

    private async Task TrainAndSaveAsync()
    {
        Console.WriteLine(
            "[FAISS] loading raw data...");

        var (vectors, labels) =
            await LoadDataAsync(
                Constant.REFERENCES_FILE_PATH);

        Labels = labels;

        Console.WriteLine(
            "[FAISS] flattening full dataset...");

        float[] allFlatVectors =
            vectors
                .SelectMany(x => x)
                .ToArray();

        Console.WriteLine(
            "[FAISS] creating sharded index...");

        var sharded =
            new IndexShards(
                Constant.VECTOR_DIM,
                true,
                true);

        int total = vectors.Length;

        int shardSize =
            (int)Math.Ceiling(
                total / (double)_shardCount);

        for (int shardId = 0;
             shardId < _shardCount;
             shardId++)
        {
            int start =
                shardId * shardSize;

            if (start >= total)
            {
                break;
            }

            int length = Math.Min(
                shardSize,
                total - start);

            Console.WriteLine(
                $"[FAISS] building shard {shardId}");

            float[] shardVectors =
                vectors
                    .Skip(start)
                    .Take(length)
                    .SelectMany(x => x)
                    .ToArray();

            var quantizer =
                new IndexFlatL2(
                    Constant.VECTOR_DIM);

            var index =
                new IndexIVFScalarQuantizer(
                    quantizer,
                    Constant.VECTOR_DIM,
                    Constant.NLIST,
                    QuantizerType.QT_fp16,
                    MetricType.L2);

            index.Nprobe =
                Constant.NPROBE;

            Console.WriteLine(
                $"[FAISS] training shard {shardId} using FULL dataset");

            await index.TrainAsync(
                vectors.LongLength,
                allFlatVectors);

            Console.WriteLine(
                $"[FAISS] adding vectors to shard {shardId}");

            index.Add(
                length,
                shardVectors);

            Console.WriteLine(
                $"[FAISS] saving shard {shardId}");

            IndexSerializer.Write(
                index,
                GetShardIndexPath(shardId));

            sharded.AddIndex(index);

            Console.WriteLine(
                $"[FAISS] shard {shardId} ready: {index.TotalCount}");
        }

        Console.WriteLine(
            "[FAISS] saving labels...");

        SaveLabels(
            Constant.LABELS_FILE_PATH,
            labels);

        ShardedIndex = sharded;

        Console.WriteLine(
            $"[FAISS] ready: {ShardedIndex.TotalCount}");
    }

    private void LoadSaved()
    {
        Console.WriteLine(
            "[FAISS] loading saved shards...");

        var sharded =
            new IndexShards(
                Constant.VECTOR_DIM,
                true,
                true);

        for (int shardId = 0;
             shardId < _shardCount;
             shardId++)
        {
            string path =
                GetShardIndexPath(shardId);

            if (!File.Exists(path))
            {
                continue;
            }

            Console.WriteLine(
                $"[FAISS] loading shard {shardId}");

            var index =
                IndexDeserializer
                    .Read<IndexIVFScalarQuantizer>(
                        path);

            index.Nprobe =
                Constant.NPROBE;

            sharded.AddIndex(index);

            Console.WriteLine(
                $"[FAISS] shard {shardId} loaded: {index.TotalCount}");
        }

        Console.WriteLine(
            "[FAISS] loading labels...");

        Labels = LoadLabels(
            Constant.LABELS_FILE_PATH);

        ShardedIndex = sharded;

        Console.WriteLine(
            $"[FAISS] ready: {ShardedIndex.TotalCount}");
    }

    private static void SaveLabels(
        string path,
        sbyte[] labels)
    {
        File.WriteAllBytes(
            path,
            labels.Select(x => (byte)x)
                .ToArray());
    }

    private static sbyte[] LoadLabels(
        string path)
    {
        return File
            .ReadAllBytes(path)
            .Select(x => (sbyte)x)
            .ToArray();
    }

    private static string GetShardIndexPath(
        int shardId)
    {
        return Path.Combine(
            Path.GetDirectoryName(
                Constant.INDEX_FILE_PATH)!,
            $"index_shard_{shardId}.faiss");
    }
}