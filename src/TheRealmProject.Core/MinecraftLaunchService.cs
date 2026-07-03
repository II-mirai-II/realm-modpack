using CmlLib.Core;
using CmlLib.Core.Auth;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace TheRealmProject.Core;

public sealed class MinecraftLaunchService(
    RealmPaths paths,
    ProfileService profileService,
    LauncherStateService stateService)
{
    private static readonly ConcurrentDictionary<int, Process> RunningProcesses = new();

    public async Task LaunchAsync(
        string versionId,
        string javaPath,
        IProgress<string>? progress = null,
        Action<int>? exited = null,
        CancellationToken cancellationToken = default)
    {
        paths.Ensure();
        var profile = await profileService.LoadAsync(cancellationToken);
        var config = await stateService.LoadConfigAsync(cancellationToken);

        var launcher = new CMLauncher(new MinecraftPath(paths.Minecraft));
        launcher.FileChanged += e => progress?.Report($"{e.FileKind}: {e.FileName} ({e.ProgressedFileCount}/{e.TotalFileCount})");
        launcher.ProgressChanged += (_, e) => progress?.Report($"Download: {e.ProgressPercentage}%");

        var launchOption = new MLaunchOption
        {
            MaximumRamMb = config.MaximumRamMb,
            MinimumRamMb = config.MinimumRamMb,
            Session = MSession.GetOfflineSession(profile.PlayerId),
            JavaPath = javaPath
        };

        progress?.Report($"Abrindo Minecraft {versionId} como {profile.PlayerId}...");
        var process = await launcher.CreateProcessAsync(versionId, launchOption);
        process.StartInfo.Arguments = PrepareLaunchArguments(process.StartInfo.Arguments, profile);
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.EnableRaisingEvents = true;
        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                progress?.Report(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                progress?.Report(e.Data);
        };

        process.Exited += (_, _) =>
        {
            var exitCode = process.ExitCode;
            var processId = process.Id;
            progress?.Report($"Minecraft finalizou com código {exitCode}.");
            RunningProcesses.TryRemove(processId, out _);
            exited?.Invoke(exitCode);
            process.Dispose();
        };
        process.Start();
        RunningProcesses[process.Id] = process;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        progress?.Report("Minecraft iniciado. O botão Jogar ficará bloqueado até o jogo fechar.");
    }

    private static string PrepareLaunchArguments(string arguments, RealmProfile profile)
    {
        var prepared = DeduplicateClasspath(arguments);
        prepared = ReplaceArgumentValue(prepared, "--uuid", profile.Uuid);
        prepared = ReplaceArgumentValue(prepared, "--accessToken", "0");
        prepared = ReplaceArgumentValue(prepared, "--clientId", "0");
        prepared = ReplaceArgumentValue(prepared, "--xuid", "0");
        return prepared;
    }

    private static string ReplaceArgumentValue(string arguments, string argumentName, string value)
    {
        var escaped = Regex.Escape(argumentName);
        return Regex.Replace(arguments, $@"(?<name>{escaped})\s+(?:""[^""]*""|\S+)", match => $"{match.Groups["name"].Value} {value}");
    }

    private static string DeduplicateClasspath(string arguments)
    {
        var match = Regex.Match(arguments, @"(?<prefix>(?:^|\s)-cp\s+)(?<classpath>(?:""[^""]+"";?)+)");
        if (!match.Success)
            return arguments;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deduped = match.Groups["classpath"].Value
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => path.Trim().Trim('"'))
            .Where(path => seen.Add(path))
            .ToArray();

        var builder = new StringBuilder(arguments);
        builder.Remove(match.Groups["classpath"].Index, match.Groups["classpath"].Length);
        builder.Insert(match.Groups["classpath"].Index, $"\"{string.Join(Path.PathSeparator, deduped)}\"");
        return builder.ToString();
    }
}
