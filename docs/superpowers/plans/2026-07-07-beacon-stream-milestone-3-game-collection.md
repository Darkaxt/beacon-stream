# Beacon Stream Milestone 3 Game Collection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build Beacon's normalized game library for Steam official apps, Steam non-Steam shortcuts, Heroic, Hydra, manual entries, SteamGridDB artwork, generated fallback covers, dedupe, and launch intent exposure.

**Architecture:** Keep game discovery in `Beacon.Core` behind provider interfaces, with platform-specific file/database discovery in small provider classes and no display-policy leakage into game profiles. Normalize every source into `GameDescriptor` plus `GameLaunchIntent`, `GameArtwork`, installed state, and process hints, then aggregate and dedupe through a deterministic `GameLibraryService`. Server and Client Lab consume the normalized library rather than hand-authored Apollo/Sunshine `apps.json` mappings.

**Tech Stack:** .NET 10, xUnit, minimal APIs, `HttpClient`, `System.Text.Json`, Steam VDF fixture parsers, `Microsoft.Data.Sqlite` for Hydra, Vite/Vitest/Playwright for Client Lab.

---

## Research Anchors

- Valve Developer Community documents that non-Steam shortcuts are stored in `Steam/userdata/<user>/config/shortcuts.vdf`.
- ValveSoftware/steam-for-linux issue #9463 reports current Steam clients can make non-Steam Big Picture/rungame IDs non-repeatable from CRC alone. Therefore Beacon must parse the stored shortcut `appid` when present and convert it to the long launch id.
- The mature workaround used by Steam tooling is: `longRungameId = ((uint)shortcutAppId << 32) | 0x02000000`.
- Heroic project docs state that general config is `config.json`, cache is under `store`, and game settings/logs are in `GamesConfig`. Local probe on this machine also found `%APPDATA%\heroic\gog_store\installed.json`, `%APPDATA%\heroic\GamesConfig`, and `%APPDATA%\heroic\sideload_apps`.
- Local Hydra probe found `%APPDATA%\hydra\hydra.db`; the `game` table includes `id`, `objectID`, `remoteId`, `title`, `iconUrl`, `downloadPath`, `executablePath`, `shop`, `status`, and `isDeleted`.
- SteamGridDB v2 is the artwork source. API keys must come from configuration/environment, never from committed files.

## Scope Boundaries

- Implement game library and launch intent only. Do not add per-game display topology policy.
- Do not copy source from Apollo, Sunshine, Heroic, Hydra, or Steam tooling. Use their behavior as reference only.
- Do not store the user's SteamGridDB API key in git.
- Do not use symlinks.
- Do not introduce cancellation timeouts as lifecycle logic.

## File Structure

- Modify: `src/Beacon.Core/Games/GameDescriptor.cs` - normalized game model.
- Create: `src/Beacon.Core/Games/GameLaunchIntent.cs` - launch type and command.
- Create: `src/Beacon.Core/Games/GameArtwork.cs` - artwork path/source model.
- Create: `src/Beacon.Core/Games/GameProcessHints.cs` - optional executable/process hints.
- Create: `src/Beacon.Core/Games/GameLibrarySnapshot.cs` - immutable scan result.
- Create: `src/Beacon.Core/Games/IGameLibraryProvider.cs` - provider interface.
- Create: `src/Beacon.Core/Games/GameLibraryService.cs` - aggregate, dedupe, enrich.
- Create: `src/Beacon.Core/Games/Artwork/IArtworkProvider.cs` - artwork lookup boundary.
- Create: `src/Beacon.Core/Games/Artwork/GeneratedFallbackCoverProvider.cs` - generated title cover.
- Create: `src/Beacon.Core/Games/Steam/ValveKeyValueParser.cs` - text ACF/libraryfolders parser.
- Create: `src/Beacon.Core/Games/Steam/SteamLibraryFoldersParser.cs` - Steam library roots and app ids.
- Create: `src/Beacon.Core/Games/Steam/SteamAppManifestParser.cs` - official app manifests.
- Create: `src/Beacon.Core/Games/Steam/SteamShortcutBinaryParser.cs` - binary `shortcuts.vdf` parser.
- Create: `src/Beacon.Core/Games/Steam/SteamShortcutLaunchId.cs` - 32-bit to 64-bit launch id conversion and legacy fallback.
- Create: `src/Beacon.Core/Games/Steam/SteamGameLibraryProvider.cs` - official plus shortcut provider.
- Create: `src/Beacon.Core/Games/Heroic/HeroicGameLibraryProvider.cs` - Heroic JSON provider.
- Create: `src/Beacon.Core/Games/Hydra/HydraGameLibraryProvider.cs` - Hydra SQLite provider.
- Create: `src/Beacon.Core/Games/Manual/ManualGameLibraryProvider.cs` - Beacon manual and optional legacy mappings.
- Create: `src/Beacon.Core/Games/SteamGridDb/SteamGridDbArtworkProvider.cs` - HTTP artwork provider.
- Modify: `src/Beacon.Server/Api/ClientEndpoints.cs` - expose library endpoint and plan-by-game-id support.
- Modify: `src/Beacon.ClientLab/src/main.ts` - show library and select normalized game.
- Create tests under `tests/Beacon.Core.Tests/Games/**`.
- Modify server and client tests to cover normalized library flows.
- Update docs: `README.md`, `docs/extraction-map.md`, and `docs/superpowers/specs/2026-06-03-personal-streaming-orchestrator-design.md` if the requirement register needs clarified wording.

## Task 1: Normalize Game Model

**Files:**
- Modify: `src/Beacon.Core/Games/GameDescriptor.cs`
- Create: `src/Beacon.Core/Games/GameLaunchIntent.cs`
- Create: `src/Beacon.Core/Games/GameArtwork.cs`
- Create: `src/Beacon.Core/Games/GameProcessHints.cs`
- Create: `src/Beacon.Core/Games/GameLibrarySnapshot.cs`
- Test: `tests/Beacon.Core.Tests/Games/GameDescriptorTests.cs`

- [ ] **Step 1: Write failing model tests**

```csharp
using Beacon.Core.Games;

namespace Beacon.Core.Tests.Games;

public sealed class GameDescriptorTests
{
    [Fact]
    public void GameDescriptorCarriesLaunchArtworkInstalledAndHintsWithoutDisplayPolicy()
    {
        var game = new GameDescriptor(
            Id: "steam-shortcut:4261190003",
            Title: "Stranger of Sword City",
            Source: "steam-shortcut",
            Launch: new GameLaunchIntent("steam-rungameid", "steam://rungameid/18301671704960696320"),
            Artwork: new GameArtwork("C:/ProgramData/BeaconStream/artwork/stranger.png", "steamgriddb"),
            Installed: true,
            ProcessHints: new GameProcessHints("SoSC.exe", "D:/Games/Saviors of Sapphire Wings Stranger of Sword City Revisited/SoSC"));

        Assert.Equal("steam-rungameid", game.Launch.Type);
        Assert.Equal("steamgriddb", game.Artwork.Source);
        Assert.True(game.Installed);
        Assert.Equal("SoSC.exe", game.ProcessHints.ExecutableName);
        Assert.DoesNotContain(game.GetType().GetProperties().Select(property => property.Name), name => name.Contains("Display", StringComparison.OrdinalIgnoreCase));
    }
}
```

- [ ] **Step 2: Verify the test fails**

Run: `dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter GameDescriptorTests`

Expected: compile failure because `GameLaunchIntent`, `GameArtwork`, `GameProcessHints`, and the expanded `GameDescriptor` constructor do not exist.

- [ ] **Step 3: Implement the minimal model**

```csharp
namespace Beacon.Core.Games;

public sealed record GameDescriptor(
    string Id,
    string Title,
    string Source,
    GameLaunchIntent Launch,
    GameArtwork Artwork,
    bool Installed,
    GameProcessHints ProcessHints);
```

```csharp
namespace Beacon.Core.Games;

public sealed record GameLaunchIntent(string Type, string Command);
```

```csharp
namespace Beacon.Core.Games;

public sealed record GameArtwork(string? CoverPath, string Source);
```

```csharp
namespace Beacon.Core.Games;

public sealed record GameProcessHints(string? ExecutableName, string? WorkingDirectory);
```

```csharp
namespace Beacon.Core.Games;

public sealed record GameLibrarySnapshot(IReadOnlyList<GameDescriptor> Games, IReadOnlyList<string> Diagnostics);
```

- [ ] **Step 4: Update existing call sites**

Replace existing three-argument construction with a helper inside tests and server request mapping:

```csharp
private static GameDescriptor CreateRequestedGame(string appId, string title, string source) =>
    new(
        appId,
        title,
        source,
        new GameLaunchIntent("manual-request", appId),
        new GameArtwork(null, "none"),
        Installed: true,
        new GameProcessHints(null, null));
```

- [ ] **Step 5: Verify**

Run: `dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter GameDescriptorTests`

Expected: `Passed`.

- [ ] **Step 6: Commit**

```bash
git add src/Beacon.Core/Games tests/Beacon.Core.Tests/Games tests/Beacon.Core.Tests/Sessions src/Beacon.Server/Api/ClientEndpoints.cs
git commit -m "Add normalized game descriptor model"
```

## Task 2: Steam Text VDF Parsers

**Files:**
- Create: `src/Beacon.Core/Games/Steam/ValveKeyValueParser.cs`
- Create: `src/Beacon.Core/Games/Steam/SteamLibraryFoldersParser.cs`
- Create: `src/Beacon.Core/Games/Steam/SteamAppManifestParser.cs`
- Test: `tests/Beacon.Core.Tests/Games/Steam/SteamTextVdfParserTests.cs`

- [ ] **Step 1: Write failing parser tests**

```csharp
using Beacon.Core.Games.Steam;

namespace Beacon.Core.Tests.Games.Steam;

public sealed class SteamTextVdfParserTests
{
    [Fact]
    public void ReadsLibraryFoldersAndAppIds()
    {
        const string text = """
        "libraryfolders"
        {
            "0"
            {
                "path" "D:\\Steam"
                "apps"
                {
                    "1086940" "159037966816"
                    "1449690" "48816593394"
                }
            }
        }
        """;

        IReadOnlyList<SteamLibraryFolder> folders = SteamLibraryFoldersParser.Parse(text);

        SteamLibraryFolder folder = Assert.Single(folders);
        Assert.Equal(@"D:\Steam", folder.Path);
        Assert.Equal([1086940, 1449690], folder.AppIds);
    }

    [Fact]
    public void ReadsAppManifestTitleAndInstallDir()
    {
        const string text = """
        "AppState"
        {
            "appid" "1086940"
            "name" "Baldur's Gate 3"
            "installdir" "Baldurs Gate 3"
            "StateFlags" "4"
        }
        """;

        SteamAppManifest manifest = SteamAppManifestParser.Parse(text, @"D:\Steam\steamapps\appmanifest_1086940.acf");

        Assert.Equal(1086940, manifest.AppId);
        Assert.Equal("Baldur's Gate 3", manifest.Name);
        Assert.Equal(@"D:\Steam\steamapps\common\Baldurs Gate 3", manifest.InstallPath);
        Assert.True(manifest.Installed);
    }
}
```

- [ ] **Step 2: Verify red**

Run: `dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter SteamTextVdfParserTests`

Expected: compile failure because parser types do not exist.

- [ ] **Step 3: Implement text VDF parsing**

Implement a small quote/block parser that returns nested dictionaries. The parser only needs quoted keys, quoted values, and `{}` blocks for Steam text VDF.

```csharp
namespace Beacon.Core.Games.Steam;

public sealed record SteamLibraryFolder(string Path, IReadOnlyList<int> AppIds);

public sealed record SteamAppManifest(int AppId, string Name, string InstallPath, bool Installed);
```

- [ ] **Step 4: Verify**

Run: `dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter SteamTextVdfParserTests`

Expected: `Passed`.

- [ ] **Step 5: Commit**

```bash
git add src/Beacon.Core/Games/Steam tests/Beacon.Core.Tests/Games/Steam
git commit -m "Parse Steam library and app manifests"
```

## Task 3: Steam Shortcut Binary Parser And Launch IDs

**Files:**
- Create: `src/Beacon.Core/Games/Steam/SteamShortcutBinaryParser.cs`
- Create: `src/Beacon.Core/Games/Steam/SteamShortcutLaunchId.cs`
- Test: `tests/Beacon.Core.Tests/Games/Steam/SteamShortcutBinaryParserTests.cs`

- [ ] **Step 1: Write failing shortcut tests**

```csharp
using System.Text;
using Beacon.Core.Games.Steam;

namespace Beacon.Core.Tests.Games.Steam;

public sealed class SteamShortcutBinaryParserTests
{
    [Fact]
    public void ParsesStoredAppIdAndBuildsLongRungameId()
    {
        byte[] shortcut = SteamShortcutFixture.Create(
            appId: unchecked((int)0xFDFC9973),
            appName: "DRAGON QUEST III HD-2D Remake",
            exe: "\"D:\\Games\\Dragon Quest III HD-2D Remake\\DQIIIHD2DRemake.exe\"",
            startDir: "\"D:\\Games\\Dragon Quest III HD-2D Remake\"");

        SteamShortcut parsed = Assert.Single(SteamShortcutBinaryParser.Parse(shortcut));

        Assert.Equal(unchecked((int)0xFDFC9973), parsed.AppId);
        Assert.Equal("DRAGON QUEST III HD-2D Remake", parsed.AppName);
        Assert.Equal("steam://rungameid/18301671704960696320", SteamShortcutLaunchId.FromStoredAppId(parsed.AppId).ToUri());
    }

    private static class SteamShortcutFixture
    {
        public static byte[] Create(int appId, string appName, string exe, string startDir)
        {
            using var stream = new MemoryStream();
            WriteByte(stream, 0); WriteString(stream, "shortcuts");
            WriteByte(stream, 0); WriteString(stream, "0");
            WriteByte(stream, 2); WriteString(stream, "appid"); stream.Write(BitConverter.GetBytes(appId));
            WriteByte(stream, 1); WriteString(stream, "appname"); WriteString(stream, appName);
            WriteByte(stream, 1); WriteString(stream, "Exe"); WriteString(stream, exe);
            WriteByte(stream, 1); WriteString(stream, "StartDir"); WriteString(stream, startDir);
            WriteByte(stream, 8);
            WriteByte(stream, 8);
            return stream.ToArray();
        }

        private static void WriteByte(Stream stream, byte value) => stream.WriteByte(value);

        private static void WriteString(Stream stream, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            stream.Write(bytes);
            stream.WriteByte(0);
        }
    }
}
```

- [ ] **Step 2: Verify red**

Run: `dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter SteamShortcutBinaryParserTests`

Expected: compile failure because parser types do not exist.

- [ ] **Step 3: Implement parser and conversion**

```csharp
namespace Beacon.Core.Games.Steam;

public sealed record SteamShortcut(int AppId, string AppName, string Exe, string StartDir, string? Icon);

public readonly record struct SteamShortcutLaunchId(ulong Value)
{
    private const ulong LowerBits = 0x02000000UL;

    public static SteamShortcutLaunchId FromStoredAppId(int appId) =>
        new(((ulong)unchecked((uint)appId) << 32) | LowerBits);

    public string ToUri() => $"steam://rungameid/{Value}";
}
```

The binary parser must support object start (`0`), string (`1`), int32 (`2`), and object end (`8`). It must ignore unknown fields and collect only `appid`, `appname`, `Exe`, `StartDir`, and `icon`.

- [ ] **Step 4: Verify**

Run: `dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter SteamShortcutBinaryParserTests`

Expected: `Passed`.

- [ ] **Step 5: Commit**

```bash
git add src/Beacon.Core/Games/Steam tests/Beacon.Core.Tests/Games/Steam
git commit -m "Parse Steam shortcuts with stored launch ids"
```

## Task 4: Steam Game Provider

**Files:**
- Create: `src/Beacon.Core/Games/IGameLibraryProvider.cs`
- Create: `src/Beacon.Core/Games/Steam/SteamGameLibraryProvider.cs`
- Test: `tests/Beacon.Core.Tests/Games/Steam/SteamGameLibraryProviderTests.cs`

- [ ] **Step 1: Write failing provider tests**

```csharp
using Beacon.Core.Games;
using Beacon.Core.Games.Steam;

namespace Beacon.Core.Tests.Games.Steam;

public sealed class SteamGameLibraryProviderTests
{
    [Fact]
    public async Task DiscoversOfficialAppsAndNonSteamShortcuts()
    {
        using var fixture = SteamProviderFixture.Create();

        IGameLibraryProvider provider = new SteamGameLibraryProvider(fixture.SteamRoot);
        GameLibrarySnapshot snapshot = await provider.ScanAsync(CancellationToken.None);

        Assert.Contains(snapshot.Games, game =>
            game.Id == "steam:1086940" &&
            game.Title == "Baldur's Gate 3" &&
            game.Launch.Command == "steam://run/1086940" &&
            game.Source == "steam");

        Assert.Contains(snapshot.Games, game =>
            game.Id.StartsWith("steam-shortcut:", StringComparison.Ordinal) &&
            game.Title == "DRAGON QUEST III HD-2D Remake" &&
            game.Launch.Type == "steam-rungameid" &&
            game.Launch.Command.StartsWith("steam://rungameid/", StringComparison.Ordinal));
    }
}
```

- [ ] **Step 2: Verify red**

Run: `dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter SteamGameLibraryProviderTests`

Expected: compile failure because provider types do not exist.

- [ ] **Step 3: Implement provider**

```csharp
namespace Beacon.Core.Games;

public interface IGameLibraryProvider
{
    string Name { get; }
    Task<GameLibrarySnapshot> ScanAsync(CancellationToken cancellationToken);
}
```

Provider behavior:

- Read `config/libraryfolders.vdf`.
- Read `steamapps/appmanifest_*.acf` under each library root.
- Read `userdata/*/config/shortcuts.vdf`.
- Build Steam official launch command as `steam://run/{appId}`.
- Build shortcut launch command from the stored shortcut `appid`.
- Add diagnostics for unreadable files and continue scanning other files.

- [ ] **Step 4: Verify**

Run: `dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter SteamGameLibraryProviderTests`

Expected: `Passed`.

- [ ] **Step 5: Commit**

```bash
git add src/Beacon.Core/Games tests/Beacon.Core.Tests/Games
git commit -m "Discover Steam games and shortcuts"
```

## Task 5: Heroic Provider

**Files:**
- Create: `src/Beacon.Core/Games/Heroic/HeroicGameLibraryProvider.cs`
- Test: `tests/Beacon.Core.Tests/Games/Heroic/HeroicGameLibraryProviderTests.cs`

- [ ] **Step 1: Write failing Heroic tests**

```csharp
using Beacon.Core.Games;
using Beacon.Core.Games.Heroic;

namespace Beacon.Core.Tests.Games.Heroic;

public sealed class HeroicGameLibraryProviderTests
{
    [Fact]
    public async Task ReadsGogInstalledJsonAndGamesConfig()
    {
        using var fixture = HeroicFixture.Create();
        fixture.WriteGogInstalled("""
        {"installed":[{"appName":"1453375253","title":"Stardew Valley","install_path":"D:/Games/Heroic/Stardew Valley","executable":"Stardew Valley.exe","version":"1.6.14"}]}
        """);
        fixture.WriteGameConfig("1453375253.json", """{"appName":"1453375253","title":"Stardew Valley"}""");

        IGameLibraryProvider provider = new HeroicGameLibraryProvider(fixture.Root);
        GameLibrarySnapshot snapshot = await provider.ScanAsync(CancellationToken.None);

        GameDescriptor game = Assert.Single(snapshot.Games);
        Assert.Equal("heroic:gog:1453375253", game.Id);
        Assert.Equal("Stardew Valley", game.Title);
        Assert.Equal("process", game.Launch.Type);
        Assert.Contains("Stardew Valley.exe", game.Launch.Command, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Verify red**

Run: `dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter HeroicGameLibraryProviderTests`

Expected: compile failure because Heroic provider does not exist.

- [ ] **Step 3: Implement tolerant JSON provider**

Provider behavior:

- Read `gog_store/installed.json` when present.
- Read `legendaryConfig/legendary/installed.json` if present in a later fixture.
- Read `sideload_apps/*.json` when present.
- Join `GamesConfig/<appName>.json` only for extra title/executable hints.
- Do not read Heroic auth/token files.
- Add diagnostics for unknown schemas and empty stores.

- [ ] **Step 4: Verify**

Run: `dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter HeroicGameLibraryProviderTests`

Expected: `Passed`.

- [ ] **Step 5: Commit**

```bash
git add src/Beacon.Core/Games/Heroic tests/Beacon.Core.Tests/Games/Heroic
git commit -m "Discover Heroic games"
```

## Task 6: Hydra Provider

**Files:**
- Modify: `src/Beacon.Core/Beacon.Core.csproj`
- Create: `src/Beacon.Core/Games/Hydra/HydraGameLibraryProvider.cs`
- Test: `tests/Beacon.Core.Tests/Games/Hydra/HydraGameLibraryProviderTests.cs`

- [ ] **Step 1: Add failing Hydra test**

```csharp
using Beacon.Core.Games;
using Beacon.Core.Games.Hydra;
using Microsoft.Data.Sqlite;

namespace Beacon.Core.Tests.Games.Hydra;

public sealed class HydraGameLibraryProviderTests
{
    [Fact]
    public async Task ReadsInstalledHydraGamesFromSqlite()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        await using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
            create table game (id integer primary key, objectID text not null, title text not null, executablePath text, shop text not null, status text, isDeleted boolean not null default 0);
            insert into game (objectID,title,executablePath,shop,status,isDeleted) values ('hydra-1','Persona 5 Royal','D:/Games/Persona5/P5R.exe','hydra','complete',0);
            """;
            await command.ExecuteNonQueryAsync();
        }

        IGameLibraryProvider provider = new HydraGameLibraryProvider(dbPath);
        GameLibrarySnapshot snapshot = await provider.ScanAsync(CancellationToken.None);

        GameDescriptor game = Assert.Single(snapshot.Games);
        Assert.Equal("hydra:hydra-1", game.Id);
        Assert.Equal("Persona 5 Royal", game.Title);
        Assert.Equal("process", game.Launch.Type);
        Assert.Equal("D:/Games/Persona5/P5R.exe", game.Launch.Command);
    }
}
```

- [ ] **Step 2: Verify red**

Run: `dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter HydraGameLibraryProviderTests`

Expected: compile failure for missing provider and possibly missing `Microsoft.Data.Sqlite`.

- [ ] **Step 3: Add package and provider**

Run: `dotnet add src/Beacon.Core/Beacon.Core.csproj package Microsoft.Data.Sqlite`

Provider query:

```sql
select objectID, title, executablePath, shop, status, isDeleted
from game
where coalesce(isDeleted, 0) = 0
```

Provider behavior:

- Skip rows without title.
- Mark installed when `executablePath` is not empty and `status` is not a deleted/uninstalled value.
- Add diagnostics when the database is missing, locked, or schema is missing columns.

- [ ] **Step 4: Verify**

Run: `dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter HydraGameLibraryProviderTests`

Expected: `Passed`.

- [ ] **Step 5: Commit**

```bash
git add src/Beacon.Core/Beacon.Core.csproj src/Beacon.Core/Games/Hydra tests/Beacon.Core.Tests/Games/Hydra
git commit -m "Discover Hydra games"
```

## Task 7: SteamGridDB Artwork And Fallback Covers

**Files:**
- Create: `src/Beacon.Core/Games/Artwork/IArtworkProvider.cs`
- Create: `src/Beacon.Core/Games/Artwork/GeneratedFallbackCoverProvider.cs`
- Create: `src/Beacon.Core/Games/SteamGridDb/SteamGridDbArtworkProvider.cs`
- Test: `tests/Beacon.Core.Tests/Games/Artwork/ArtworkProviderTests.cs`
- Test: `tests/Beacon.Core.Tests/Games/SteamGridDb/SteamGridDbArtworkProviderTests.cs`

- [ ] **Step 1: Write failing artwork tests**

```csharp
using Beacon.Core.Games;
using Beacon.Core.Games.Artwork;

namespace Beacon.Core.Tests.Games.Artwork;

public sealed class ArtworkProviderTests
{
    [Fact]
    public async Task GeneratedFallbackCoverWritesReadableSvg()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var provider = new GeneratedFallbackCoverProvider(root);

        GameArtwork artwork = await provider.GetArtworkAsync(new GameArtworkRequest("Dispatch", "manual:dispatch", null), CancellationToken.None);

        Assert.Equal("generated", artwork.Source);
        Assert.NotNull(artwork.CoverPath);
        string svg = await File.ReadAllTextAsync(artwork.CoverPath);
        Assert.Contains("Dispatch", svg, StringComparison.Ordinal);
        Assert.Contains("<svg", svg, StringComparison.Ordinal);
    }
}
```

```csharp
using Beacon.Core.Games;
using Beacon.Core.Games.SteamGridDb;

namespace Beacon.Core.Tests.Games.SteamGridDb;

public sealed class SteamGridDbArtworkProviderTests
{
    [Fact]
    public async Task ChoosesFirstExactCandidateWithUsableGrid()
    {
        var handler = new FakeSteamGridDbHandler()
            .WithJson("/api/v2/search/autocomplete/Dispatch", """{"success":true,"data":[{"id":1,"name":"Dispatch","types":["game"]},{"id":2,"name":"Dispatch","types":["game"]}]}""")
            .WithJson("/api/v2/grids/game/1", """{"success":true,"data":[]}""")
            .WithJson("/api/v2/grids/game/2", """{"success":true,"data":[{"url":"https://cdn.example/dispatch.png","width":600,"height":900}]}""");

        var provider = new SteamGridDbArtworkProvider(new HttpClient(handler) { BaseAddress = new Uri("https://www.steamgriddb.com") }, "test-key", Path.GetTempPath());

        GameArtwork artwork = await provider.GetArtworkAsync(new GameArtworkRequest("Dispatch", "manual:dispatch", null), CancellationToken.None);

        Assert.Equal("steamgriddb", artwork.Source);
        Assert.EndsWith(".png", artwork.CoverPath, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Verify red**

Run: `dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter "ArtworkProviderTests|SteamGridDbArtworkProviderTests"`

Expected: compile failure because artwork providers do not exist.

- [ ] **Step 3: Implement providers**

```csharp
namespace Beacon.Core.Games.Artwork;

public sealed record GameArtworkRequest(string Title, string GameId, int? SteamAppId);

public interface IArtworkProvider
{
    Task<GameArtwork> GetArtworkAsync(GameArtworkRequest request, CancellationToken cancellationToken);
}
```

SteamGridDB provider behavior:

- Require API key constructor value; if blank, return `GameArtwork(null, "none")`.
- Search autocomplete by title.
- Exact-name candidates compare case-insensitively.
- For each exact candidate, request grids and pick the first static grid URL with usable dimensions.
- Download artwork to configured cache root using a sanitized game id.
- Do not log or expose the API key.

Fallback provider behavior:

- Generate SVG with dark background, title text wrapped to multiple lines, and deterministic accent color from game id.
- Store under `ProgramData/BeaconStream/artwork/fallback` by default.

- [ ] **Step 4: Verify**

Run: `dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter "ArtworkProviderTests|SteamGridDbArtworkProviderTests"`

Expected: `Passed`.

- [ ] **Step 5: Commit**

```bash
git add src/Beacon.Core/Games/Artwork src/Beacon.Core/Games/SteamGridDb tests/Beacon.Core.Tests/Games/Artwork tests/Beacon.Core.Tests/Games/SteamGridDb
git commit -m "Add SteamGridDB artwork and fallback covers"
```

## Task 8: Aggregate, Dedupe, And Legacy Mapping Filter

**Files:**
- Create: `src/Beacon.Core/Games/GameLibraryService.cs`
- Create: `src/Beacon.Core/Games/Manual/ManualGameLibraryProvider.cs`
- Test: `tests/Beacon.Core.Tests/Games/GameLibraryServiceTests.cs`
- Test: `tests/Beacon.Core.Tests/Games/Manual/ManualGameLibraryProviderTests.cs`

- [ ] **Step 1: Write failing aggregation tests**

```csharp
using Beacon.Core.Games;

namespace Beacon.Core.Tests.Games;

public sealed class GameLibraryServiceTests
{
    [Fact]
    public async Task DedupesLegacySteamMappingsWhenAutomaticDiscoveryCoversThem()
    {
        var steam = new FakeProvider("steam", new GameDescriptor(
            "steam:1086940",
            "Baldur's Gate 3",
            "steam",
            new GameLaunchIntent("steam-app", "steam://run/1086940"),
            new GameArtwork(null, "none"),
            true,
            new GameProcessHints(null, null)));

        var manual = new FakeProvider("manual", new GameDescriptor(
            "legacy-sunshine:BG3",
            "Baldur's Gate 3",
            "legacy-sunshine",
            new GameLaunchIntent("steam-app", "steam://run/1086940"),
            new GameArtwork(null, "none"),
            true,
            new GameProcessHints(null, null)));

        var service = new GameLibraryService([manual, steam], new NullArtworkProvider());

        GameLibrarySnapshot snapshot = await service.ScanAsync(CancellationToken.None);

        GameDescriptor game = Assert.Single(snapshot.Games);
        Assert.Equal("steam:1086940", game.Id);
    }
}
```

- [ ] **Step 2: Verify red**

Run: `dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter GameLibraryServiceTests`

Expected: compile failure because service does not exist.

- [ ] **Step 3: Implement service**

Rules:

- Provider order: Steam, Heroic, Hydra, manual, legacy.
- Dedupe by normalized launch command first, then by source/id.
- Prefer automatic providers over manual/legacy when launch command matches.
- Enrich only games whose artwork source is `none`.
- If SteamGridDB returns no artwork, call generated fallback provider.
- Preserve diagnostics from every provider.

- [ ] **Step 4: Verify**

Run: `dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter GameLibraryServiceTests`

Expected: `Passed`.

- [ ] **Step 5: Commit**

```bash
git add src/Beacon.Core/Games tests/Beacon.Core.Tests/Games
git commit -m "Aggregate and dedupe game libraries"
```

## Task 9: Server API And Client Lab Game Selection

**Files:**
- Modify: `src/Beacon.Server/Api/ClientEndpoints.cs`
- Modify: `src/Beacon.Server/Program.cs`
- Modify: `tests/Beacon.Server.Tests/ClientApiTests.cs`
- Modify: `src/Beacon.ClientLab/src/main.ts`
- Modify: `src/Beacon.ClientLab/src/clientLab.test.ts`
- Modify: `tests/Beacon.ClientLab.Playwright/tests/client-lab.spec.ts`

- [x] **Step 1: Write failing API tests**

```csharp
[Fact]
public async Task GameLibraryEndpointReturnsNormalizedGames()
{
    await using BeaconServerFixture fixture = await BeaconServerFixture.StartAsync();

    HttpResponseMessage response = await fixture.Client.GetAsync("/games");

    response.EnsureSuccessStatusCode();
    string body = await response.Content.ReadAsStringAsync();
    using JsonDocument document = JsonDocument.Parse(body);
    Assert.True(document.RootElement.GetProperty("games").GetArrayLength() > 0);
    Assert.True(document.RootElement.GetProperty("games")[0].TryGetProperty("launch", out _));
}
```

- [x] **Step 2: Verify red**

Run: `dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter GameLibraryEndpointReturnsNormalizedGames`

Expected: endpoint missing or compile failure.

- [x] **Step 3: Add endpoint and Client Lab wiring**

Server behavior:

- `GET /games` returns `{ games, diagnostics }`.
- `POST /clients/{clientId}/plan` accepts either explicit app fields or a normalized `gameId`; when `gameId` is present, resolve from library.
- `POST /clients/{clientId}/launch` follows the same resolution.

Client Lab behavior:

- Fetch `/games` during simulated hello.
- Show title, source, installed state, and cover.
- Plan and launch selected `gameId`.

- [x] **Step 4: Verify API and web tests**

Run:

```bash
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj
pnpm --dir src/Beacon.ClientLab lint
pnpm --dir src/Beacon.ClientLab test
pnpm --dir tests/Beacon.ClientLab.Playwright lint
pnpm --dir tests/Beacon.ClientLab.Playwright test
```

Expected: all pass.

- [x] **Step 5: Commit**

```bash
git add src/Beacon.Server tests/Beacon.Server.Tests src/Beacon.ClientLab tests/Beacon.ClientLab.Playwright
git commit -m "Expose normalized game library to clients"
```

## Task 10: Local Game Probe

**Files:**
- Create: `src/Beacon.GameProbe/Beacon.GameProbe.csproj`
- Create: `src/Beacon.GameProbe/Program.cs`
- Modify: `Beacon.slnx`
- Test: `tests/Beacon.GameProbe.Tests/Beacon.GameProbe.Tests.csproj`
- Test: `tests/Beacon.GameProbe.Tests/GameProbeCommandLineTests.cs`

- [x] **Step 1: Write failing CLI parser test**

```csharp
namespace Beacon.GameProbe.Tests;

public sealed class GameProbeCommandLineTests
{
    [Fact]
    public void ParsesScanCommand()
    {
        GameProbeOptions options = GameProbeOptions.Parse(["scan", "--json"]);

        Assert.Equal("scan", options.Command);
        Assert.True(options.Json);
    }
}
```

- [x] **Step 2: Verify red**

Run: `dotnet test tests/Beacon.GameProbe.Tests/Beacon.GameProbe.Tests.csproj`

Expected: project or types missing.

- [x] **Step 3: Implement probe**

Commands:

- `scan`: scan local providers and print table.
- `scan --json`: print full JSON with diagnostics.
- `steam-shortcuts <path>`: parse one `shortcuts.vdf` and print title plus `steam://rungameid`.

- [x] **Step 4: Verify with local read-only scan**

Run:

```bash
dotnet test tests/Beacon.GameProbe.Tests/Beacon.GameProbe.Tests.csproj
dotnet run --project src/Beacon.GameProbe -- scan --json
```

Expected: tests pass; local scan does not mutate Steam/Heroic/Hydra files and prints diagnostics.

- [x] **Step 5: Commit**

```bash
git add Beacon.slnx src/Beacon.GameProbe tests/Beacon.GameProbe.Tests
git commit -m "Add read-only game library probe"
```

## Task 11: Docs, Validation, And Sync

**Files:**
- Modify: `README.md`
- Modify: `docs/extraction-map.md`
- Modify: `docs/superpowers/specs/2026-06-03-personal-streaming-orchestrator-design.md` only if needed for clarified provider behavior.

- [x] **Step 1: Update docs**

Document:

- Game library providers.
- Steam shortcut stored-`appid` behavior.
- SteamGridDB configuration via environment/config.
- Generated fallback cover behavior.
- `Beacon.GameProbe` commands.
- No per-game display policy in Milestone 3.

- [x] **Step 2: Run static validation**

Run:

```bash
dotnet format Beacon.slnx --verify-no-changes
dotnet build Beacon.slnx -warnaserror
pnpm --dir src/Beacon.ClientLab lint
pnpm --dir tests/Beacon.ClientLab.Playwright lint
```

Expected: exit code 0 for every command.

- [x] **Step 3: Run dynamic validation**

Run:

```bash
dotnet test Beacon.slnx
pnpm --dir src/Beacon.ClientLab test
pnpm --dir tests/Beacon.ClientLab.Playwright test
dotnet run --project src/Beacon.GameProbe -- scan --json
```

Expected: tests pass and probe prints normalized Steam entries plus provider diagnostics for empty/missing providers.

- [x] **Step 4: Sync**

```bash
git status --short
git add README.md docs src tests Beacon.slnx
git commit -m "Complete normalized game collection"
git push origin codex/milestone-3-game-collection
gh pr create --draft --base main --head codex/milestone-3-game-collection --title "Implement normalized game collection" --body "Milestone 3 game collection implementation."
```

Expected: draft PR exists and CI starts.

## Task 12: Refactor And Second Validation Loop

**Files:**
- Same files touched above.

- [x] **Step 1: Review for boundaries**

Checklist:

- Providers do not know display policy.
- Server does not parse Steam/Heroic/Hydra files directly.
- SteamGridDB provider does not expose API key in logs/errors.
- Generated covers are deterministic and do not require external services.
- Provider diagnostics are actionable.

- [x] **Step 2: Refactor only if the checklist finds concrete issues**

Allowed refactors:

- Move shared path sanitization to a small helper.
- Split oversized provider methods.
- Extract fixture builders from tests.
- Tighten diagnostics wording.

- [x] **Step 3: Repeat validation**

Run:

```bash
dotnet format Beacon.slnx --verify-no-changes
dotnet build Beacon.slnx -warnaserror
dotnet test Beacon.slnx
pnpm --dir src/Beacon.ClientLab lint
pnpm --dir src/Beacon.ClientLab test
pnpm --dir tests/Beacon.ClientLab.Playwright lint
pnpm --dir tests/Beacon.ClientLab.Playwright test
dotnet run --project src/Beacon.GameProbe -- scan --json
```

Expected: exit code 0 for all validation commands.

- [x] **Step 4: Final sync for Milestone 3**

```bash
git status --short
git add README.md docs src tests Beacon.slnx
git commit -m "Harden game collection providers"
git push origin codex/milestone-3-game-collection
gh pr checks --watch
```

Expected: PR checks pass. Mark the PR ready only after CI is green.

## Requirement Coverage

- `REQ-GAME-001`: Game library is a first-class service and endpoint.
- `REQ-GAME-002`: Steam, Steam shortcuts, Heroic, Hydra, and manual providers are included.
- `REQ-GAME-003`: Steam shortcut launch uses stored appid to build the correct 64-bit `rungameid`.
- `REQ-GAME-004`: External games injected into Steam launch through normalized Steam shortcut entries.
- `REQ-GAME-005`: Model includes launch identity, source, cover, installed state, and process hints.
- `REQ-GAME-006`: No display topology policy is added to game profiles.
- `REQ-GAME-007`: SteamGridDB provider is included.
- `REQ-GAME-008`: Exact-name candidate handling chooses the first candidate with usable artwork.
- `REQ-GAME-009`: Generated SVG fallback covers are included.
- `REQ-GAME-010`: Dedupe removes legacy manual Steam mappings when automatic discovery covers the launch command.

## Plan Self-Review

- Placeholder scan: checked for banned placeholder phrases and found none.
- Type consistency: all model names are defined before use.
- Scope check: this is one milestone with one product slice; WPF cockpit, streaming backend, and APK remain later milestones.
- Testing: every production code task starts with a failing test and red/green verification.
