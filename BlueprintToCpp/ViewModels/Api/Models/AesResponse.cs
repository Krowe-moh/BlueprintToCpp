using J = Newtonsoft.Json.JsonPropertyAttribute;

namespace BlueRange.ViewModels.Api.Models;

public class AesResponse
{
    [J("mainKey")] public string MainKey { get; set; } = "0x0000000000000000000000000000000000000000000000000000000000000000";
    [J("dynamicKeys")] public List<DynamicKeyResponse> DynamicKeys { get; set; }
}

public class DynamicKeyResponse
{
    [J("key")] public string Key { get; set; }
    [J("guid")] public string Guid { get; set; }
}
