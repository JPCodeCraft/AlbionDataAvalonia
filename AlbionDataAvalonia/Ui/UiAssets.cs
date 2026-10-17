using Avalonia.Controls;
using Avalonia.Platform;
using System;

namespace AlbionDataAvalonia.Ui;

/// <summary>Assets shared by windows built in code.</summary>
public static class UiAssets
{
    private static WindowIcon? windowIcon;

    /// <summary>The app icon for a window's title bar and taskbar entry.</summary>
    public static WindowIcon WindowIcon => windowIcon ??= new WindowIcon(AssetLoader.Open(new Uri("avares://AlbionDataAvalonia/Assets/logo.ico")));
}
