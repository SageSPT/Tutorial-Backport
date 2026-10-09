using System.Collections.Generic;
using Newtonsoft.Json;

namespace TutorialBackport.Client;

internal class TapeInfo
{
    [JsonProperty("tpl")] public string Tpl;
    [JsonProperty("tapeId")] public string TapeId;
    [JsonProperty("audio")] public string Audio;
    [JsonProperty("length")] public float Length;
    [JsonProperty("subtitles")] public List<TapeSubtitle> Subtitles;
}
