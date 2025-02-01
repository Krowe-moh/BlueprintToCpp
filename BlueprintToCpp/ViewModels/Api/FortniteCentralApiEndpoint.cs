using BlueRange.ViewModels.Api.Models;
using RestSharp;
using Serilog;

namespace BlueRange.ViewModels.Api;

public class FortniteCentralApiEndpoint
{
    private readonly RestClient _client;

    public FortniteCentralApiEndpoint(RestClient client)
    {
        _client = client;
    }

    public async Task<AesResponse> GetAesAsync()
    {
        var request = new RestRequest("https://fortnitecentral.genxgames.gg/api/v1/aes");
        var response = await _client.ExecuteAsync<AesResponse>(request).ConfigureAwait(false);
        Log.Information("[{Method}] [{Status}({StatusCode})] '{Resource}'", request.Method, response.StatusDescription, (int) response.StatusCode, request.Resource);
        return response.Data ?? new AesResponse();
    }

    public async Task<MappingsResponse[]> GetMappingsAsync()
    {
        var request = new RestRequest("https://fortnitecentral.genxgames.gg/api/v1/mappings");
        var response = await _client.ExecuteAsync<MappingsResponse[]>(request).ConfigureAwait(false);
        Log.Information("[{Method}] [{Status}({StatusCode})] '{Resource}'", request.Method, response.StatusDescription, (int) response.StatusCode, request.Resource);
        return response.Data ?? [];
    }
}
