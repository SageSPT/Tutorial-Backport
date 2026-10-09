using Newtonsoft.Json;

namespace TutorialBackport.Client;

internal class TapeSubtitle
{
    [JsonProperty("id")] public string Id;
    [JsonProperty("start")] public float Start;
    [JsonProperty("end")] public float End;
}
