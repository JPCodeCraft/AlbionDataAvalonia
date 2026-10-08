using Albion.Network;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Shared;
using Serilog;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class DebugResponseProbeResponseHandler : PacketHandler<ResponsePacket>
{
    private readonly Func<bool> isEnabled;

    private static readonly int[] ProbeOperationCodeValues =
    [
        (int)OperationCodes.Join,
        (int)OperationCodes.FishingFinish,
        (int)OperationCodes.ChangeCluster,
        // Match Activity requests so successful and rejected transfers can be traced.
        (int)OperationCodes.ExitEnterStart,
        (int)OperationCodes.ExitEnterCancel,
        (int)OperationCodes.Respawn,
        (int)OperationCodes.TeleportBack,
        (int)OperationCodes.UseLootChest,
        (int)OperationCodes.TreasureChestUsingStart,
        (int)OperationCodes.TreasureChestUsingCancel,
        (int)OperationCodes.MatchLootChestOpeningStart,
        (int)OperationCodes.MatchLootChestOpeningCancel,
        (int)OperationCodes.ContainerOpen,
        (int)OperationCodes.ContainerClose,
        (int)OperationCodes.InventoryMoveItem,
        (int)OperationCodes.InventoryMoveGivenItems,
        (int)OperationCodes.InventorySplitStack,
        (int)OperationCodes.InventorySplitStackInto,
        (int)OperationCodes.InventoryStack,
        (int)OperationCodes.InventoryReorder,
        (int)OperationCodes.InventoryAddToStacks,
        (int)OperationCodes.InventoryDestroyItem,
        (int)OperationCodes.InventoryRecoverItem,
        (int)OperationCodes.InventoryRecoverAllItems,
        (int)OperationCodes.InventoryDropAll,
        (int)OperationCodes.TakeSilver,
        (int)OperationCodes.PartyLeave,
        (int)OperationCodes.PartyChangeLootSetting,
        (int)OperationCodes.GetIslandInfos,
        (int)OperationCodes.RegisterToObject,
        (int)OperationCodes.UnRegisterFromObject,
        // Match the request probe so successful and rejected actions can be correlated.
        (int)OperationCodes.AttackBuildingStart,
        (int)OperationCodes.ActionOnBuildingStart,
        (int)OperationCodes.ActionOnBuildingCancel,
        (int)OperationCodes.BuildingChangeRenovationState,
        (int)OperationCodes.ConstructionSiteCreate,
        (int)OperationCodes.TearDownConstructionSite,
        (int)OperationCodes.PlaceableObjectPlace,
        (int)OperationCodes.PlaceableObjectPlaceCancel,
        (int)OperationCodes.PlaceableObjectPickup,
        (int)OperationCodes.FarmableHarvest,
        (int)OperationCodes.FarmableFinishGrownItem,
        (int)OperationCodes.FarmableDestroy,
        (int)OperationCodes.FarmableGetProduct,
        (int)OperationCodes.FarmableFill,
        (int)OperationCodes.BoostFarmable,
    ];

    public DebugResponseProbeResponseHandler(Func<bool> isEnabled)
    {
        this.isEnabled = isEnabled ?? throw new ArgumentNullException(nameof(isEnabled));
    }

    protected override Task OnHandleAsync(ResponsePacket packet)
    {
        if (!isEnabled() || !Log.IsEnabled(Serilog.Events.LogEventLevel.Debug) || !ProbeOperationCodeValues.Contains(packet.OperationCode))
        {
            return Task.CompletedTask;
        }

        var response = new DebugResponseProbeResponse(packet.Parameters);
        Log.Debug(
            "Debug probe captured response {OperationCode} ({OperationName}). ReturnCode={ReturnCode}, MessageSizeBytes={MessageSizeBytes}, IsFragmented={IsFragmented}, FragmentCount={FragmentCount}, CapturedAt={CapturedAt:O}, ConnectionId={ConnectionId}, OriginKey={OriginKey}, ParameterCount={ParameterCount}: {Parameters}",
            packet.OperationCode,
            System.Enum.GetName(typeof(OperationCodes), packet.OperationCode) ?? "Unknown",
            packet.ReturnCode,
            packet.MessageSizeBytes,
            packet.IsFragmented,
            packet.FragmentCount,
            packet.CapturedAt,
            packet.ConnectionId,
            packet.MessageIdentity?.OriginKey,
            response.Parameters.Count,
            DebugProbeFormatter.FormatParameters(response.Parameters));

        return Task.CompletedTask;
    }

}
