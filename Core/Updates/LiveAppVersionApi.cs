using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Iris.Core.Networking;

namespace Iris.Core.Updates;

/// <summary><see cref="IAppVersionApi"/> over the API (api-contract §17). Public: no session, no refresh.</summary>
public sealed class LiveAppVersionApi(ApiClient api) : IAppVersionApi
{
    public Task<AppVersionCheckDto> GetAsync(string currentVersion, CancellationToken ct) => api.SendAsync(
        new ApiRequest(HttpMethod.Get, $"app-version?platform=windows&version={Uri.EscapeDataString(currentVersion)}", Anonymous: true),
        IrisJsonContext.Default.AppVersionCheckDto,
        ct);
}
