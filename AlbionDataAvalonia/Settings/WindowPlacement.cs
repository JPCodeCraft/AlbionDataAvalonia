namespace AlbionDataAvalonia.Settings;

// A window's last normal spot: position in physical pixels on the whole desktop, size in
// device-independent units, and whether it was maximized over that spot.
public sealed record WindowPlacement(int X, int Y, double Width, double Height, bool Maximized = false);
