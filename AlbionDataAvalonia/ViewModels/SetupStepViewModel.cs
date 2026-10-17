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
        nameof(IsBlocked), nameof(NeedsAttention), nameof(IsDone))]
    private SetupStepState state;

    public bool HasHint => !string.IsNullOrEmpty(Hint);
    public bool HasCode => !string.IsNullOrEmpty(Code);
    public bool HasActions => Actions.Count > 0;
    public bool IsPending => State == SetupStepState.Pending;
    public bool IsActive => State == SetupStepState.Active;
    public bool IsDone => State == SetupStepState.Done;
    public bool IsChecked => State is SetupStepState.Done or SetupStepState.Stale;
    public bool IsStale => State == SetupStepState.Stale;
    public bool IsWarning => State == SetupStepState.Warning;
    public bool IsBlocked => State == SetupStepState.Blocked;
    public bool NeedsAttention => State is SetupStepState.Warning or SetupStepState.Blocked;

    public void Set(SetupStepState newState, string newValue, string newHint = "", IReadOnlyList<SetupStepAction>? newActions = null, string newCode = "")
    {
        State = newState;
        Value = newValue;
        Hint = newHint;
        Code = newCode;
        Actions = newActions ?? [];
    }
}
