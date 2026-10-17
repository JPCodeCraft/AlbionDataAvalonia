using AlbionDataAvalonia.Ui;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Views.Dialogs;

public enum DialogButtonRole { Secondary, Primary, Danger }

/// <summary>
/// The frame every app dialog shares: the app icon and input styling, a title, a message, optional content and a
/// right-aligned row of buttons with Cancel first. Subclasses add their parts in the constructor and call
/// <see cref="Finish"/> with the result; closing any other way returns the default.
/// </summary>
public abstract class AppDialog<TResult> : Window
{
    private readonly StackPanel body = new() { Spacing = 14 };
    private readonly StackPanel buttons = new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Right,
        Spacing = 8
    };
    private TResult? result;

    protected AppDialog(string title, double width = 460)
    {
        Classes.Add("AppPage");
        Title = title;
        Icon = UiAssets.WindowIcon;
        Width = width;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = new StackPanel { Margin = new Thickness(20), Spacing = 14, Children = { body, buttons } };
        // Enter confirms a safe default; otherwise focus starts on Cancel.
        Opened += (_, _) => buttons.Children.OfType<Button>().FirstOrDefault(button => button.IsDefault || button.IsCancel)?.Focus();
    }

    protected TextBlock AddHeading(string text) => Add(new TextBlock { Text = text, Classes = { "UiTitle" } });

    protected TextBlock AddMessage(string text) =>
        Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Classes = { "UiMuted" } });

    /// <summary>A smaller line, such as a hint under a field; pass "UiWarning" or "UiError" to color it.</summary>
    protected TextBlock AddNote(string text, string? tone = null)
    {
        var note = new TextBlock { Text = text, Classes = { "UiLabel" } };
        if (tone is not null) note.Classes.Add(tone);
        return Add(note);
    }

    protected T Add<T>(T control) where T : Control
    {
        body.Children.Add(control);
        return control;
    }

    protected Button AddCancelButton(string label = "Cancel")
    {
        var button = new Button { Content = label, IsCancel = true, Classes = { "UiAction" } };
        button.Click += (_, _) => Close();
        buttons.Children.Add(button);
        return button;
    }

    protected Button AddActionButton(string label, DialogButtonRole role, Action onClick, bool isDefault = false)
    {
        var button = new Button { Content = label, IsDefault = isDefault, Classes = { "UiAction" } };
        if (role == DialogButtonRole.Primary) button.Classes.Add("UiPrimary");
        else if (role == DialogButtonRole.Danger) button.Classes.Add("UiDanger");
        button.Click += (_, _) => onClick();
        buttons.Children.Add(button);
        return button;
    }

    protected void Finish(TResult value)
    {
        result = value;
        Close();
    }

    /// <summary>Shows the dialog over the owner, or on its own when there is no visible owner, and returns its result.</summary>
    public async Task<TResult?> ShowAsync(Window? owner)
    {
        if (owner is { IsVisible: true })
        {
            await ShowDialog(owner);
            return result;
        }

        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Closed += (_, _) => closed.TrySetResult();
        Show();
        await closed.Task;
        return result;
    }
}
