using FullRumble.Updater;
using ImGuiNET;
using RecompOne.Runtime.Host.Window;

namespace FullRumble;

internal sealed class UpdateSettingsSection : ISettingsSection
{
    private readonly object _stateLock = new();
    private ReleaseMetadata? _release;
    private string _status = "Check for updates to see if a newer official release is available.";
    private bool _busy;
    private bool _pending;

    public string Id => "updates";
    public string Title => "Updates";
    public int Order => 100;

    public void Draw()
    {
        Version? installedVersion = typeof(UpdateSettingsSection).Assembly.GetName().Version;
        ImGui.TextUnformatted($"Installed version: {installedVersion?.ToString(4) ?? "unknown"}");
        ImGui.Spacing();

        (ReleaseMetadata? release, string status, bool busy, bool pending) = GetState();
        ImGui.TextWrapped(status);
        ImGui.Spacing();

        if (!busy && !pending && ImGui.Button("Check for updates"))
            CheckForUpdates();

        if (!busy && !pending && release is not null &&
            installedVersion is not null && release.Version > installedVersion)
        {
            ImGui.SameLine();
            if (ImGui.Button($"Download {release.Name}"))
                ImGui.OpenPopup("Confirm Full Rumble update");
        }

        bool popupOpen = true;
        if (ImGui.BeginPopupModal("Confirm Full Rumble update", ref popupOpen,
                ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextWrapped(
                $"Download and verify {release?.Name}, then install it after Full Rumble closes? " +
                "Save your game progress before closing.");
            ImGui.Spacing();
            if (ImGui.Button("Download update"))
            {
                ImGui.CloseCurrentPopup();
                if (release is not null)
                    DownloadAndSchedule(release);
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
                ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
    }

    private void CheckForUpdates()
    {
        lock (_stateLock)
        {
            _busy = true;
            _release = null;
            _status = "Checking the official Full Rumble releases...";
        }

        _ = Task.Run(async () =>
        {
            try
            {
                ReleaseMetadata latest = await UpdateService.CheckForLatestReleaseAsync();
                Version? current = typeof(UpdateSettingsSection).Assembly.GetName().Version;
                lock (_stateLock)
                {
                    _release = latest;
                    _status = current is null
                        ? "The latest release was found, but the installed version could not be determined."
                        : latest.Version > current
                            ? $"Update available: {latest.Name} ({latest.Version.ToString(4)})."
                            : "Full Rumble is up to date.";
                }
            }
            catch (Exception error)
            {
                SetStatus($"Update check failed: {error.Message}");
            }
            finally
            {
                lock (_stateLock) _busy = false;
            }
        });
    }

    private void DownloadAndSchedule(ReleaseMetadata release)
    {
        lock (_stateLock)
        {
            _busy = true;
            _status = $"Preparing {release.Name}...";
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var progress = new InlineProgress<string>(SetStatus);
                await UpdateService.PrepareUpdateForExitAsync(
                    release, AppContext.BaseDirectory, Environment.ProcessId, progress);
                lock (_stateLock)
                {
                    _pending = true;
                    _status = $"{release.Name} is downloaded and verified. Close Full Rumble to install it; " +
                              "the game will reopen when the update finishes.";
                }
            }
            catch (Exception error)
            {
                SetStatus($"Update could not be prepared: {error.Message}");
            }
            finally
            {
                lock (_stateLock) _busy = false;
            }
        });
    }

    private void SetStatus(string status)
    {
        lock (_stateLock) _status = status;
    }

    private (ReleaseMetadata? Release, string Status, bool Busy, bool Pending) GetState()
    {
        lock (_stateLock)
            return (_release, _status, _busy, _pending);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
