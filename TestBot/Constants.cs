namespace TestBot;

/// <summary>
/// Commands, callbacks, and identifiers used by the reference TestBot implementation.
/// </summary>
public static class Constants
{
    public const string Name = "TestBot";

    public struct Commands
    {
        public const string ClearChatData = "/clearchatdata";
        public const string GetLastGif = "/getlastgif";
    }

    public struct Callbacks
    {
        public const string ChatDataClear = "chatdata-clear";
    }
}
