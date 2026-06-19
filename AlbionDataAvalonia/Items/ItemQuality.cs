namespace AlbionDataAvalonia.Items;

public static class ItemQuality
{
    public static string Format(int? quality)
    {
        return quality switch
        {
            1 => "Normal",
            2 => "Good",
            3 => "Outstanding",
            4 => "Excellent",
            5 => "Masterpiece",
            _ => "Unknown"
        };
    }
}
