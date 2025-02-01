using BlueRange.Services;
using CUE4Parse.UE4.Assets;

namespace BlueRange.Utils;

public static class ProviderUtils
{
    public static async Task<T> LoadPackageAsync<T>(string path) where T : IPackage
    {
        return (T) await ApplicationService.CUE4Parse.Provider.LoadPackageAsync(path).ConfigureAwait(false);
    }
}
