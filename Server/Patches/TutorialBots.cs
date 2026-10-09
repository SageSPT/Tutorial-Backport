using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using HarmonyLib;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Generators.Bot;
using SPTarkov.Server.Core.Helpers.Bot;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Bots;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils;

namespace TutorialBackport.Server;

public static class TutorialBots
{
    private static readonly Regex Tpl = new("^[0-9a-f]{24}$", RegexOptions.Compiled);

    [ThreadStatic] private static bool _generatingTutorialBot;
    private static BotType? _template;
    private static readonly object TemplateLock = new();
    private static TemplateTable _templates = null!;
    private static JsonUtil _json = null!;
    private static ISptLogger<TutorialMod> _logger = null!;

    public static void Apply(TemplateTable templates, JsonUtil json, ISptLogger<TutorialMod> logger)
    {
        _templates = templates;
        _json = json;
        _logger = logger;
        if (!File.Exists(TutorialIds.Db("bots", "assaulttutorial.json")))
        {
            logger.Warning("[TutorialBackport] bots/assaulttutorial.json missing - tutorial bots use the assault template");
            return;
        }

        var harmony = new Harmony(TutorialIds.ModGuid + ".bots");
        try
        {
            harmony.Patch(AccessTools.Method(typeof(BotGenerator), nameof(BotGenerator.PrepareAndGenerateBot)),
                prefix: new HarmonyMethod(typeof(TutorialBots), nameof(BeforePrepare)),
                finalizer: new HarmonyMethod(typeof(TutorialBots), nameof(AfterPrepare)));
            harmony.Patch(AccessTools.Method(typeof(BotHelper), nameof(BotHelper.GetBotTemplate)),
                postfix: new HarmonyMethod(typeof(TutorialBots), nameof(AfterGetBotTemplate)));
        }
        catch (Exception error)
        {
            logger.Error($"[TutorialBackport] tutorial bot template patch failed (bots use assault): {error.Message}");
        }
    }

    private static void BeforePrepare(BotGenerationDetails botGenerationDetails)
    {
        _generatingTutorialBot = botGenerationDetails is { IsPmc: not true }
                                 && string.Equals(botGenerationDetails.Role, TutorialData.TutorialBotRole, StringComparison.OrdinalIgnoreCase)
                                 && string.Equals(botGenerationDetails.Location, TutorialIds.LocationKey, StringComparison.OrdinalIgnoreCase);
    }

    private static Exception? AfterPrepare(Exception? __exception)
    {
        _generatingTutorialBot = false;
        return __exception;
    }

    private static void AfterGetBotTemplate(string role, ref BotType? __result)
    {
        if (!_generatingTutorialBot || !string.Equals(role, TutorialData.TutorialBotRole, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        BotType? tutorial = Template();
        if (tutorial != null)
        {
            __result = tutorial;
        }
    }

    private static BotType? Template()
    {
        if (_template != null)
        {
            return _template;
        }

        lock (TemplateLock)
        {
            return _template ?? Build();
        }
    }

    private static BotType? Build()
    {
        try
        {
            JsonNode root = JsonNode.Parse(File.ReadAllText(TutorialIds.Db("bots", "assaulttutorial.json")))!;
            int removed = Filter(root["inventory"]);
            _template = _json.Deserialize<BotType>(root.ToJsonString());
        }
        catch (Exception error)
        {
            _logger.Error($"[TutorialBackport] assaultTutorial template unusable (bots use assault): {error.Message}");
        }

        return _template;
    }

    private static int Filter(JsonNode? node)
    {
        int removed = 0;
        switch (node)
        {
            case JsonObject obj:
                foreach (string key in obj.Select(p => p.Key).ToList())
                {
                    if (Tpl.IsMatch(key) && !_templates.Items.ContainsKey(new MongoId(key)))
                    {
                        obj.Remove(key);
                        removed++;
                        continue;
                    }

                    removed += Filter(obj[key]);
                }

                break;
            case JsonArray array:
                for (int i = array.Count - 1; i >= 0; i--)
                {
                    if (array[i] is JsonValue value && value.TryGetValue(out string? text) && Tpl.IsMatch(text)
                        && !_templates.Items.ContainsKey(new MongoId(text)))
                    {
                        array.RemoveAt(i);
                        removed++;
                    }
                    else
                    {
                        removed += Filter(array[i]);
                    }
                }

                break;
        }

        return removed;
    }
}
