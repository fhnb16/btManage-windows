using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using BtManage.Interop;

namespace BtManage.Updates;

/// <summary>A release newer than the running version, as the core describes it.</summary>
internal sealed record UpdateInfo(string Version, string PageUrl, UpdateAsset? Asset);

internal sealed record UpdateAsset(string Name, string Url, long Size, string? Sha256);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(UpdateInfo))]
internal sealed partial class UpdateJson : JsonSerializerContext;

internal enum UpdateState
{
    Idle,
    Checking,
    UpToDate,
    Available,
    Downloading,
    CheckFailed,
    InstallFailed,
}

/// <summary>
/// Looks for a newer release among the GitHub releases of this application's repository,
/// and installs it: downloads the archive for this processor, checks it against the SHA-256
/// GitHub reports, unpacks it, and has a small script copy it over the application once
/// the application has exited.
///
/// The request to GitHub carries nothing but a fixed client name.
/// </summary>
internal sealed class Updater
{
    private const string Executable = "BtManage.exe";
    private static readonly HttpClient Http = CreateClient();

    /// <summary>"owner/repository", set at build time (the UpdateRepository property).</summary>
    public static string Repository { get; } = typeof(Updater).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == "UpdateRepository")?.Value ?? string.Empty;

    public static string CurrentVersion { get; } =
        typeof(Updater).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Published as one self-extracting executable rather than a folder of files.</summary>
    private static bool IsSingleFile => string.IsNullOrEmpty(typeof(Updater).Assembly.Location);

    private static string ExecutablePath =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, Executable);

    /// <summary>
    /// Part of the name of the release file that matches how this copy was installed: the
    /// single executable for a single executable, the archive for a folder.
    /// </summary>
    private static string AssetHint =>
        (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "windows-arm64" : "windows-x64")
        + (IsSingleFile ? "-portable.exe" : ".zip");

    private static string WorkDirectory => Path.Combine(AppPaths.DataDirectory, "update");

    public UpdateState State { get; private set; } = UpdateState.Idle;

    /// <summary>The newer release, once a check has found one.</summary>
    public UpdateInfo? Available { get; private set; }

    /// <summary>Download progress from 0 to 100 while <see cref="State"/> is Downloading.</summary>
    public int Progress { get; private set; }

    /// <summary>Raised on the thread that called, after every change of state or progress.</summary>
    public event Action? Changed;

    /// <summary>Asks GitHub for the latest release. Returns the newer release, if there is one.</summary>
    public async Task<UpdateInfo?> CheckAsync()
    {
        if (Repository.Length == 0 || State is UpdateState.Checking or UpdateState.Downloading)
        {
            return Available;
        }

        Set(UpdateState.Checking);
        try
        {
            var json = await Http.GetStringAsync($"https://api.github.com/repos/{Repository}/releases/latest");
            Available = Hub.UpdateCheck(CurrentVersion, json, AssetHint) is { } found
                ? JsonSerializer.Deserialize(found, UpdateJson.Default.UpdateInfo)
                : null;
            Set(Available is null ? UpdateState.UpToDate : UpdateState.Available);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            // No network, no releases yet, or the rate limit: nothing to report but the failure.
            Set(UpdateState.CheckFailed);
        }

        return Available;
    }

    /// <summary>
    /// Downloads and stages the release found by <see cref="CheckAsync"/>, then starts the
    /// script that swaps the files. On success the caller must quit the application at once.
    /// </summary>
    public async Task<bool> InstallAsync()
    {
        if (Available?.Asset is not { Sha256: { } expected } asset || State == UpdateState.Downloading)
        {
            // Without a file or its checksum there is nothing safe to install.
            Set(UpdateState.InstallFailed);
            return false;
        }

        Progress = 0;
        Set(UpdateState.Downloading);
        try
        {
            Directory.CreateDirectory(WorkDirectory);
            var archive = Path.Combine(WorkDirectory, "update" + Path.GetExtension(asset.Name));
            await DownloadAsync(asset, archive);

            await using (var file = File.OpenRead(archive))
            {
                var actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(file));
                if (actual != expected)
                {
                    throw new InvalidDataException("The download does not match the published checksum");
                }
            }

            if (InstallFrom(archive))
            {
                return true;
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException
            or InvalidDataException or UnauthorizedAccessException or TaskCanceledException)
        {
            // Reported below.
        }

        Set(UpdateState.InstallFailed);
        return false;
    }

    /// <summary>
    /// Stages a downloaded release (an archive of the application folder, or the single
    /// executable) and starts the script that copies it over the running application after
    /// it exits. Returns false when the file is not the application or its folder cannot
    /// be written to.
    /// </summary>
    public static bool InstallFrom(string download)
    {
        var executable = ExecutablePath;
        var target = Path.GetDirectoryName(executable)!;
        var staged = Path.Combine(WorkDirectory, "staged");
        if (Directory.Exists(staged))
        {
            Directory.Delete(staged, recursive: true);
        }

        var name = Path.GetFileName(executable);
        if (Path.GetExtension(download).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(staged);
            File.Copy(download, Path.Combine(staged, name));
        }
        else
        {
            ZipFile.ExtractToDirectory(download, staged);
        }

        if (!File.Exists(Path.Combine(staged, name)) || !CanWrite(target))
        {
            return false;
        }

        // The script waits for this process, copies the new files and starts the application.
        // Paths travel in the environment, so spaces and quotes in them need no escaping.
        var script = Path.Combine(WorkDirectory, "swap.cmd");
        File.WriteAllText(script, """
            @echo off
            :wait
            tasklist /FI "PID eq %BTM_PID%" 2>nul | find "%BTM_PID%" >nul && (timeout /t 1 /nobreak >nul & goto wait)
            robocopy "%BTM_STAGED%" "%BTM_TARGET%" /E /R:5 /W:1 /NFL /NDL /NJH /NJS /NP >nul
            if %ERRORLEVEL% GEQ 8 exit /b 1
            start "" "%BTM_EXE%" --restarted
            """);

        var start = new ProcessStartInfo("cmd.exe", "/d /c call \"%BTM_SCRIPT%\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.Environment["BTM_SCRIPT"] = script;
        start.Environment["BTM_PID"] = Environment.ProcessId.ToString();
        start.Environment["BTM_STAGED"] = staged;
        start.Environment["BTM_TARGET"] = target;
        start.Environment["BTM_EXE"] = executable;
        Process.Start(start);
        return true;
    }

    /// <summary>Removes what an earlier update left behind.</summary>
    public static void CleanUp()
    {
        try
        {
            if (Directory.Exists(WorkDirectory))
            {
                Directory.Delete(WorkDirectory, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Still in use by the script that started this process; next time.
        }
    }
    private async Task DownloadAsync(UpdateAsset asset, string path)
    {
        using var response = await Http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? asset.Size;

        await using var source = await response.Content.ReadAsStreamAsync();
        await using var file = File.Create(path);
        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read));
            done += read;
            var percent = total > 0 ? (int)(done * 100 / total) : 0;
            if (percent != Progress)
            {
                Progress = percent;
                Changed?.Invoke();
            }
        }
    }

    private static bool CanWrite(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, $".write-test-{Environment.ProcessId}");
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void Set(UpdateState state)
    {
        State = state;
        Changed?.Invoke();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("btManage", CurrentVersion));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}
