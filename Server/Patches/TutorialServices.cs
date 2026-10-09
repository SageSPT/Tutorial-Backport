using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Servers;

namespace TutorialBackport.Server;

[Injectable(InjectionType.Singleton)]
public class TutorialServices(SaveServer saveServer, TutorialLoot loot)
{
    public TutorialLoot Loot => loot;

    public SptProfile? Profile(MongoId sessionId) => saveServer.ProfileExists(sessionId) ? saveServer.GetProfile(sessionId) : null;
}
