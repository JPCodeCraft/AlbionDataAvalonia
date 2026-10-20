using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Locations.Models;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.State;
using AlbionDataAvalonia.Ui;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace AlbionDataAvalonia.ViewModels;

public enum SetupHeroKind
{
    Waiting,
    Idle,
    Warning,
    Blocked,
    Zone
}

public enum SetupSummarySeverity
{
    Ok,
    Waiting,
    Warning,
    Blocked,
    Idle
}

/// <summary>
/// Walks through what the client needs before it can upload: the capture driver, adapters, game traffic, server,
/// character and location. The Main tab shows the steps and the sidebar a summary. Everything is recomputed once a
/// second from live state.
/// </summary>
public sealed partial class SetupStatusViewModel : ViewModelBase
{
    private enum SetupStage
    {
        CheckingDriver,
        DriverBlocked,
        StartingCapture,
        AdaptersBlocked,
        NoAdapters,
        Waiting,
        Stuck,
        Closed,
        LoggingIn,
        UnknownServer,
        ChangeZones,
        Ready
    }

    private readonly NetworkListenerService networkListener = null!;
    private readonly PlayerState playerState = null!;

    public SetupStatusViewModel()
    {
    }

    /// <param name="installDriver">Opens the Npcap download.</param>
    /// <param name="installMacPermissions">Installs the macOS capture permission and restarts the client.</param>
    public SetupStatusViewModel(NetworkListenerService networkListener, PlayerState playerState, ICommand installDriver, ICommand installMacPermissions)
    {
        this.networkListener = networkListener;
        this.playerState = playerState;
        windowsDriverActions = [new("Download Npcap", installDriver, true), new("Check again", CheckCaptureAgainCommand)];
        linuxDriverActions = [new("Check again", CheckCaptureAgainCommand, true)];
        macPermissionActions = [new("Install permissions", installMacPermissions, true)];

        _ = CheckCaptureDriverAsync();
        setupTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Update());
        setupTimer.Start();
        Update();
    }

    /// <summary>Market orders arrive encrypted, so the summary warns even when every step is done.</summary>
    [ObservableProperty] private bool ordersEncrypted;

    partial void OnOrdersEncryptedChanged(bool value) => Update();

    /// <summary>Every step is done and the client can upload.</summary>
    [ObservableProperty] private bool isReady;

    private static readonly TimeSpan HelpDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan LoginGrace = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan GameProcessCheckInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DriverRecheckInterval = TimeSpan.FromSeconds(15);

    private readonly SetupStepViewModel driverStep = new(OperatingSystemName switch
    {
        "macOS" => "Capture permission",
        "Linux" => "Capture library",
        _ => "Capture driver"
    });
    private readonly SetupStepViewModel adaptersStep = new("Network adapters");
    private readonly SetupStepViewModel trafficStep = new("Game traffic");
    private readonly SetupStepViewModel serverStep = new("Server");
    private readonly SetupStepViewModel characterStep = new("Character");
    private readonly SetupStepViewModel locationStep = new("Location");

    private DispatcherTimer? setupTimer;
    private bool? captureDriverReady;
    private bool isCheckingDriver;
    private DateTime lastDriverCheckUtc;
    private DateTime? listeningSinceUtc;
    private DateTime? inGameSinceUtc;
    private bool gameProcessRunning;
    private IReadOnlyList<string> trafficRouters = [];
    private bool isCheckingGameProcess;
    private DateTime lastGameProcessCheckUtc;
    private bool troubleshootingAutoOpened;
    private SetupStage setupStage = SetupStage.CheckingDriver;
    private CaptureStatus captureStatus = CaptureStatus.NotStarted;
    private IReadOnlyList<SetupStepAction>? windowsDriverActions;
    private IReadOnlyList<SetupStepAction>? linuxDriverActions;
    private IReadOnlyList<SetupStepAction>? macPermissionActions;

    private IReadOnlyList<SetupStepViewModel>? setupSteps;
    private bool isUpdatingSetup;

    public IReadOnlyList<SetupStepViewModel> SetupSteps => setupSteps ??= [driverStep, adaptersStep, trafficStep, serverStep, characterStep, locationStep];

    [ObservableProperty] private bool showReadyView;
    [ObservableProperty] private string heroTitle = "Checking capture setup…";
    [ObservableProperty] private string heroText = "";
    [ObservableProperty] private string heroMeta = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeroIsRadar), nameof(HeroIsIdle), nameof(HeroIsWarning), nameof(HeroIsBlocked), nameof(HeroIsZone))]
    private SetupHeroKind heroKind;

    [ObservableProperty] private bool showTroubleshooting;
    [ObservableProperty] private bool isTroubleshootingOpen;
    [ObservableProperty] private string statusTitle = "Checking setup…";
    [ObservableProperty] private string statusSubtitle = "Step 1 of 6";
    [ObservableProperty] private string statusTooltip = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusIsOk), nameof(StatusIsWaiting), nameof(StatusIsWarning), nameof(StatusIsBlocked), nameof(StatusIsIdle))]
    private SetupSummarySeverity statusSeverity = SetupSummarySeverity.Waiting;

    [ObservableProperty] private string checksPassedText = "";
    [ObservableProperty] private bool allChecksPassed;
    [ObservableProperty] private bool isLocationUnrecognized;

    public bool HeroIsRadar => HeroKind is SetupHeroKind.Waiting;
    public bool HeroIsIdle => HeroKind is SetupHeroKind.Idle;
    public bool HeroIsWarning => HeroKind is SetupHeroKind.Warning;
    public bool HeroIsBlocked => HeroKind is SetupHeroKind.Blocked;
    public bool HeroIsZone => HeroKind is SetupHeroKind.Zone;
    public bool StatusIsOk => StatusSeverity is SetupSummarySeverity.Ok;
    public bool StatusIsWaiting => StatusSeverity is SetupSummarySeverity.Waiting;
    public bool StatusIsWarning => StatusSeverity is SetupSummarySeverity.Warning;
    public bool StatusIsBlocked => StatusSeverity is SetupSummarySeverity.Blocked;
    public bool StatusIsIdle => StatusSeverity is SetupSummarySeverity.Idle;

    public string TroubleshootingTitle => $"Not working? Troubleshooting for {OperatingSystemName}";
    public IReadOnlyList<TroubleshootingStep> TroubleshootingSteps { get; } = CreateTroubleshootingSteps();

    private static string OperatingSystemName =>
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macOS"
        : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "Linux"
        : "Windows";

    private static string LinuxCaptureCommand =>
        $"sudo setcap cap_net_admin,cap_net_raw+eip \"{Environment.ProcessPath ?? "/path/to/AFMDataClient_Linux64"}\"";

    private static IReadOnlyList<TroubleshootingStep> CreateTroubleshootingSteps() => OperatingSystemName switch
    {
        "Linux" =>
        [
            new("1.", "Pause VPNs and ping boosters,", "then change zones."),
            new("2.", "Re-grant capture rights", "if you moved or replaced the app. The installer does this for you, or run:", LinuxCaptureCommand),
            new("3.", "Restart the client.", "Right-click the tray icon, choose Exit, and open AFM Data Client again."),
            new("4.", "Still stuck?", "Use Copy diagnostics below and paste it in the AFM Discord.")
        ],
        "macOS" =>
        [
            new("1.", "Update capture permissions", "if the sidebar offers it, then restart the client."),
            new("2.", "Still denied?", "Log out of macOS and back in, or restart the Mac."),
            new("3.", "Pause VPNs and ping boosters,", "then change zones."),
            new("4.", "Still stuck?", "Use Copy diagnostics below and paste it in the AFM Discord.")
        ],
        _ =>
        [
            new("1.", "Pause ping boosters and VPNs", "(ExitLag, WTFast, NoPing and similar). They can route game traffic where the client can’t see it. Then change zones."),
            new("2.", "Restart the client.", "Right-click the tray icon, choose Exit, and open AFM Data Client again."),
            new("3.", "Reinstall Npcap", "from npcap.com. If WinPcap shows up under Installed apps, uninstall it first."),
            new("4.", "Still stuck?", "Use Copy diagnostics below and paste it in the AFM Discord.")
        ]
    };

    [RelayCommand]
    private Task CheckCaptureAgain() => CheckCaptureDriverAsync();

    private async Task CheckCaptureDriverAsync()
    {
        if (isCheckingDriver) return;
        isCheckingDriver = true;
        lastDriverCheckUtc = DateTime.UtcNow;
        try
        {
            captureDriverReady = await Task.Run(NpCapInstallationChecker.IsNpCapInstalled);
        }
        catch (Exception error)
        {
            Log.Warning(error, "Could not check the capture driver.");
            captureDriverReady = false;
        }
        finally
        {
            isCheckingDriver = false;
        }

        Update();
    }

    private void CheckGameProcess()
    {
        if (isCheckingGameProcess || DateTime.UtcNow - lastGameProcessCheckUtc < GameProcessCheckInterval) return;
        isCheckingGameProcess = true;
        lastGameProcessCheckUtc = DateTime.UtcNow;
        Task.Run(() =>
        {
            try
            {
                return GameProcessDetector.Scan();
            }
            catch (Exception error)
            {
                Log.Debug(error, "Could not list processes to look for the game.");
                return new GameProcessScan(false, []);
            }
        }).ContinueWith(task => Dispatcher.UIThread.Post(() =>
        {
            isCheckingGameProcess = false;
            gameProcessRunning = task.Result.IsGameRunning;
            trafficRouters = task.Result.TrafficRouters;
        }));
    }

    /// <summary>Recomputes every step now; it also runs once a second.</summary>
    public void Update()
    {
        if (UiDispatch.Repost(Update)) return;

        // Reading IsInGame can raise a player state change that lands back here.
        if (playerState is null || networkListener is null || isUpdatingSetup) return;
        isUpdatingSetup = true;
        try
        {
            UpdateCore();
        }
        finally
        {
            isUpdatingSetup = false;
        }
    }

    private void UpdateCore()
    {
        var now = DateTime.UtcNow;
        captureStatus = networkListener.GetCaptureStatus();
        var macBlocked = networkListener.IsMacOSCapturePermissionSetupRequired;
        if (captureStatus.IsStarted) listeningSinceUtc ??= now;

        var inGame = playerState.IsInGame;
        if (inGame) inGameSinceUtc ??= now;
        else inGameSinceUtc = null;

        var lastPacket = playerState.LastPacketTime;
        var everReceived = lastPacket != DateTime.MinValue;
        var quietSince = listeningSinceUtc ?? now;
        if (everReceived && lastPacket > quietSince) quietSince = lastPacket;
        var quietFor = now - quietSince;
        var loggingIn = inGame && now - inGameSinceUtc < LoginGrace;

        var server = playerState.AlbionServer;
        var character = playerState.PlayerName;
        var hasCharacter = !string.IsNullOrWhiteSpace(character);
        var location = playerState.Location;
        var locationUnset = location == AlbionLocations.Unset;
        var locationUnknown = location == AlbionLocations.Unknown;

        if (!inGame && captureStatus.IsStarted) CheckGameProcess();
        else if (inGame) gameProcessRunning = false;
        if (captureDriverReady == false && now - lastDriverCheckUtc >= DriverRecheckInterval) _ = CheckCaptureDriverAsync();

        // 1. Capture driver, library or permission.
        var driverReady = captureDriverReady == true && !macBlocked;
        if (macBlocked)
        {
            driverStep.Set(SetupStepState.Blocked, "Blocked by macOS",
                "macOS needs a one-time permission before the client can read network packets. You’ll see an administrator prompt, then the client restarts.",
                macPermissionActions);
        }
        else if (captureDriverReady is null)
        {
            driverStep.Set(SetupStepState.Active, "Checking…");
        }
        else if (captureDriverReady == false)
        {
            if (OperatingSystemName == "Linux")
            {
                driverStep.Set(SetupStepState.Blocked, "Not found",
                    "The client reads game traffic through libpcap. Install it with your package manager, then check again:",
                    linuxDriverActions, "sudo apt install libpcap0.8    or    sudo dnf install libpcap");
            }
            else
            {
                driverStep.Set(SetupStepState.Blocked, "Not installed",
                    "The client reads game traffic through Npcap, a free network capture driver. Install it with the default options, then check again.",
                    windowsDriverActions);
            }
        }
        else
        {
            driverStep.Set(SetupStepState.Done, OperatingSystemName switch { "macOS" => "Allowed", "Linux" => "libpcap found", _ => "Installed" });
        }

        // 2. Network adapters.
        var adaptersReady = false;
        if (!driverReady)
        {
            adaptersStep.Set(SetupStepState.Pending, "Starts after capture works");
        }
        else if (!captureStatus.IsStarted)
        {
            adaptersStep.Set(SetupStepState.Active, "Starting…");
        }
        else if (captureStatus.SelectedDeviceName is { } selected)
        {
            adaptersReady = true;
            adaptersStep.Set(SetupStepState.Done, $"Using {selected}");
        }
        else if (captureStatus.ListeningDeviceCount > 0)
        {
            adaptersReady = true;
            adaptersStep.Set(SetupStepState.Done,
                $"Listening on {UiFormat.Count(captureStatus.ListeningDeviceCount, "adapter")}");
        }
        else if (captureStatus.AvailableDeviceCount == 0)
        {
            adaptersStep.Set(SetupStepState.Warning, "None found",
                "No network adapter is available. Check that this computer is online. The client keeps looking.");
        }
        else
        {
            switch (OperatingSystemName)
            {
                case "Linux":
                    adaptersStep.Set(SetupStepState.Blocked, "Can’t open",
                        "The client needs permission to capture. Run this once, then restart the client:", newCode: LinuxCaptureCommand);
                    break;
                case "macOS":
                    adaptersStep.Set(SetupStepState.Blocked, "Can’t open",
                        "macOS blocked capture. Install permissions from the sidebar, then restart the client.");
                    break;
                default:
                    adaptersStep.Set(SetupStepState.Blocked, "Can’t open",
                        "Npcap is installed but couldn’t open any adapter. Reinstall Npcap with the default options, then restart the client.");
                    break;
            }
        }

        // 3. Game traffic.
        var stuck = !inGame && adaptersReady && gameProcessRunning && quietFor >= HelpDelay;
        var waitingLong = !inGame && !everReceived && quietFor >= HelpDelay;
        var routerAdvice = DescribeTrafficRouters();
        var routerStep = trafficRouters.Count > 0 ? $"Pause {TrafficRouterDetector.JoinNames(trafficRouters)}, then change zones." : null;
        if (!adaptersReady && !inGame)
        {
            trafficStep.Set(SetupStepState.Pending, UiText.None);
        }
        else if (inGame)
        {
            trafficStep.Set(SetupStepState.Done, "Receiving");
        }
        else if (stuck)
        {
            trafficStep.Set(SetupStepState.Warning, $"Nothing for {UiFormat.Clock(quietFor)}",
                $"Albion Online is running, but no game traffic reached the client. {routerStep ?? "Try the steps below."}");
        }
        else if (everReceived)
        {
            trafficStep.Set(SetupStepState.Active, $"Last packet {UiFormat.Clock(now - lastPacket)} ago",
                "Log in again and the client picks up where it left off.");
        }
        else if (waitingLong)
        {
            trafficStep.Set(SetupStepState.Warning, $"Nothing for {UiFormat.Clock(quietFor)}",
                $"Still waiting. {routerStep ?? "If the game is already open, try the steps below."}");
        }
        else
        {
            trafficStep.Set(SetupStepState.Active, "Waiting for the game…", "Start Albion Online and log in to a character.");
        }

        // 4. Server.
        if (server is not null)
        {
            serverStep.Set(inGame ? SetupStepState.Done : SetupStepState.Stale, server.Name);
        }
        else if (!inGame)
        {
            serverStep.Set(SetupStepState.Pending, adaptersReady ? "From the first game packet" : UiText.None);
        }
        else if (loggingIn)
        {
            serverStep.Set(SetupStepState.Active, "Reading…");
        }
        else
        {
            serverStep.Set(SetupStepState.Warning, "Unknown server",
                "Game traffic arrives from a server the client doesn’t know. Use Copy diagnostics below and paste it in the AFM Discord.");
        }

        // 5. Character.
        var serverReady = server is not null;
        if (hasCharacter)
        {
            characterStep.Set(inGame ? SetupStepState.Done : SetupStepState.Stale, inGame ? character : $"{character} (last session)");
        }
        else if (!inGame || !serverReady)
        {
            characterStep.Set(SetupStepState.Pending, adaptersReady ? "When you enter a zone" : UiText.None);
        }
        else if (loggingIn)
        {
            characterStep.Set(SetupStepState.Active, "Reading…");
        }
        else
        {
            characterStep.Set(SetupStepState.Active, "Not read yet",
                "Change zones once: take any zone exit or portal. The client opened after you logged in, so it missed this.");
        }

        // 6. Location.
        if (!locationUnset && !locationUnknown)
        {
            locationStep.Set(inGame ? SetupStepState.Done : SetupStepState.Stale,
                inGame ? location.FriendlyName : $"{location.FriendlyName} (last session)");
        }
        else if (!inGame || !serverReady)
        {
            locationStep.Set(SetupStepState.Pending, adaptersReady ? "When you enter a zone" : UiText.None);
        }
        else if (locationUnknown)
        {
            locationStep.Set(SetupStepState.Warning, "Unrecognized zone", "Market uploads pause here. Move to a city or another known zone.");
        }
        else if (loggingIn)
        {
            locationStep.Set(SetupStepState.Active, "Reading…");
        }
        else if (!hasCharacter)
        {
            locationStep.Set(SetupStepState.Active, "Updates with the same zone change");
        }
        else
        {
            locationStep.Set(SetupStepState.Active, "Not read yet", "Change zones once: take any zone exit or portal so the client can read where you are.");
        }

        setupStage = (macBlocked || captureDriverReady == false) ? SetupStage.DriverBlocked
            : captureDriverReady is null ? SetupStage.CheckingDriver
            : !captureStatus.IsStarted ? SetupStage.StartingCapture
            : !adaptersReady && !inGame ? (captureStatus.AvailableDeviceCount == 0 ? SetupStage.NoAdapters : SetupStage.AdaptersBlocked)
            : stuck ? SetupStage.Stuck
            : !inGame ? (everReceived ? SetupStage.Closed : SetupStage.Waiting)
            : !serverReady ? (loggingIn ? SetupStage.LoggingIn : SetupStage.UnknownServer)
            : !hasCharacter || locationUnset ? (loggingIn ? SetupStage.LoggingIn : SetupStage.ChangeZones)
            : SetupStage.Ready;

        var passed = 0;
        foreach (var step in SetupSteps) if (step.State == SetupStepState.Done) passed++;
        var firstOpen = SetupSteps.Count;
        for (var index = 0; index < SetupSteps.Count; index++)
        {
            if (SetupSteps[index].State != SetupStepState.Done)
            {
                firstOpen = index;
                break;
            }
        }

        AllChecksPassed = passed == SetupSteps.Count;
        ChecksPassedText = $"{passed} of {SetupSteps.Count} checks passed";
        IsLocationUnrecognized = inGame && locationUnknown;
        ShowReadyView = setupStage == SetupStage.Ready;

        UpdateHero(quietFor, now - lastPacket, character, location, routerAdvice);
        UpdateStatusSummary(firstOpen, quietFor);
        UpdateTroubleshooting(waitingLong);
        IsReady = setupStage == SetupStage.Ready;
    }

    /// <summary>Advice naming the VPNs and ping boosters found running, or null when there are none.</summary>
    private string? DescribeTrafficRouters()
    {
        if (trafficRouters.Count == 0) return null;
        var one = trafficRouters.Count == 1;
        var advice = $"{TrafficRouterDetector.JoinNames(trafficRouters)} {(one ? "is" : "are")} running and may be sending the game’s traffic " +
            $"where the client can’t see it. Pause {(one ? "it" : "them")}, then change zones.";
        // The README's ExitLag fix, for players who want to keep it on.
        return trafficRouters.Contains("ExitLag")
            ? advice + " In ExitLag you can instead set Tools › Packets redirection method to Legacy - NDIS."
            : advice;
    }

    private void UpdateHero(TimeSpan quietFor, TimeSpan sinceLastPacket, string character, AlbionLocation location, string? routerAdvice)
    {
        var listening = $"Listening for {UiFormat.Clock(quietFor)}";
        (HeroKind, HeroTitle, HeroText, HeroMeta) = setupStage switch
        {
            SetupStage.DriverBlocked => (SetupHeroKind.Blocked, OperatingSystemName switch
            {
                "macOS" => "Allow packet capture on this Mac",
                "Linux" => "Install libpcap to start capturing",
                _ => "Install Npcap to start capturing"
            }, "That’s the only setup step. Everything after it is detected by itself.", ""),
            SetupStage.CheckingDriver => (SetupHeroKind.Waiting, "Checking capture setup…", "This takes a moment.", ""),
            SetupStage.StartingCapture => (SetupHeroKind.Waiting, "Starting capture…", "Opening your network adapters. This takes a few seconds.", ""),
            SetupStage.NoAdapters => (SetupHeroKind.Warning, "No network adapter found",
                "The client keeps looking and starts capturing as soon as one is available.", ""),
            SetupStage.AdaptersBlocked => (SetupHeroKind.Blocked, "The client can’t open your network adapters",
                "Fix the step below, then restart the client.", ""),
            SetupStage.Stuck => (SetupHeroKind.Warning, "The game is running, but no traffic is reaching the client",
                routerAdvice ?? "Something between the game and your network card is usually the cause.", listening),
            SetupStage.Closed => (SetupHeroKind.Idle, "Albion Online closed",
                !string.IsNullOrWhiteSpace(character) && location != AlbionLocations.Unset && location != AlbionLocations.Unknown
                    ? $"{character} was last seen in {location.FriendlyName}. Log in again and the client picks up where it left off."
                    : "Log in again and the client picks up where it left off.",
                $"Last packet {UiFormat.Clock(sinceLastPacket)} ago"),
            SetupStage.Waiting when quietFor >= HelpDelay => (SetupHeroKind.Waiting, "Still waiting for game traffic",
                routerAdvice ?? "If the game is already open, something may be hiding its traffic. The steps below usually fix it.", listening),
            SetupStage.Waiting => (SetupHeroKind.Waiting, "Waiting for Albion Online",
                "Start the game and log in. This page updates by itself, so you can leave the client in the tray.",
                $"{listening}  ·  Help opens after 1 minute"),
            SetupStage.LoggingIn => (SetupHeroKind.Waiting, "Logging in…", "Game traffic found. Reading your character and location.", ""),
            SetupStage.UnknownServer => (SetupHeroKind.Warning, "Game traffic found, but the server is unknown",
                "Use Copy diagnostics below and paste it in the AFM Discord so the server can be added.", ""),
            SetupStage.ChangeZones => (SetupHeroKind.Zone, "One more step: change zones",
                "The client started after you logged in. Walk through any zone exit or portal so it can read who and where you are.", ""),
            _ => (SetupHeroKind.Idle, "", "", "")
        };
    }

    private void UpdateStatusSummary(int firstOpen, TimeSpan quietFor)
    {
        var step = $"Step {Math.Min(firstOpen + 1, SetupSteps.Count)} of {SetupSteps.Count}";
        (StatusSeverity, StatusTitle, StatusSubtitle) = setupStage switch
        {
            SetupStage.DriverBlocked => (SetupSummarySeverity.Blocked, "Capture not set up", step),
            SetupStage.CheckingDriver => (SetupSummarySeverity.Waiting, "Checking setup…", step),
            SetupStage.StartingCapture => (SetupSummarySeverity.Waiting, "Starting capture…", step),
            SetupStage.NoAdapters => (SetupSummarySeverity.Warning, "No network adapter", step),
            SetupStage.AdaptersBlocked => (SetupSummarySeverity.Blocked, "Can’t open adapters", step),
            SetupStage.Waiting => (quietFor >= HelpDelay ? SetupSummarySeverity.Warning : SetupSummarySeverity.Waiting, "Waiting for the game", step),
            SetupStage.Stuck => (SetupSummarySeverity.Warning, "No game traffic", step),
            SetupStage.Closed => (SetupSummarySeverity.Idle, "Game closed", "Waiting for you to log in"),
            SetupStage.LoggingIn => (SetupSummarySeverity.Waiting, "Logging in…", step),
            SetupStage.UnknownServer => (SetupSummarySeverity.Warning, "Unknown server", step),
            SetupStage.ChangeZones => (SetupSummarySeverity.Warning, "Change zones once", step),
            _ when OrdersEncrypted => (SetupSummarySeverity.Warning, "Orders encrypted", "Other tracking continues"),
            _ when IsLocationUnrecognized => (SetupSummarySeverity.Warning, "Unrecognized zone", ChecksPassedText),
            _ => (SetupSummarySeverity.Ok, "All set", ChecksPassedText)
        };

        var open = firstOpen < SetupSteps.Count ? SetupSteps[firstOpen] : null;
        StatusTooltip = setupStage == SetupStage.Ready
            ? OrdersEncrypted ? "Market orders are encrypted right now, so they can’t be uploaded." : "Everything the client needs is detected."
            : open is { HasHint: true } ? open.Hint : HeroText;
    }

    private void UpdateTroubleshooting(bool waitingLong)
    {
        ShowTroubleshooting = setupStage is SetupStage.Waiting or SetupStage.Stuck or SetupStage.Closed or SetupStage.UnknownServer
            or SetupStage.ChangeZones or SetupStage.NoAdapters or SetupStage.AdaptersBlocked;

        // Opens once per quiet stretch; the user can close it again.
        var needsHelp = setupStage == SetupStage.Stuck || (setupStage == SetupStage.Waiting && waitingLong);
        if (needsHelp && !troubleshootingAutoOpened)
        {
            troubleshootingAutoOpened = true;
            IsTroubleshootingOpen = true;
        }
        else if (setupStage is SetupStage.Ready or SetupStage.LoggingIn)
        {
            troubleshootingAutoOpened = false;
            IsTroubleshootingOpen = false;
        }
    }

    /// <summary>The checks and game state for Copy diagnostics, without the character's name.</summary>
    public void AppendDiagnostics(StringBuilder text, string indent)
    {
        foreach (var step in SetupSteps)
        {
            var value = step.State == SetupStepState.Pending ? "not detected" : step.Value;
            // Diagnostics get pasted in public channels, so leave the character name out.
            if (step == characterStep && step.IsChecked) value = step.IsStale ? "detected (last session)" : "detected";
            text.AppendLine($"{indent}{step.Label}: {value}");
        }

        text.AppendLine($"{indent}Game process: {(playerState.IsInGame ? "sending traffic" : gameProcessRunning ? "running" : "not detected")}");
    }
}
