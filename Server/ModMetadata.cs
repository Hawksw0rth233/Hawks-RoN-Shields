using SPTarkov.Server.Core.Models.Spt.Mod;
namespace Hawks.RonShields;
public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.hawks.ronshields";
    public string Name { get; init; } = "Ready or Not Shields";
    public string Author { get; init; } = "Hawks";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("0.5.0");
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.6");
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; }
    public string License { get; init; } = "Local conversion";
    public bool HasPrepatcher { get; init; } = false;
}
