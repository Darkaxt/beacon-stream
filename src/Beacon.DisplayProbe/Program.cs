using Beacon.DisplayProbe;
using Beacon.Platform.Windows.Displays;

await using var api = new WindowsDisplayApi();
return await DisplayProbeApp.RunAsync(api, args, Console.Out, Console.Error);
