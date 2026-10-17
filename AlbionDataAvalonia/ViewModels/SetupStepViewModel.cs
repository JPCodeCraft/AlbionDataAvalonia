using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Windows.Input;

namespace AlbionDataAvalonia.ViewModels;

public enum SetupStepState
{
    Pending,
    Active,
    Done,
    Stale,
    Warning,
    Blocked
}

public sealed record SetupStepAction(string Label, ICommand Command, bool IsPrimary = false);

public sealed record TroubleshootingStep(string Number, string Title, string Text, string? Code = null)
{
    public bool HasCode => !string.IsNullOrEmpty(Code);
}

/// <summary>One row of the Main tab setup checklist.</summary>
public sealed partial class SetupStepViewModel(string label) : ObservableObject
{
    private static readonly IBrush DoneBrush = new SolidColorBrush(Color.Parse("#4CAF50"));
    private static readonly IBrush StaleBrush = new SolidColorBrush(Color.Parse("#734CAF50"));
    private static readonly IBrush WarningBrush = new SolidColorBrush(Color.Parse("#FFA726"));
    private static readonly IBrush BlockedBrush = new SolidColorBrush(Color.Parse("#FF7043"));
    private static readonly IBrush PendingBrush = new SolidColorBrush(Color.Parse("#26FFFFFF"));

    [ObservableProperty] private string label = label;
    [ObservableProperty] private string value = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHint))]
    private string hint = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCode))]
    private string code = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActions))]
    private IReadOnlyList<SetupStepAction> actions = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPending), nameof(IsActive), nameof(IsChecked), nameof(IsStale), nameof(IsWarning),
        nameof(IsBlocked), nameof(NeedsAttention), nameof(StateBrush))]
    private SetupStepState state;

    public bool HasHint => !string.IsNullOrEmpty(Hint);
    public bool HasCode => !string.IsNullOrEmpty(Code);
    public bool HasActions => Actions.Count > 0;
    public bool IsPending => State == SetupStepState.Pending;
    public bool IsActive => State == SetupStepState.Active;
    public bool IsChecked => State is SetupStepState.Done or SetupStepState.Stale;
    public bool IsStale => State == SetupStepState.Stale;
    public bool IsWarning => State == SetupStepState.Warning;
    public bool IsBlocked => State == SetupStepState.Blocked;
    public bool NeedsAttention => State is SetupStepState.Warning or SetupStepState.Blocked;

    public IBrush StateBrush => State switch
    {
        SetupStepState.Done => DoneBrush,
        SetupStepState.Stale => StaleBrush,
        SetupStepState.Active or SetupStepState.Warning => WarningBrush,
        SetupStepState.Blocked => BlockedBrush,
        _ => PendingBrush
    };

    public void Set(SetupStepState newState, string newValue, string newHint = "", IReadOnlyList<SetupStepAction>? newActions = null, string newCode = "")
    {
        State = newState;
        Value = newValue;
        Hint = newHint;
        Code = newCode;
        Actions = newActions ?? [];
    }
}
