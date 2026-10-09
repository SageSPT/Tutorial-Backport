using Path = System.IO.Path;
using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils;

namespace TutorialBackport.Server;

[Injectable(InjectionType.Singleton)]
public class TutorialData(JsonUtil jsonUtil)
{
    public bool Disabled { get; set; }
    public LocationBase LocationBase { get; private set; } = null!;
    public List<object> SubtitleTracks { get; private set; } = [];
    public Dictionary<string, List<Item>> TutorialLoadout { get; private set; } = [];
    public JsonElement TutorialGlobals { get; private set; }
    public Dictionary<string, Dictionary<string, string>> Locales { get; } = [];

    public const string TutorialBotRole = "assault";

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        string basePath = TutorialIds.Db("locations", TutorialIds.LocationKey, "base.json");
        string baseJson = await File.ReadAllTextAsync(basePath, cancellationToken);
        baseJson = baseJson.Replace("\"assaultTutorial\"", "\"" + TutorialBotRole + "\"");
        LocationBase = jsonUtil.Deserialize<LocationBase>(baseJson) ?? throw new InvalidDataException("base.json did not deserialize");
        LocationBase.Banners = [];
        SubtitleTracks = jsonUtil.Deserialize<List<object>>(await File.ReadAllTextAsync(TutorialIds.Db("templates", "subtitleTracks.json"), cancellationToken)) ?? [];
        TutorialLoadout = jsonUtil.Deserialize<Dictionary<string, List<Item>>>(await File.ReadAllTextAsync(TutorialIds.Db("templates", "tutorialLoadout.json"), cancellationToken)) ?? [];
        TutorialGlobals = JsonDocument.Parse(await File.ReadAllTextAsync(TutorialIds.Db("globals", "tutorial.json"), cancellationToken)).RootElement.Clone();

        foreach (string file in Directory.GetFiles(TutorialIds.Db("locales"), "*.json"))
        {
            var keys = jsonUtil.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(file, cancellationToken));
            if (keys != null)
            {
                Locales[Path.GetFileNameWithoutExtension(file)] = keys;
            }
        }
    }
}
