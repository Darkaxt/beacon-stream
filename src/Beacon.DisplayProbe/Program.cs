using Beacon.DisplayProbe;
using Beacon.Platform.Windows.Displays;

return await DisplayProbeApp.RunAsync(new WindowsDisplayApi(), args, Console.Out, Console.Error);
