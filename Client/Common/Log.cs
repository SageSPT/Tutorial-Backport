using System.Collections.Generic;
using BepInEx.Logging;

namespace TutorialBackport.Client;

internal static class Log
{
    public static ManualLogSource Source;
    private static readonly HashSet<string> Written = new HashSet<string>();

    public static void Error(string category, string message) => Write(true, category, message);
    public static void Warning(string category, string message) => Write(false, category, message);

    private static void Write(bool error, string category, string message)
    {
        if (Source == null)
        {
            return;
        }

        string line = "[" + category + "] " + message;
        lock (Written)
        {
            if (!Written.Add(line))
            {
                return;
            }
        }

        if (error)
        {
            Source.LogError(line);
        }
        else
        {
            Source.LogWarning(line);
        }
    }
}
