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
        if (viewModel is not null) viewModel.PropertyChanged += ViewModelPropertyChanged;
    }

    // Resizing by hand turns off automatic height; showing or hiding a section fits it again.
    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ActivityStreamerViewModel.HasSession) or nameof(ActivityStreamerViewModel.ShowChart)
            or nameof(ActivityStreamerViewModel.ShowNothingSelected))
            SizeToContent = SizeToContent.Height;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (viewModel is not null) viewModel.PropertyChanged -= ViewModelPropertyChanged;
        base.OnClosed(e);
    }
}
