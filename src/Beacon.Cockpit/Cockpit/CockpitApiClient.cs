using System.Net.Http;
using System.Net.Http.Json;

namespace Beacon.Cockpit.Cockpit;

public interface ICockpitApi
{
    Task<CockpitSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);

    Task RestorePhysicalAsync(CancellationToken cancellationToken);

    Task MoveWindowsBackAsync(bool minimize, CancellationToken cancellationToken);

    Task CloseVirtualWindowsAsync(CancellationToken cancellationToken);

    Task TerminateVirtualProcessesAsync(CancellationToken cancellationToken);

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

    public async Task MoveWindowsBackAsync(bool minimize, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync("/admin/recovery/move-windows-back", new { minimize }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task CloseVirtualWindowsAsync(CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync("/admin/recovery/close-virtual-windows", new { }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task TerminateVirtualProcessesAsync(CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync("/admin/recovery/terminate-virtual-processes", new { }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task RecoverClientDisplayAsync(string clientId, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync($"/admin/clients/{Uri.EscapeDataString(clientId)}/display/recover", new { }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
