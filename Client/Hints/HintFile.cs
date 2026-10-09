using System.Collections.Generic;
using Newtonsoft.Json;

namespace TutorialBackport.Client;

internal class HintFile
{
    [JsonProperty("triggers")] public List<HintData> Triggers;
    [JsonProperty("named")] public Dictionary<string, HintData> Named;
}
