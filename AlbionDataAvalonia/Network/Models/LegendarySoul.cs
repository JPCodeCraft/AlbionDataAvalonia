using System;

public class LegendarySoul
{
    public long ObjectId { get; set; }
    public long Attunement { get; set; }
    public double Strain { get; set; }
    public string[] TraitsIds { get; set; } = Array.Empty<string>();
    public double[] TraitsValues { get; set; } = Array.Empty<double>();

    public LegendarySoul(long objectId, long attunement, double strain, string[] traitsIds, double[] traitsValues)
    {
        ObjectId = objectId;
        Attunement = attunement;
        Strain = strain;
        TraitsIds = traitsIds;
        TraitsValues = traitsValues;
    }
}