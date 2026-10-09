using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;

namespace TutorialBackport.Server;

[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public class TutorialProfileGuard(TutorialProgress progress, TutorialData data) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (!data.Disabled)
        {
            progress.ProtectExistingProfiles();
        }

        return Task.CompletedTask;
    }
}
