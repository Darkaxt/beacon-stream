using System.Diagnostics;
using Beacon.Core.Games;

namespace Beacon.Platform.Windows.Games;

public sealed record WindowsGameLaunchCommand(
    string FileName,
    string? Arguments,
    string? WorkingDirectory,
    bool UseShellExecute);

public sealed class WindowsGameLauncher : IGameLauncher
{
    public async Task<GameLaunchResult> LaunchAsync(GameLaunchRequest request, CancellationToken cancellationToken)
    {
        WindowsGameLaunchCommand command = CreateCommand(request);
        var startInfo = new ProcessStartInfo
        {
            FileName = command.FileName,
            Arguments = command.Arguments ?? string.Empty,
            UseShellExecute = command.UseShellExecute
        };

        if (!string.IsNullOrWhiteSpace(command.WorkingDirectory))
        {
            startInfo.WorkingDirectory = command.WorkingDirectory;
        }

        try
        {
            Process? process = Process.Start(startInfo);
            var state = new GameLaunchState(
                request.Plan.SessionId,
                request.Game.Id,
                request.Game.Launch.Type,
                request.Game.Launch.Command,
                SelectOwnedProcessId(command.UseShellExecute, process?.Id),
                request.DisplayId,
                Started: true);

            return await Task.FromResult(GameLaunchResult.Ok(state));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return await Task.FromResult(GameLaunchResult.Fail($"Failed to launch '{request.Game.Title}': {ex.Message}"));
        }
    }

    internal static int? SelectOwnedProcessId(bool useShellExecute, int? startedProcessId) =>
        useShellExecute ? null : startedProcessId;

    public static WindowsGameLaunchCommand CreateCommand(GameLaunchRequest request)
    {
        GameDescriptor game = request.Game;
        return game.Launch.Type switch
        {
            "process" => CreateProcessCommand(game),
            "steam-app" or "steam-rungameid" => new WindowsGameLaunchCommand(
                game.Launch.Command,
                Arguments: null,
                WorkingDirectory: null,
                UseShellExecute: true),
            _ => new WindowsGameLaunchCommand(
                game.Launch.Command,
                Arguments: null,
                WorkingDirectory: game.ProcessHints.WorkingDirectory,
                UseShellExecute: true)
        };
    }

    private static WindowsGameLaunchCommand CreateProcessCommand(GameDescriptor game)
    {
        string? workingDirectory = game.ProcessHints.WorkingDirectory;
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            workingDirectory = Path.GetDirectoryName(game.Launch.Command);
        }

        return new WindowsGameLaunchCommand(
            game.Launch.Command,
            Arguments: null,
            WorkingDirectory: workingDirectory,
            UseShellExecute: false);
    }
}
