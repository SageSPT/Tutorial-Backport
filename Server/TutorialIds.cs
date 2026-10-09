using Path = System.IO.Path;
using System.Reflection;

namespace TutorialBackport.Server;

public static class TutorialIds
{
    public const string ModGuid = "com.sage.tutorialbackport";
    public const string LocationKey = "sandbox_start";
    public const string LocationId = "68236e8153654e8c1200798a";
    public const string IntroTrackId = "68d6c08ac4b4997eeb79db5c";

    public const string CompletedFlag = "tutorialCompleted";

    public static string ModDir => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
    public static string Db(params string[] parts) => Path.Combine(new[] { ModDir, "db" }.Concat(parts).ToArray());
}
