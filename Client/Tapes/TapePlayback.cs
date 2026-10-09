using Newtonsoft.Json;

namespace TutorialBackport.Client;

internal class TapePlayback
{
    [JsonProperty("baseVolume")] public float BaseVolume = 0.68f;
}
