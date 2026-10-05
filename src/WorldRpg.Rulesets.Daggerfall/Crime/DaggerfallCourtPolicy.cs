namespace WorldRpg.Rulesets.Daggerfall.Crime;

internal enum DaggerfallCourtPlea { Guilty, Etiquette, Streetwise }
internal enum DaggerfallCourtOutcome { Convicted, Acquitted, Banished, GuildRescue }
internal sealed record DaggerfallCourtPenalty(int Fine, int PrisonDays, bool Severe);
internal sealed record DaggerfallCourtSentence(DaggerfallCourtOutcome Outcome, int Fine, int PrisonDays)
{
    internal DaggerfallCourtSentence Validate() => Enum.IsDefined(Outcome) && Fine >= 0 && PrisonDays >= 0
        && (Outcome == DaggerfallCourtOutcome.Convicted || Fine == 0 && PrisonDays == 0)
        ? this : throw new ArgumentException("Court sentence has invalid outcome, fine or prison time.");
}

/// <summary>Classic CourtWindow penalty tables and plea arithmetic, independent of money/time mutation.</summary>
internal static class DaggerfallCourtPolicy
{
    private static readonly int[] PerReputation = [5, 5, 6, 6, 10, 5, 5, 3, 8, 8, 9, 6, 0, 8, 0];
    private static readonly int[] Base = [300, 200, 600, 1000, 10000, 200, 500, 100, 500, 500, 1200, 200, 200, 1000, 100];
    private static readonly int[] Minimum = [50, 10, 80, 10, 9000, 10, 10, 2, 10, 10, 160, 5, 5, 10, 4];
    private static readonly int[] Maximum = [1000, 800, 1200, 1500, 12000, 12000, 1500, 700, 1500, 1500, 2000, 1000, 1000, 1500, 700];

    internal static DaggerfallCourtPenalty Accuse(DaggerfallCrimeKind crime, int legalReputation, ulong carriedGold,
        Func<int, int, int> roll)
    {
        if (!Enum.IsDefined(crime)) throw new ArgumentOutOfRangeException(nameof(crime));
        int index = (int)crime - 1;
        int first = legalReputation < 0 ? (int)Math.Min(-(long)legalReputation, 75) : 0;
        int second = legalReputation < 0 ? (int)Math.Min(-(long)legalReputation / 2, 75) : 0;
        bool severe = roll(1, 100) <= second || roll(1, 100) <= first;
        long amount = Math.Clamp(Base[index] + PerReputation[index] * Math.Abs((long)legalReputation), Minimum[index], Maximum[index]);
        int fine = 0, days = 0;
        for (int unit = 0; unit < amount / 40; unit++)
            if (roll(0, 1) != 0) fine += 40; else days += 3;
        if (carriedGold < (ulong)fine) { days += (fine - (int)carriedGold) / 40; fine = (int)carriedGold; }
        return new(fine, days, severe);
    }

    internal static DaggerfallCourtSentence Decide(DaggerfallCourtPenalty penalty, DaggerfallCourtPlea plea,
        int reputation, int skill, int personality, Func<int, int, int> roll)
    {
        if (!Enum.IsDefined(plea)) throw new ArgumentOutOfRangeException(nameof(plea));
        if (plea == DaggerfallCourtPlea.Guilty)
            return penalty.Severe ? new(DaggerfallCourtOutcome.Banished, 0, 0)
                : new(DaggerfallCourtOutcome.Convicted, penalty.Fine / 2, penalty.PrisonDays / 2);
        int chance = (int)Math.Clamp(reputation + ((long)skill + personality) / 2, 5, 95);
        if (roll(1, 100) <= chance) return new(DaggerfallCourtOutcome.Acquitted, 0, 0);
        if (penalty.Severe) return new(DaggerfallCourtOutcome.Banished, 0, 0);
        int adjustment = reputation + roll(1, 100);
        return new(DaggerfallCourtOutcome.Convicted,
            adjustment < 25 ? penalty.Fine * 2 : adjustment > 75 ? penalty.Fine / 2 : penalty.Fine, penalty.PrisonDays);
    }

    internal static DaggerfallCourtSentence FitAvailableGold(DaggerfallCourtSentence sentence, ulong carriedGold) =>
        carriedGold >= (ulong)sentence.Fine ? sentence
            : sentence with { Fine = (int)carriedGold, PrisonDays = sentence.PrisonDays + (sentence.Fine - (int)carriedGold) / 40 };
}
