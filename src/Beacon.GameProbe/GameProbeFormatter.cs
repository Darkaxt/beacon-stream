using System.Text;
using Beacon.Core.Games;
using Beacon.Core.Games.Steam;

namespace Beacon.GameProbe;

public static class GameProbeFormatter
{
    public static string FormatTable(GameLibrarySnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Source             Installed  Id                                Title");
        builder.AppendLine("------             ---------  --                                -----");

        foreach (GameDescriptor game in snapshot.Games)
        {
            string installed = game.Installed ? "yes" : "no";
            builder.AppendLine($"{Trim(game.Source, 18),-18} {installed,-10} {Trim(game.Id, 33),-33} {game.Title}");
        }

        if (snapshot.Diagnostics.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Diagnostics:");
            foreach (string diagnostic in snapshot.Diagnostics)
            {
                builder.AppendLine($"- {diagnostic}");
            }
        }

        return builder.ToString();
    }

    public static string FormatShortcuts(IReadOnlyList<SteamShortcut> shortcuts)
    {
        var builder = new StringBuilder();
        foreach (SteamShortcut shortcut in shortcuts)
        {
            string uri = SteamShortcutLaunchId.FromStoredAppId(shortcut.AppId).ToUri();
            builder.AppendLine($"{shortcut.AppName}\t{uri}");
        }

        return builder.ToString();
    }

    private static string Trim(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)] + ".";
}
