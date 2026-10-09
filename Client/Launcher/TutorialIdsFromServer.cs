using System;
using System.Collections.Generic;
using JsonType;

namespace TutorialBackport.Client;

internal static class TutorialIdsFromServer
{
    public static string LocationKey(LocationSettings settings)
    {
        foreach (KeyValuePair<string, LocationSettings.Location> pair in settings.locations)
        {
            if (string.Equals(pair.Value.Id, TutorialIds.LocationId, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Key;
            }
        }

        return TutorialIds.LocationId;
    }
}
