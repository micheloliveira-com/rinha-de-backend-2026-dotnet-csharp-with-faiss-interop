public class FraudService(
    FaissClient faissClient,
    VectorService vectorService
)
{
    public async Task<FraudResponse> ProcessAsync(FraudRequest fraudRequest)
    {
        /*var vector = vectorService.BuildVector(fraudRequest);

        var result = await faissClient.QueryAsync(vector);

        var score = result.FraudCount / 5.0f;*/

        return new FraudResponse(
            Approved: false,
            FraudScore: 0
        );
    }
}