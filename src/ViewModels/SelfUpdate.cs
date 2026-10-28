using System;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CommitDock.ViewModels
{
    public enum SelfUpdateStage
    {
        Checking,
        UpToDate,
        CheckFailed,
        Available,
        Downloading,
        Installing,
        Failed,
        HandedToSystem,
    }

    public class SelfUpdate : ObservableObject
    {
        public SelfUpdateStage Stage
        {
            get => _stage;
            private set
            {
                if (SetProperty(ref _stage, value))
                {
                    OnPropertyChanged(nameof(IsChecking));
                    OnPropertyChanged(nameof(IsUpToDate));
                    OnPropertyChanged(nameof(IsCheckFailed));
                    OnPropertyChanged(nameof(HasNewVersion));
                    OnPropertyChanged(nameof(IsAvailable));
                    OnPropertyChanged(nameof(IsDownloading));
                    OnPropertyChanged(nameof(IsInstalling));
                    OnPropertyChanged(nameof(IsFailed));
                    OnPropertyChanged(nameof(IsHandedToSystem));
                    OnPropertyChanged(nameof(IsBusy));
                }
            }
        }

        public Models.Version Version
        {
            get => _version;
            private set => SetProperty(ref _version, value);
        }

        public string CurrentVersion => Models.SelfUpdater.CurrentVersionString;

        public string ErrorMessage
        {
            get => _errorMessage;
            private set => SetProperty(ref _errorMessage, value);
        }

        public string PackageName
        {
            get => _packageName;
            private set => SetProperty(ref _packageName, value);
        }

        public double Progress
        {
            get => _progress;
            private set
            {
                if (SetProperty(ref _progress, value))
                    OnPropertyChanged(nameof(ProgressPercent));
            }
        }

        public string ProgressPercent => $"{Math.Clamp(_progress, 0, 100):0}%";

        public string ProgressText
        {
            get => _progressText;
            private set => SetProperty(ref _progressText, value);
        }

        public bool IsProgressUnknown
        {
            get => _isProgressUnknown;
            private set => SetProperty(ref _isProgressUnknown, value);
        }

        public string DownloadedFile
        {
            get => _downloadedFile;
            private set => SetProperty(ref _downloadedFile, value);
        }

        public bool CanInstall => _asset != null;
        public bool IsAutoInstall => _asset != null && Models.SelfUpdater.CanInstallAutomatically(_kind);
        public bool IsSystemInstall => _asset != null && Models.SelfUpdater.NeedsSystemInstaller(_kind);
        public bool IsManualDownload => _asset == null;

        public bool IsChecking => _stage == SelfUpdateStage.Checking;
        public bool IsUpToDate => _stage == SelfUpdateStage.UpToDate;
        public bool IsCheckFailed => _stage == SelfUpdateStage.CheckFailed;
        public bool HasNewVersion => _stage >= SelfUpdateStage.Available;
        public bool IsAvailable => _stage == SelfUpdateStage.Available;
        public bool IsDownloading => _stage == SelfUpdateStage.Downloading;
        public bool IsInstalling => _stage == SelfUpdateStage.Installing;
        public bool IsFailed => _stage == SelfUpdateStage.Failed;
        public bool IsHandedToSystem => _stage == SelfUpdateStage.HandedToSystem;
        public bool IsBusy => _stage is SelfUpdateStage.Downloading or SelfUpdateStage.Installing;

        /// <summary>
        ///     Starts in the "checking" state. Call <see cref="CheckAsync"/> to run the check.
        /// </summary>
        public SelfUpdate()
        {
            _stage = SelfUpdateStage.Checking;
        }

        /// <summary>
        ///     Starts with a release that is already known to be newer than the running version.
        /// </summary>
        public SelfUpdate(Models.Version version)
        {
            SetVersion(version);
        }

        public async Task CheckAsync()
        {
            Stage = SelfUpdateStage.Checking;

            try
            {
                var latest = await Models.SelfUpdater.FetchLatestReleaseAsync();
                if (latest == null || string.IsNullOrEmpty(latest.TagName))
                    throw new InvalidOperationException("GitHub returned no release information.");

                if (latest.IsNewVersion)
                    SetVersion(latest);
                else
                    Stage = SelfUpdateStage.UpToDate;
            }
            catch (Exception e)
            {
                ErrorMessage = GetReason(e);
                Stage = SelfUpdateStage.CheckFailed;
            }
        }

        public async Task InstallAsync()
        {
            if (_asset == null || IsBusy)
                return;

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            try
            {
                Progress = 0;
                ProgressText = string.Empty;
                IsProgressUnknown = _asset.Size <= 0;
                Stage = SelfUpdateStage.Downloading;

                var file = await Models.SelfUpdater.DownloadAsync(_asset, ReportProgress, token);

                Stage = SelfUpdateStage.Installing;
                await Models.SelfUpdater.InstallAsync(_kind, file, App.ReleaseSingleInstanceLock);

                if (Models.SelfUpdater.NeedsSystemInstaller(_kind))
                {
                    DownloadedFile = file;
                    Stage = SelfUpdateStage.HandedToSystem;
                }
                else
                {
                    // The new version (or the installer, which starts it) is already running.
                    App.Quit(0);
                }
            }
            catch (OperationCanceledException)
            {
                Stage = SelfUpdateStage.Available;
            }
            catch (Exception e)
            {
                ErrorMessage = GetReason(e);
                Stage = SelfUpdateStage.Failed;
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
            }
        }

        public void Cancel()
        {
            _cts?.Cancel();
        }

        public void SkipThisVersion()
        {
            if (_version != null)
                Preferences.Instance.IgnoreUpdateTag = _version.TagName;
        }

        public void OpenReleasePage()
        {
            Native.OS.OpenBrowser(_version?.ReleasePageUrl ?? Models.SelfUpdater.ReleasesUrl);
        }

        public void RevealDownloadedFile()
        {
            if (!string.IsNullOrEmpty(_downloadedFile))
                Native.OS.OpenInFileManager(_downloadedFile);
        }

        private void SetVersion(Models.Version version)
        {
            Version = version;
            _kind = Models.SelfUpdater.PackageKind;
            _asset = Models.SelfUpdater.FindAsset(version, _kind);
            PackageName = _asset?.Name ?? string.Empty;

            OnPropertyChanged(nameof(CanInstall));
            OnPropertyChanged(nameof(IsAutoInstall));
            OnPropertyChanged(nameof(IsSystemInstall));
            OnPropertyChanged(nameof(IsManualDownload));

            Stage = SelfUpdateStage.Available;
        }

        private void ReportProgress(long received, long total)
        {
            var now = Environment.TickCount64;
            if (received < total && now - _lastProgressTick < 100)
                return;

            _lastProgressTick = now;

            Dispatcher.UIThread.Post(() =>
            {
                if (total > 0)
                {
                    Progress = received * 100.0 / total;
                    ProgressText = $"{FormatSize(received)} / {FormatSize(total)}";
                    IsProgressUnknown = false;
                }
                else
                {
                    ProgressText = FormatSize(received);
                    IsProgressUnknown = true;
                }
            });
        }

        private static string GetReason(Exception e)
        {
            return e.InnerException is { } inner ? inner.Message : e.Message;
        }

        private static string FormatSize(long bytes)
        {
            const double kb = 1024;
            const double mb = kb * 1024;
            const double gb = mb * 1024;

            if (bytes >= gb)
                return $"{bytes / gb:0.00} GB";
            if (bytes >= mb)
                return $"{bytes / mb:0.0} MB";
            if (bytes >= kb)
                return $"{bytes / kb:0} KB";
            return $"{bytes} B";
        }

        private SelfUpdateStage _stage = SelfUpdateStage.Checking;
        private Models.Version _version = null;
        private Models.UpdatePackageKind _kind = Models.UpdatePackageKind.Unsupported;
        private Models.ReleaseAsset _asset = null;
        private string _errorMessage = string.Empty;
        private string _packageName = string.Empty;
        private double _progress = 0;
        private string _progressText = string.Empty;
        private bool _isProgressUnknown = false;
        private string _downloadedFile = string.Empty;
        private CancellationTokenSource _cts = null;
        private long _lastProgressTick = 0;
    }
}
