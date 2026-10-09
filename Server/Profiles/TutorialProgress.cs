using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Servers;

namespace TutorialBackport.Server;

[Injectable(InjectionType.Singleton)]
public class TutorialProgress(SaveServer saveServer)
{
    public bool IsPending(SptProfile? profile)
    {
        object? value = null;
        if (profile?.SptData?.ExtensionData?.TryGetValue(TutorialIds.CompletedFlag, out value) != true)
        {
            return false;
        }

        return value switch
        {
            bool b => !b,
            JsonElement { ValueKind: JsonValueKind.False } => true,
            _ => false
        };
    }

    public void Set(SptProfile? profile, bool completed)
    {
        if (profile?.SptData == null)
        {
            return;
        }

        profile.SptData.ExtensionData ??= new Dictionary<string?, object?>();
        profile.SptData.ExtensionData[TutorialIds.CompletedFlag] = completed;
    }

    public void ProtectExistingProfiles()
    {
        string marker = Path.Combine(TutorialIds.ModDir, "existing-profiles-protected.json");
        if (File.Exists(marker))
        {
            return;
        }

        var markedIds = new List<string>();
        foreach (var profile in saveServer.GetProfiles().Values)
        {
            if (profile.SptData == null || profile.SptData.ExtensionData?.ContainsKey(TutorialIds.CompletedFlag) == true)
            {
                continue;
            }

            Set(profile, true);
            markedIds.Add(profile.ProfileInfo?.ProfileId?.ToString() ?? "?");
        }

        File.WriteAllText(marker, JsonSerializer.Serialize(new { utc = DateTime.UtcNow.ToString("O"), profiles = markedIds }));
    }
}
