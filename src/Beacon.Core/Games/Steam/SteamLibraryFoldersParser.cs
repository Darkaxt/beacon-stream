namespace Beacon.Core.Games.Steam;

public sealed record SteamLibraryFolder(string Path, IReadOnlyList<int> AppIds);

public static class SteamLibraryFoldersParser
{
    public static IReadOnlyList<SteamLibraryFolder> Parse(string text)
    {
        ValveKeyValueObject root = ValveKeyValueParser.Parse(text);
        if (!root.TryGetObject("libraryfolders", out ValveKeyValueObject libraryFolders))
        {
            return [];
        }

        var folders = new List<SteamLibraryFolder>();
        foreach (ValveKeyValueNode node in libraryFolders.Values.Values)
        {
            if (node.Object is null || !node.Object.TryGetString("path", out string path))
            {
                continue;
            }

            var appIds = new List<int>();
            if (node.Object.TryGetObject("apps", out ValveKeyValueObject apps))
            {
                foreach (string key in apps.Values.Keys)
                {
                    if (int.TryParse(key, out int appId))
                    {
                        appIds.Add(appId);
                    }
                }
            }

            folders.Add(new SteamLibraryFolder(path, appIds));
        }

        return folders;
    }
}
