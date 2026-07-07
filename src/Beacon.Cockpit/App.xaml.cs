using System.Net.Http;
using System.Windows;
using Beacon.Cockpit.Cockpit;

namespace Beacon.Cockpit;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Uri serverUri = ResolveServerUri(e.Args);
        var httpClient = new HttpClient { BaseAddress = serverUri };
        var viewModel = new CockpitShellViewModel(new CockpitApiClient(httpClient), serverUri.ToString().TrimEnd('/'));
        var window = new MainWindow(viewModel);

        MainWindow = window;
        window.Show();

        await viewModel.RefreshAsync(CancellationToken.None);
    }

    private static Uri ResolveServerUri(IReadOnlyList<string> args)
    {
        const string defaultServer = "http://localhost:5000";

        for (int index = 0; index < args.Count; index++)
        {
            string argument = args[index];
            string? value = null;

            if (string.Equals(argument, "--server", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Count)
            {
                value = args[index + 1];
            }
            else if (argument.StartsWith("--server=", StringComparison.OrdinalIgnoreCase))
            {
                value = argument["--server=".Length..];
            }

            if (value is not null && Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
            {
                return uri;
            }
        }

        return new Uri(defaultServer);
    }
}
