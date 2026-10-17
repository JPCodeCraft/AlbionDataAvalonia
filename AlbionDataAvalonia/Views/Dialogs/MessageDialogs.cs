namespace AlbionDataAvalonia.Views.Dialogs;

/// <summary>Asks before an action. Destructive actions get the danger button and never respond to Enter.</summary>
public sealed class ConfirmDialog : AppDialog<bool>
{
    public ConfirmDialog(string title, string heading, string message, string action, bool destructive, string cancel = "Cancel")
        : base(title)
    {
        AddHeading(heading);
        AddMessage(message);
        AddCancelButton(cancel);
        AddActionButton(action, destructive ? DialogButtonRole.Danger : DialogButtonRole.Primary, () => Finish(true), isDefault: !destructive);
    }
}

/// <summary>Tells the user something that needs no decision.</summary>
public sealed class AlertDialog : AppDialog<bool>
{
    public AlertDialog(string title, string heading, string message, string button = "OK")
        : base(title, 420)
    {
        AddHeading(heading);
        AddMessage(message);
        AddActionButton(button, DialogButtonRole.Primary, () => Finish(true), isDefault: true).IsCancel = true;
    }
}
