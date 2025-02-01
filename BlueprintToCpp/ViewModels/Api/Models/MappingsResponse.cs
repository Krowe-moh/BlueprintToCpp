using J = Newtonsoft.Json.JsonPropertyAttribute;

namespace BlueRange.ViewModels.Api.Models;

public class MappingsResponse
{
    [J("url")] public string Url { get; set; }
    [J("fileName")] public string FileName { get; set; }
}
