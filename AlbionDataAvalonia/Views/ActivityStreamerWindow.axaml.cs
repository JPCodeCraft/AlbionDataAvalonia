using AlbionDataAvalonia.ViewModels;
using Avalonia.Controls;
using System;
using System.ComponentModel;

namespace AlbionDataAvalonia.Views;

public partial class ActivityStreamerWindow : Window
{
    private ActivityStreamerViewModel? viewModel;

    public ActivityStreamerWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (viewModel is not null) viewModel.PropertyChanged -= ViewModelPropertyChanged;
        viewModel = DataContext as ActivityStreamerViewModel;
        if (viewModel is null) return;
        viewModel.PropertyChanged += ViewModelPropertyChanged;
        Width = viewModel.WindowWidth;
    }

    // Resizing by hand turns off automatic height; changing the size or a section fits the window again.
    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ActivityStreamerViewModel.WindowWidth) && viewModel is not null)
        {
            Width = viewModel.WindowWidth;
            SizeToContent = SizeToContent.Height;
        }
        else if (args.PropertyName is nameof(ActivityStreamerViewModel.HasSession) or nameof(ActivityStreamerViewModel.ShowChart)
            or nameof(ActivityStreamerViewModel.ShowNothingSelected))
            SizeToContent = SizeToContent.Height;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (viewModel is not null) viewModel.PropertyChanged -= ViewModelPropertyChanged;
        base.OnClosed(e);
    }
}
