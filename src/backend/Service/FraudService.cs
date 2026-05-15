public class FraudService(
    FaissService faissService,
    VectorService vectorService
)
{
    public FraudResponse Process(FraudRequest fraudRequest)
    {
        Span<float> vector =
            stackalloc float[Constant.VECTOR_DIM];
            
        vectorService
            .BuildVector(fraudRequest, vector);

        var fraudCount =
            faissService.Search(vector);

        var score =
            fraudCount / Constant.TOP_K_F;

        return new FraudResponse(
                Approved: score < 0.6f,
                FraudScore: score
            );
    }
}