using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CommitDock.Models
{
    public enum UpdatePackageKind
    {
        Unsupported = 0,
        WindowsInstaller,
        WindowsPortable,
        MacOSApp,
        LinuxAppImage,
        LinuxDeb,
        LinuxRpm,
    }

    /// <summary>
    ///     Fetches releases from GitHub and replaces the running copy of Commit Dock with a downloaded one.
    /// </summary>
    public static class SelfUpdater
    {
        public const string ReleasesUrl = "https://github.com/mzaeemnasir/Commit-Dock/releases/latest";

        /// <summary>
        ///     How the running copy was installed. It decides which release file fits and how it gets swapped in.
        /// </summary>
        public static UpdatePackageKind PackageKind { get; } = DetectPackageKind();

        public static string CurrentVersionString { get; } = GetCurrentVersionString();

        public static bool CanInstallAutomatically(UpdatePackageKind kind)
        {
            return kind is UpdatePackageKind.WindowsInstaller
                or UpdatePackageKind.WindowsPortable
                or UpdatePackageKind.MacOSApp
                or UpdatePackageKind.LinuxAppImage;
        }

        public static bool NeedsSystemInstaller(UpdatePackageKind kind)
        {
            return kind is UpdatePackageKind.LinuxDeb or UpdatePackageKind.LinuxRpm;
        }

        public static async Task<Version> FetchLatestReleaseAsync(CancellationToken token = default)
        {
            using var client = CreateHttpClient(TimeSpan.FromSeconds(10));
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            var data = await client.GetStringAsync(LATEST_RELEASE_API, token).ConfigureAwait(false);
            return JsonSerializer.Deserialize(data, JsonCodeGen.Default.Version);
        }

        public static string GetAssetName(UpdatePackageKind kind, string versionNumber)
        {
            bool arm64;
            switch (RuntimeInformation.OSArchitecture)
            {
                case Architecture.X64:
                    arm64 = false;
                    break;
                case Architecture.Arm64:
                    arm64 = true;
                    break;
                default:
                    return null;
            }

            return kind switch
            {
                UpdatePackageKind.WindowsInstaller => $"CommitDock-v{versionNumber}-setup-{(arm64 ? "arm64" : "x64")}.exe",
                UpdatePackageKind.WindowsPortable => $"CommitDock-v{versionNumber}-win-{(arm64 ? "arm64" : "x64")}.zip",
                UpdatePackageKind.MacOSApp => $"CommitDock-v{versionNumber}-osx-{(arm64 ? "arm64" : "x64")}.zip",
                UpdatePackageKind.LinuxAppImage => $"CommitDock-v{versionNumber}-linux-{(arm64 ? "arm64" : "amd64")}.AppImage",
                UpdatePackageKind.LinuxDeb => $"CommitDock-v{versionNumber}-linux-{(arm64 ? "arm64" : "amd64")}.deb",
                UpdatePackageKind.LinuxRpm => $"CommitDock-v{versionNumber}-linux-{(arm64 ? "aarch64" : "x86_64")}.rpm",
                _ => null,
            };
        }

        public static ReleaseAsset FindAsset(Version version, UpdatePackageKind kind)
        {
            if (version == null)
                return null;

            var name = GetAssetName(kind, version.VersionNumber);
            return string.IsNullOrEmpty(name) ? null : version.FindAsset(name);
        }

        /// <summary>
        ///     Downloads a release file into the updates cache and returns its path. <paramref name="onProgress"/>
        ///     is called from a background thread with the bytes received and the total size (0 when unknown).
        /// </summary>
        public static async Task<string> DownloadAsync(ReleaseAsset asset, Action<long, long> onProgress, CancellationToken token)
        {
            var dir = GetDownloadDirectory();
            Directory.CreateDirectory(dir);

            var file = Path.Combine(dir, asset.Name);
            var partial = file + ".part";

            try
            {
                using var client = CreateHttpClient(Timeout.InfiniteTimeSpan);
                using var response = await client.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var total = response.Content.Headers.ContentLength ?? asset.Size;
                var received = 0L;
                var buffer = new byte[1 << 16];

                await using (var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
                await using (var target = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length, true))
                {
                    int read;
                    while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                    {
                        await target.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                        received += read;
                        onProgress?.Invoke(received, total);
                    }
                }

                if (asset.Size > 0 && new FileInfo(partial).Length != asset.Size)
                    throw new IOException("The downloaded file is incomplete. Please try again.");

                File.Move(partial, file, true);
                return file;
            }
            catch
            {
                TryDeleteFile(partial);
                throw;
            }
        }

        /// <summary>
        ///     Installs a downloaded release file. For packages Commit Dock can install itself this ends with the
        ///     new version (or the installer, which relaunches Commit Dock) being started, and the caller is
        ///     expected to quit right after. Packages that need the package manager of the system are only opened.
        /// </summary>
        public static Task InstallAsync(UpdatePackageKind kind, string file)
        {
            return Task.Run(() =>
            {
                WritePostUpdateMarker();

                try
                {
                    switch (kind)
                    {
                        case UpdatePackageKind.WindowsInstaller:
                            InstallWindowsInstaller(file);
                            break;
                        case UpdatePackageKind.WindowsPortable:
                            InstallWindowsPortable(file);
                            break;
                        case UpdatePackageKind.MacOSApp:
                            if (OperatingSystem.IsMacOS())
                                InstallMacOSApp(file);
                            break;
                        case UpdatePackageKind.LinuxAppImage:
                            if (OperatingSystem.IsLinux())
                                InstallLinuxAppImage(file);
                            break;
                        case UpdatePackageKind.LinuxDeb:
                        case UpdatePackageKind.LinuxRpm:
                            OpenWithSystemInstaller(file);
                            break;
                        default:
                            throw new NotSupportedException("This copy of Commit Dock cannot update itself. Please download the new version from the release page.");
                    }
                }
                catch
                {
                    TryDeleteFile(GetMarkerFile());
                    throw;
                }
            });
        }

        /// <summary>
        ///     Must be called first thing at startup. When Commit Dock was relaunched by an update it waits for the
        ///     previous instance to exit (so the single-instance lock is free) and strips the relaunch arguments.
        /// </summary>
        public static string[] HandleRelaunchArgs(string[] args)
        {
            if (args.Length < 2 || !args[0].Equals(RELAUNCH_ARG, StringComparison.Ordinal))
                return args;

            if (int.TryParse(args[1], out var pid))
            {
                try
                {
                    using var previous = Process.GetProcessById(pid);
                    previous.WaitForExit(15000);
                }
                catch
                {
                    // The previous instance is already gone.
                }
            }

            return args[2..];
        }

        /// <summary>
        ///     Removes files left behind by a previous update (renamed binaries, downloads, staging folders).
        /// </summary>
        public static void CleanupAfterUpdate()
        {
            try
            {
                var exe = Environment.ProcessPath;
                var dir = string.IsNullOrEmpty(exe) ? null : Path.GetDirectoryName(exe);

                if (OperatingSystem.IsWindows() && !string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    foreach (var old in Directory.EnumerateFiles(dir, "*.old", SearchOption.AllDirectories))
                        TryDeleteFile(old);
                }
                else if (OperatingSystem.IsMacOS() && GetMacOSBundlePath() is { } bundle)
                {
                    TryDeleteDirectory(bundle + ".old");
                }
                else if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } appImage)
                {
                    TryDeleteFile(appImage + ".old");
                }

                TryDeleteDirectory(GetDownloadDirectory());
            }
            catch
            {
                // Best effort only.
            }
        }

        /// <summary>
        ///     Returns the version that was running when an in-app update was started, on the first launch after
        ///     it, and forgets it. Returns null otherwise.
        /// </summary>
        public static string ConsumePostUpdateMarker()
        {
            var marker = GetMarkerFile();

            try
            {
                if (!File.Exists(marker))
                    return null;

                var previous = File.ReadAllText(marker).Trim();
                File.Delete(marker);
                return previous;
            }
            catch
            {
                return null;
            }
        }

        public static string GetDownloadDirectory()
        {
            return Path.Combine(Native.OS.BasicDirectories.CacheDir, "updates");
        }

        private static void InstallWindowsInstaller(string file)
        {
            // Setup closes the running copy (Commit Dock quits right after this anyway), installs over it and,
            // because of /UPDATE=1, starts the new version when it is done. See build/windows/CommitDock.iss.
            var start = new ProcessStartInfo
            {
                FileName = file,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOCANCEL /SP- /CLOSEAPPLICATIONS /UPDATE=1",
                WorkingDirectory = Path.GetDirectoryName(file)!,
                UseShellExecute = true,
            };

            Process.Start(start);
        }

        private static void InstallWindowsPortable(string file)
        {
            var exe = Environment.ProcessPath!;
            var exeName = Path.GetFileName(exe);
            var appDir = Path.GetDirectoryName(exe)!;
            var stage = PrepareStageDirectory();

            ZipFile.ExtractToDirectory(file, stage, true);

            var source = FindDirectoryContaining(stage, exeName)
                ?? throw new FileNotFoundException($"{exeName} was not found in the downloaded package.");

            // A running executable can be renamed but not overwritten on Windows, so every file that is still in
            // use is moved aside and removed on the next start (see CleanupAfterUpdate).
            foreach (var path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var dest = Path.Combine(appDir, Path.GetRelativePath(source, path));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

                if (File.Exists(dest))
                    MoveAside(dest);

                File.Move(path, dest, true);
            }

            TryDeleteDirectory(stage);
            Relaunch(exe, appDir);
        }

        [SupportedOSPlatform("macos")]
        private static void InstallMacOSApp(string file)
        {
            var bundle = GetMacOSBundlePath()
                ?? throw new NotSupportedException("Commit Dock is not running from an application bundle.");
            var stage = PrepareStageDirectory();

            // ditto keeps permissions, symlinks and the code signature, which ZipFile would drop.
            RunTool("/usr/bin/ditto", "-x", "-k", file, stage);

            var newBundle = Directory.EnumerateDirectories(stage, "*.app").FirstOrDefault()
                ?? throw new FileNotFoundException("No application bundle was found in the downloaded package.");

            TryRunTool("/usr/bin/xattr", "-dr", "com.apple.quarantine", newBundle);

            var backup = bundle + ".old";
            TryDeleteDirectory(backup);
            Directory.Move(bundle, backup);

            try
            {
                MoveDirectory(newBundle, bundle);
            }
            catch
            {
                Directory.Move(backup, bundle);
                throw;
            }

            TryDeleteDirectory(backup);
            TryDeleteDirectory(stage);

            var start = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
            start.ArgumentList.Add("-n");
            start.ArgumentList.Add(bundle);
            start.ArgumentList.Add("--args");
            start.ArgumentList.Add(RELAUNCH_ARG);
            start.ArgumentList.Add(Environment.ProcessId.ToString());
            Process.Start(start);
        }

        [SupportedOSPlatform("linux")]
        private static void InstallLinuxAppImage(string file)
        {
            var current = Environment.GetEnvironmentVariable("APPIMAGE");
            if (string.IsNullOrEmpty(current) || !File.Exists(current))
                throw new NotSupportedException("The running AppImage could not be located.");

            // The file name and location chosen by the user are kept, so launchers and desktop entries keep working.
            var fresh = current + ".new";
            var backup = current + ".old";

            File.Move(file, fresh, true);
            File.SetUnixFileMode(fresh,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

            TryDeleteFile(backup);
            File.Move(current, backup, true);

            try
            {
                File.Move(fresh, current, true);
            }
            catch
            {
                File.Move(backup, current, true);
                throw;
            }

            TryDeleteFile(backup);
            Relaunch(current, Path.GetDirectoryName(current)!);
        }

        private static void OpenWithSystemInstaller(string file)
        {
            var start = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
            start.ArgumentList.Add(file);
            Process.Start(start);
        }

        private static void Relaunch(string executable, string workingDirectory)
        {
            var start = new ProcessStartInfo(executable)
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
            };

            start.ArgumentList.Add(RELAUNCH_ARG);
            start.ArgumentList.Add(Environment.ProcessId.ToString());
            Process.Start(start);
        }

        private static UpdatePackageKind DetectPackageKind()
        {
            try
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe))
                    return UpdatePackageKind.Unsupported;

                var dir = Path.GetDirectoryName(exe);
                if (string.IsNullOrEmpty(dir))
                    return UpdatePackageKind.Unsupported;

                // Development builds (dotnet run/build) and framework-dependent publishes ship the managed assembly
                // next to the host. Release packages are native AOT builds without it.
                if (File.Exists(Path.Combine(dir, "CommitDock.dll")))
                    return UpdatePackageKind.Unsupported;

                if (OperatingSystem.IsWindows())
                    return Directory.EnumerateFiles(dir, "unins*.exe").Any() ? UpdatePackageKind.WindowsInstaller : UpdatePackageKind.WindowsPortable;

                if (OperatingSystem.IsMacOS())
                    return GetMacOSBundlePath() != null ? UpdatePackageKind.MacOSApp : UpdatePackageKind.Unsupported;

                if (OperatingSystem.IsLinux())
                {
                    var appImage = Environment.GetEnvironmentVariable("APPIMAGE");
                    if (!string.IsNullOrEmpty(appImage) && File.Exists(appImage))
                        return UpdatePackageKind.LinuxAppImage;

                    if (dir.StartsWith("/opt/commitdock", StringComparison.Ordinal))
                    {
                        if (File.Exists("/var/lib/dpkg/info/commitdock.list"))
                            return UpdatePackageKind.LinuxDeb;
                        if (Directory.Exists("/var/lib/rpm"))
                            return UpdatePackageKind.LinuxRpm;
                    }
                }
            }
            catch
            {
                // Fall through.
            }

            return UpdatePackageKind.Unsupported;
        }

        private static string GetMacOSBundlePath()
        {
            // <Name>.app/Contents/MacOS/CommitDock
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
                return null;

            var macos = Path.GetDirectoryName(exe);
            var contents = string.IsNullOrEmpty(macos) ? null : Path.GetDirectoryName(macos);
            var bundle = string.IsNullOrEmpty(contents) ? null : Path.GetDirectoryName(contents);

            if (string.IsNullOrEmpty(bundle) ||
                !Path.GetFileName(macos).Equals("MacOS", StringComparison.Ordinal) ||
                !Path.GetFileName(contents).Equals("Contents", StringComparison.Ordinal) ||
                !bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                return null;

            return bundle;
        }

        private static string GetCurrentVersionString()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version ?? new System.Version(0, 0, 0);
            return $"v{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
        }

        private static string GetMarkerFile()
        {
            return Path.Combine(Native.OS.BasicDirectories.ConfigDir, "update.pending");
        }

        private static void WritePostUpdateMarker()
        {
            try
            {
                File.WriteAllText(GetMarkerFile(), CurrentVersionString);
            }
            catch
            {
                // Only used for the "updated" notice, so it is fine to skip.
            }
        }

        private static HttpClient CreateHttpClient(TimeSpan timeout)
        {
            var client = new HttpClient { Timeout = timeout };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CommitDock");
            return client;
        }

        private static string PrepareStageDirectory()
        {
            var stage = Path.Combine(GetDownloadDirectory(), "stage");
            TryDeleteDirectory(stage);
            Directory.CreateDirectory(stage);
            return stage;
        }

        private static string FindDirectoryContaining(string root, string fileName)
        {
            if (File.Exists(Path.Combine(root, fileName)))
                return root;

            foreach (var sub in Directory.EnumerateDirectories(root))
            {
                var found = FindDirectoryContaining(sub, fileName);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static void MoveAside(string file)
        {
            try
            {
                File.Delete(file);
                return;
            }
            catch (IOException)
            {
                // In use, rename it instead.
            }
            catch (UnauthorizedAccessException)
            {
                // In use, rename it instead.
            }

            var backup = file + ".old";
            try
            {
                if (File.Exists(backup))
                    File.Delete(backup);
            }
            catch
            {
                backup = $"{file}.{Environment.ProcessId}.old";
            }

            File.Move(file, backup, true);
        }

        private static void MoveDirectory(string source, string target)
        {
            try
            {
                Directory.Move(source, target);
            }
            catch (IOException)
            {
                // Different volumes: copy instead.
                RunTool("/usr/bin/ditto", source, target);
                Directory.Delete(source, true);
            }
        }

        private static void RunTool(string file, params string[] args)
        {
            var start = new ProcessStartInfo(file)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };

            foreach (var arg in args)
                start.ArgumentList.Add(arg);

            using var proc = Process.Start(start) ?? throw new IOException($"Failed to start {file}.");
            var error = proc.StandardError.ReadToEnd();
            proc.WaitForExit();

            if (proc.ExitCode != 0)
            {
                var reason = string.IsNullOrWhiteSpace(error) ? $"exit code {proc.ExitCode}" : error.Trim();
                throw new IOException($"{Path.GetFileName(file)} failed: {reason}");
            }
        }

        private static void TryRunTool(string file, params string[] args)
        {
            try
            {
                RunTool(file, args);
            }
            catch
            {
                // Optional step.
            }
        }

        private static void TryDeleteFile(string file)
        {
            try
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
            catch
            {
                // Still in use, try again next time.
            }
        }

        private static void TryDeleteDirectory(string dir)
        {
            try
            {
                if (!Directory.Exists(dir))
                    return;

                foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    TryDeleteFile(file);

                Directory.Delete(dir, true);
            }
            catch
            {
                // Something inside is still in use, try again next time.
            }
        }

        private const string LATEST_RELEASE_API = "https://api.github.com/repos/mzaeemnasir/Commit-Dock/releases/latest";
        private const string RELAUNCH_ARG = "--relaunch-after-update";
    }
}
