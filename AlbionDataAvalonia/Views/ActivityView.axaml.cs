using AlbionDataAvalonia.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Views;

public partial class ActivityView : UserControl
{
    private ActivityViewModel? activeViewModel;
    private bool isAttached;

    public ActivityView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => { isAttached = true; SynchronizeViewModel(); };
        DetachedFromVisualTree += (_, _) => { isAttached = false; activeViewModel?.SetActive(false); };
        DataContextChanged += (_, _) => SynchronizeViewModel();
    }

    public ActivityView(ActivityViewModel viewModel) : this() => DataContext = viewModel;

    private void SynchronizeViewModel()
    {
        if (activeViewModel != DataContext)
        {
            activeViewModel?.SetActive(false);
            activeViewModel = DataContext as ActivityViewModel;
        }
        activeViewModel?.SetActive(isAttached);
    }

    private async void FinishSession_Click(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not ActivityViewModel viewModel || !viewModel.HasCurrentSession || TopLevel.GetTopLevel(this) is not Window owner) return;
        var id = viewModel.CurrentSessionId;
        if (await ConfirmAsync(owner, "Finish session?", "Your recorded activity is retained. Further activity will start a new session.", "Finish session")
            && viewModel.CurrentSessionId == id)
            viewModel.FinishCurrentSession(id);
    }

    private async void DeleteSession_Click(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not ActivityViewModel viewModel || !viewModel.CanDeleteSelectedSession || TopLevel.GetTopLevel(this) is not Window owner) return;
        var id = viewModel.SelectedSession?.Id;
        if (await ConfirmAsync(owner, "Delete session?", $"Permanently delete “{viewModel.SelectedSession?.Title}” and all its encounters and recorded events?", "Delete session")
            && viewModel.SelectedSession?.Id == id)
            await viewModel.DeleteSelectedSessionAsync();
    }

    private async void DeleteUnassignedLoot_Click(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not ActivityViewModel viewModel || TopLevel.GetTopLevel(this) is not Window owner) return;
        if (await ConfirmAsync(owner, "Delete unassigned loot?", "Permanently delete all pickups that do not belong to a session, across every character and server? Session loot is retained. This applies to all unassigned pickups, regardless of the displayed filters.", "Delete unassigned loot"))
            await viewModel.DeleteUnassignedLootAsync();
    }

    private static Task<bool> ConfirmAsync(Window owner, string title, string message, string action)
    {
        var cancel = new Button { Content = "Cancel" };
        var confirm = new Button { Content = action };
        var dialog = new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = title, FontSize = 18, FontWeight = Avalonia.Media.FontWeight.DemiBold },
                    new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, confirm } }
                }
            }
        };
        cancel.Click += (_, _) => dialog.Close(false);
        confirm.Click += (_, _) => dialog.Close(true);
        return dialog.ShowDialog<bool>(owner);
    }
}
