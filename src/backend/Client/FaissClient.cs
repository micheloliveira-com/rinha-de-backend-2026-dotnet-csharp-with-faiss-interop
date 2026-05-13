
public sealed class FaissClient
{
    private readonly HttpClient _http;

    public FaissClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<FaissResponse> QueryAsync(float[] vector)
    {
        var payload = new FaissRequest
        (
            vector
        );

        using var resp = await _http.PostAsJsonAsync(
            "/search",
            payload,
            JsonContext.Default.FaissRequest
        );

        resp.EnsureSuccessStatusCode();

        return (await resp.Content.ReadFromJsonAsync(
            JsonContext.Default.FaissResponse
        ))!;
    }
}