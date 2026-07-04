using System;

public class LegendarySoul
{
    public long ObjectId { get; set; }
    public Guid SoulId { get; set; }
    public string? SoulName { get; set; }
    public int Era { get; set; }
    public bool AttunedToMe { get; set; }
    public string? AttunedToPlayerName { get; set; }
    public long? Attunement { get; set; }
    public double Strain { get; set; }
    public long? PvPFameGained { get; set; }
    public long? AttunementSpent { get; set; }
    public bool HasTraitSnapshot { get; set; }
    public string[] TraitsIds { get; set; } = Array.Empty<string>();
    public double[] TraitsValues { get; set; } = Array.Empty<double>();

    public LegendarySoul(
        long objectId,
        Guid soulId,
        string? soulName,
        int era,
        bool attunedToMe,
        string? attunedToPlayerName,
        long? attunement,
        double strain,
        long? pvpFameGained,
        long? attunementSpent,
        bool hasTraitSnapshot,
        string[] traitsIds,
        double[] traitsValues)
    {
        ObjectId = objectId;
        SoulId = soulId;
        SoulName = soulName;
        Era = era;
        AttunedToMe = attunedToMe;
        AttunedToPlayerName = attunedToPlayerName;
        Attunement = attunement;
        Strain = strain;
        PvPFameGained = pvpFameGained;
        AttunementSpent = attunementSpent;
        HasTraitSnapshot = hasTraitSnapshot;
        TraitsIds = traitsIds;
        TraitsValues = traitsValues;
    }
}
