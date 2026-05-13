public static class Constant
{
    public const int VECTOR_SIZE = 14;

    public const string FAISS_URL_CONN_STRING_NAME = "faiss";
    public const string LISTEN_SOCK_ENV_VAR_NAME = "LISTEN_SOCK";
    public const string NORMALIZATION_JSON_FILE_PATH = "Resources/normalization.json";
    public const string RISK_JSON_FILE_PATH = "Resources/mcc_risk.json";
    public const int FAISS_TIMEOUT_SECONDS = 2;
    public const int WARMUP_RETRY_COUNT = 60 * 10;
    public const double WARMUP_RETRY_DELAY_SECONDS = 0.1;
}
