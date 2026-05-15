using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Faiss.Cpu.Indexes.Flat;
using Faiss.Cpu.Indexes.IVF;
using Faiss.Cpu.Serializer;
using Faiss.Models;

public class FaissService()
{
    private sbyte[]? Labels { get; set; } = default;
    private IndexIVFScalarQuantizer? Index { get; set; } = default;

    public async Task BootstrapAsync(bool onlyRebuild)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Constant.INDEX_FILE_PATH)!);

        if (onlyRebuild)
        {
            Console.WriteLine(
                "[FAISS] ONLY_REBUILD enabled -> rebuilding index and exiting");

            await TrainAndSaveAsync();

            Environment.Exit(0);
        }

        if (File.Exists(Constant.INDEX_FILE_PATH) &&
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
        const int vectorSearchCount = 1;
        Span<float> distances = stackalloc float[Constant.TOP_K];
        Span<long> ids = stackalloc long[Constant.TOP_K];

        Index!.Search(
            vectorSearchCount,
            vector,
            Constant.TOP_K,
            distances,
            ids);

        int fraudCount = 0;

        for (int i = 0; i < Constant.TOP_K; i++)
        {
            fraudCount += Labels![ids[i]];
        }

        return fraudCount;
    }

    private async Task<(float[][] vectors, sbyte[] labels)>
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
                    Constant.REFERENCES_FRAUD_VALUE,
                    StringComparison.OrdinalIgnoreCase)
                    ? (sbyte)1
                    : (sbyte)0;
            }

            labelList.Add(label);
        }

        return (
            vectors.ToArray(),
            labelList.ToArray());
    }

    private void SaveLabels(string path, sbyte[] labels)
    {
        File.WriteAllBytes(path, labels.Select(x => (byte)x).ToArray());
    }

    private sbyte[] LoadLabels(string path)
    {
        return File.ReadAllBytes(path)
            .Select(x => (sbyte)x)
            .ToArray();
    }

    private async Task TrainAndSaveAsync()
    {
        Console.WriteLine("[FAISS] loading raw data...");

        var (vectors, y) = await LoadDataAsync(Constant.REFERENCES_FILE_PATH);

        Console.WriteLine("[FAISS] creating index...");

        var quantizer = new IndexFlatL2(Constant.VECTOR_DIM);

        var idx = new IndexIVFScalarQuantizer(
            quantizer,
            Constant.VECTOR_DIM,
            Constant.NLIST,
            QuantizerType.QT_fp16,
            MetricType.L2);

        idx.Nprobe = Constant.NPROBE;

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

        IndexSerializer.Write(idx, Constant.INDEX_FILE_PATH);

        Console.WriteLine("[FAISS] saving labels...");

        SaveLabels(Constant.LABELS_FILE_PATH, y);

        Index = idx;
        Labels = y;

        Console.WriteLine($"[FAISS] ready: {idx.TotalCount}");
    }

    private void LoadSaved()
    {
        Console.WriteLine("[FAISS] loading saved index...");

        Index = IndexDeserializer.Read<IndexIVFScalarQuantizer>(Constant.INDEX_FILE_PATH);

        Index.Nprobe = Constant.NPROBE;

        Console.WriteLine("[FAISS] loading labels...");

        Labels = LoadLabels(Constant.LABELS_FILE_PATH);

        Console.WriteLine($"[FAISS] ready: {Index.TotalCount}");
    }

}