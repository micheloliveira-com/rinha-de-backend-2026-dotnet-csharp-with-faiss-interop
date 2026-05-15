public static class Constant
{
    public const string NORMALIZATION_JSON_FILE_PATH = "Resources/normalization.json";
    public const string RISK_JSON_FILE_PATH = "Resources/mcc_risk.json";
    public const string REFERENCES_FILE_PATH = "Resources/references.json.gz";
    public const string INDEX_FILE_PATH = "Resources/train/references.faiss";
    public const string LABELS_FILE_PATH = "Resources/train/labels.bin";
    public const string LISTEN_SOCK_ENV_VAR_NAME = "LISTEN_SOCK";
    public const string ONLY_REBUILD_ENV_VAR_NAME = "ONLY_REBUILD";
    public const string ONLY_REBUILD_ENV_VAR_VALUE = "1";
    public const string REFERENCES_FRAUD_VALUE = "fraud";
    public const float SCORE_APPROVED_THRESHOLD = 0.6f;
    public const int VECTOR_DIM = 14;
    public const int TOP_K = 5;
    public const float TOP_K_F = TOP_K;
    public const int NLIST = 4096;
    public const int NPROBE = 8;
    public const int WARMUP_RETRY_COUNT = 60 * 10;
    public const double WARMUP_RETRY_DELAY_SECONDS = 0.1;
}
