namespace AlbionDataAvalonia.Ui;

/// <summary>Words the app shows in many places, so a filter or a missing value reads the same everywhere.</summary>
public static class UiText
{
    /// <summary>A filter that doesn't narrow the list.</summary>
    public const string Any = "Any";

    /// <summary>A status filter that shows every state.</summary>
    public const string All = "All";

    /// <summary>A value the game didn't send, such as an unread name or a missing number.</summary>
    public const string Unknown = "Unknown";

    /// <summary>A value that can't be worked out, such as an estimated value without a price.</summary>
    public const string Unavailable = "Unavailable";

    /// <summary>A value that older data didn't store.</summary>
    public const string NotRecorded = "Not recorded";

    /// <summary>An empty cell or a value not reached yet.</summary>
    public const string None = "—";

    /// <summary>Separates short facts on one line: "Ashvale · Europe".</summary>
    public const string Separator = " · ";
}
