using System.Numerics;
using System.Runtime.CompilerServices;

public class VectorService(
    NormalizationConfig normalizationConfig,
    MccRiskConfig mccRiskConfig
)
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void BuildVector(FraudRequest req, Span<float> v)
    {
        static float Clamp(double v)
            => Math.Max(0.0f, Math.Min(1.0f, (float)v));
            
        var tx = req.Transaction;
        var customer = req.Customer;
        var merchant = req.Merchant;
        var terminal = req.Terminal;
        var last = req.LastTransaction;

        v[VectorIndex.AmountNormalized] = Clamp(tx.Amount / normalizationConfig.MaxAmount);
        v[VectorIndex.InstallmentsNormalized] = Clamp(tx.Installments / normalizationConfig.MaxInstallments);

        var ratio = customer.AvgAmount == 0 ? 0 : tx.Amount / customer.AvgAmount;
        v[VectorIndex.AmountVsAvgRatio] = Clamp(ratio / normalizationConfig.AmountVsAvgRatio);

        var dt = ParseIsoUtc(tx.RequestedAt);
        v[VectorIndex.HourOfDay] = dt.Hour / 23.0f;

        int dow = ((int)dt.DayOfWeek + 6) % 7;
        v[VectorIndex.DayOfWeek] = dow / 6.0f;

        if (last == null)
            v[VectorIndex.MinutesSinceLastTx] = -1;
        else
        {
            var lastTime = ParseIsoUtc(last.Timestamp);
            v[VectorIndex.MinutesSinceLastTx] =
                Clamp((dt - lastTime).TotalMinutes / normalizationConfig.MaxMinutes);
        }

        v[VectorIndex.LastDistanceKm] =
            last == null ? -1 : Clamp(last.KmFromCurrent / normalizationConfig.MaxKm);

        v[VectorIndex.TerminalDistanceKm] =
            Clamp(terminal.KmFromHome / normalizationConfig.MaxKm);

        v[VectorIndex.TxCount24h] =
            Clamp(customer.TxCount24h / normalizationConfig.MaxTxCount24h);

        v[VectorIndex.TerminalIsOnline] = terminal.IsOnline ? 1 : 0;
        v[VectorIndex.CardPresent] = terminal.CardPresent ? 1 : 0;

        v[VectorIndex.UnknownMerchant] =
            customer.KnownMerchants.Contains(merchant.Id) ? 0 : 1;

        v[VectorIndex.MerchantMccRisk] =
            mccRiskConfig.Values.TryGetValue(merchant.Mcc, out var risk)
                ? risk
                : 0.5f;

        v[VectorIndex.MerchantAvgAmount] =
            Clamp(merchant.AvgAmount / normalizationConfig.MaxMerchantAvgAmount);
    }

    private readonly struct VectorIndex
    {
        public const int AmountNormalized = 0;
        public const int InstallmentsNormalized = 1;
        public const int AmountVsAvgRatio = 2;
        public const int HourOfDay = 3;
        public const int DayOfWeek = 4;
        public const int MinutesSinceLastTx = 5;
        public const int LastDistanceKm = 6;
        public const int TerminalDistanceKm = 7;
        public const int TxCount24h = 8;
        public const int TerminalIsOnline = 9;
        public const int CardPresent = 10;
        public const int UnknownMerchant = 11;
        public const int MerchantMccRisk = 12;
        public const int MerchantAvgAmount = 13;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private DateTime ParseIsoUtc(string s)
    {
        int y = (s[0] - '0') * 1000 + (s[1] - '0') * 100 + (s[2] - '0') * 10 + (s[3] - '0');
        int M = (s[5] - '0') * 10 + (s[6] - '0');
        int d = (s[8] - '0') * 10 + (s[9] - '0');
        int h = (s[11] - '0') * 10 + (s[12] - '0');
        int m = (s[14] - '0') * 10 + (s[15] - '0');
        int sec = (s[17] - '0') * 10 + (s[18] - '0');

        return new DateTime(y, M, d, h, m, sec, DateTimeKind.Utc);
    }
}