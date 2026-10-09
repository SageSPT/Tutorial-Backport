using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Utils;

namespace TutorialBackport.Server;

[Injectable(TypePriority = OnLoadOrder.Routers + 10)]
public class TutorialRouter(JsonUtil json, TutorialRouteHandlers handlers) : StaticRouter(json,
[
    new RouteAction<TutorGameCheckRequest>("/client/tutor-game/check",
        (_, info, sessionId, _, _) => new ValueTask<string>(handlers.Check(sessionId, info))),
    new RouteAction<EmptyRequestData>("/client/tutor-game/profile",
        (_, _, sessionId, _, _) => new ValueTask<string>(handlers.Profile(sessionId))),
    new RouteAction<EmptyRequestData>("/client/subtitle-track/list",
        (_, _, _, _, _) => new ValueTask<string>(handlers.SubtitleTracks())),
    new RouteAction<EmptyRequestData>("/client/tutor-game/config",
        (_, _, _, _, _) => new ValueTask<string>(handlers.Config()))
]);
