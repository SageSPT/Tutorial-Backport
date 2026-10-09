using System.Collections.Generic;
using Newtonsoft.Json;

namespace TutorialBackport.Client;

internal class HintLine
{
    [JsonProperty("text")] public string Text;
    [JsonProperty("style")] public string Style;
    [JsonProperty("showKeys")] public bool ShowKeys;
    [JsonProperty("keys")] public List<string> Keys;
}
