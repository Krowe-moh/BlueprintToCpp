using BlueRange.ViewModels.Api;
using RestSharp;

namespace BlueRange.ViewModels;

public class ApiEndpointViewModel
{
    public FortniteCentralApiEndpoint FortniteCentral;

    private readonly RestClient _client = new(new RestClientOptions
    {
        UserAgent = "BlueRange",
    });

    public ApiEndpointViewModel()
    {
        FortniteCentral = new FortniteCentralApiEndpoint(_client);
    }

    public async Task<byte[]> DownloadFileAsync(string url)
    {
        var request = new RestRequest(url);
        var data = await _client.DownloadDataAsync(request).ConfigureAwait(false);
        return data ?? [];
    }
}
