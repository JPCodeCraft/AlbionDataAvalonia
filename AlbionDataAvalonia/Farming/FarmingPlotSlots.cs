using AlbionDataAvalonia.Farming.Models;
using System;

namespace AlbionDataAvalonia.Farming;

internal static class FarmingPlotSlots
{
    public static bool SamePosition(FarmingObjectObservation left, FarmingObjectObservation right) =>
        Math.Abs(left.PositionX - right.PositionX) < 0.1 && Math.Abs(left.PositionY - right.PositionY) < 0.1;

    public static bool Contains(FarmingObjectObservation plot, FarmingObjectObservation farmable)
    {
        if (plot.Kind != "plot" || farmable.Kind != "farmable") return false;
        var rotation = plot.Rotation ?? 0;
        if (!NearInteger(rotation / 90) && !NearInteger(rotation / (Math.PI / 2))) return false;
        var kennel = plot.UniqueName.Contains("KENNEL", StringComparison.Ordinal);
        return IsOffset(farmable.PositionX - plot.PositionX, kennel)
            && IsOffset(farmable.PositionY - plot.PositionY, kennel);
    }

    private static bool IsOffset(double value, bool kennel) =>
        Math.Abs(Math.Abs(value) - 6) < 0.1 || (!kennel && Math.Abs(value) < 0.1);

    private static bool NearInteger(double value) => Math.Abs(value - Math.Round(value)) < 0.001;
}
