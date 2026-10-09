using System.Collections.Generic;
using Newtonsoft.Json;

namespace TutorialBackport.Client;

internal class TapeFile
{
    [JsonProperty("recorder")] public string Recorder;
    [JsonProperty("playback")] public TapePlayback Playback;
    [JsonProperty("tapes")] public List<TapeInfo> Tapes;
}
