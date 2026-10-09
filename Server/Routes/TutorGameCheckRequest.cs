using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Utils;

namespace TutorialBackport.Server;

public record TutorGameCheckRequest : IRequestData
{
    [JsonPropertyName("skipTutorial")]
    public bool SkipTutorial { get; set; }
}
