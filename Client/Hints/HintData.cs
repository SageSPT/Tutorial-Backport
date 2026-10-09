using System.Collections.Generic;
using Newtonsoft.Json;

namespace TutorialBackport.Client;

internal class HintData
{
    [JsonProperty("key")] public string Key;
    [JsonProperty("showKey")] public int ShowKey;
    [JsonProperty("closeKey")] public int CloseKey;
    [JsonProperty("activations")] public int Activations;
    [JsonProperty("onlyInTrigger")] public bool OnlyInTrigger;
    [JsonProperty("isImportant")] public bool IsImportant;
    [JsonProperty("ignoreQueue")] public bool IgnoreQueue;
    [JsonProperty("lifetime")] public float Lifetime;
    [JsonProperty("lines")] public List<HintLine> Lines;
}
