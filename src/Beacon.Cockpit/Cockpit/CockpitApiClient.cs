using System.Net.Http;
using System.Net.Http.Json;

namespace Beacon.Cockpit.Cockpit;

public interface ICockpitApi
{
    Task<CockpitSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);

    Task RestorePhysicalAsync(CancellationToken cancellationToken);

    Task RecoverClientDisplayAsync(string clientId, CancellationToken cancellationToken);
}

public sealed class CockpitApiClient(HttpClient httpClient) : ICockpitApi
{
    public async Task<CockpitSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        CockpitSnapshot? snapshot = await httpClient.GetFromJsonAsync<CockpitSnapshot>("/admin/snapshot", cancellationToken);
        return snapshot ?? new CockpitSnapshot([], [], [], [], new CockpitGameSummary(0, []));
    }

    public async Task RestorePhysicalAsync(CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync("/admin/recovery/restore-physical", new { }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task RecoverClientDisplayAsync(string clientId, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync($"/admin/clients/{Uri.EscapeDataString(clientId)}/display/recover", new { }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
