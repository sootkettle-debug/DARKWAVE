using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace NEONWAVE;

public partial class MainWindow
{
    // DISC is the startup source so inserting an audio CD still starts it automatically.
    private bool discModeSelected = true;

    private void DiscMode_Click(object sender, RoutedEventArgs e) => SelectPlaybackMode(true);

    private void FileMode_Click(object sender, RoutedEventArgs e) => SelectPlaybackMode(false);

    private void SelectPlaybackMode(bool selectDisc)
    {
        if (discModeSelected == selectDisc) return;
        discModeSelected = selectDisc;
        if (selectDisc)
        {
            if (output != null || reader != null) DisposeAudio();
            cdAttemptedForInsertion = false;
            lastReadyCdDrive = null;
            TrackTitle.Text = "NO DISC";
            ElapsedText.Text = DurationText.Text = "0:00";
            changingSeek = true;
            SeekSlider.Value = 0;
            changingSeek = false;
            PlayButton.Content = "▶";
            UpdateDiscIndicator();
        }
        else
        {
            if (cdOpen) CloseCdSession(clearDisplay: false);
            PlayButton.Content = "▶";
            if (reader != null && currentIndex >= 0 && currentIndex < activeTracks.Count)
                TrackTitle.Text = activeTracks[currentIndex].DisplayName;
            else
            {
                TrackTitle.Text = "NO TRACK LOADED";
                ElapsedText.Text = DurationText.Text = "0:00";
                changingSeek = true;
                SeekSlider.Value = 0;
                changingSeek = false;
            }
            UpdateSourceModeDisplays();
        }
    }

    private void UpdateSourceModeDisplays()
    {
        if (DiscIndicator == null || FileIndicator == null ||
            DiscModeButton == null || FileModeButton == null) return;
        var offBrush = new SolidColorBrush(Color.FromRgb(81, 88, 92));
        DiscIndicator.Foreground = discModeSelected ? currentAccentBrush : offBrush;
        FileIndicator.Foreground = discModeSelected ? offBrush : currentAccentBrush;
        DiscIndicator.Effect = discModeSelected
            ? new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = currentAccentColor, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.85
            } : null;
        FileIndicator.Effect = !discModeSelected
            ? new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = currentAccentColor, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.85
            } : null;
        DiscModeButton.Foreground = discModeSelected ? currentAccentBrush : offBrush;
        FileModeButton.Foreground = discModeSelected ? offBrush : currentAccentBrush;
    }

    // MCI provides Windows' native audio-CD transport; file playback still uses NAudio.
    private const string CdAlias = "CompactDarkAudioDisc";
    private string? cdDrive;
    private string? lastReadyCdDrive;
    private bool cdAttemptedForInsertion;
    private bool cdOpen;
    private bool cdPlaying;
    private bool cdPaused;
    private int cdCurrentTrack;
    private int cdTrackCount;
    private TimeSpan cdTrackDuration;
    private readonly Stopwatch cdPlayWatch = new();
    private TimeSpan cdElapsedBeforePlay;
    private DateTime cdLastOpenAttemptUtc = DateTime.MinValue;

    [System.Runtime.InteropServices.DllImport("winmm.dll", EntryPoint = "mciSendStringW",
        CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern uint MciQueryString(string command, StringBuilder answer,
        uint answerCapacity, IntPtr callback);

    private static string? CdQuery(string command)
    {
        var result = new StringBuilder(128);
        return MciQueryString(command, result, (uint)result.Capacity, IntPtr.Zero) == 0
            ? result.ToString().Trim()
            : null;
    }

    private static uint CdCommand(string command) =>
        MciSendString(command, IntPtr.Zero, 0, IntPtr.Zero);

    private static TimeSpan? ParseCdTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        // TMSF is track:minute:second:frame; some drivers omit the track field.
        var parts = value.Split(':');
        if (parts.Length < 3 ||
            !int.TryParse(parts[^3], out int minutes) ||
            !int.TryParse(parts[^2], out int seconds) ||
            !int.TryParse(parts[^1], out int frames)) return null;
        return TimeSpan.FromSeconds(minutes * 60 + seconds + frames / 75.0);
    }

    private void PollAudioCd(bool mediaPresent, string? driveName)
    {
        // Only DISC mode can activate a CD. File mode never steals playback
        // from the library when a disc is inserted.
        if (!discModeSelected) return;
        // Called by the existing two-second optical-drive poll.
        if (!mediaPresent || driveName == null)
        {
            if (cdOpen) CloseCdSession(clearDisplay: false);
            lastReadyCdDrive = null;
            cdAttemptedForInsertion = false;
            TrackTitle.Text = "NO DISC";
            return;
        }

        if (!string.Equals(lastReadyCdDrive, driveName, StringComparison.OrdinalIgnoreCase))
        {
            if (cdOpen) CloseCdSession(clearDisplay: false);
            lastReadyCdDrive = driveName;
            cdAttemptedForInsertion = false;
        }

        if (cdOpen)
        {
            if (!string.Equals(CdQuery($"status {CdAlias} media present"), "true",
                    StringComparison.OrdinalIgnoreCase))
            {
                CloseCdSession(clearDisplay: false);
                TrackTitle.Text = "NO DISC";
                cdAttemptedForInsertion = false;
            }
            else return;
        }

        // One automatic attempt for each insertion, with a short retry delay if
        // Windows reports the disc ready before the optical driver is initialized.
        if (cdAttemptedForInsertion ||
            DateTime.UtcNow - cdLastOpenAttemptUtc < TimeSpan.FromSeconds(3)) return;
        cdLastOpenAttemptUtc = DateTime.UtcNow;
        if (TryOpenAudioCd(driveName))
        {
            cdAttemptedForInsertion = true;
            int firstAudioTrack = FindAudioTrack(1, 1);
            if (firstAudioTrack > 0) PlayCdTrack(firstAudioTrack);
        }
    }

    private bool TryOpenAudioCd(string driveName)
    {
        var driveLetter = driveName.TrimEnd('\\');
        if (CdCommand($"open {driveLetter} type cdaudio alias {CdAlias}") != 0)
            return false;

        if (CdCommand($"set {CdAlias} time format tmsf") != 0 ||
            !int.TryParse(CdQuery($"status {CdAlias} number of tracks"), out int tracks) ||
            tracks < 1)
        {
            CdCommand($"close {CdAlias}");
            return false;
        }
        cdTrackCount = tracks;
        cdDrive = driveName;
        cdOpen = true;
        cdCurrentTrack = 0;
        return true;
    }

    private int FindAudioTrack(int start, int direction)
    {
        if (!cdOpen || cdTrackCount < 1) return -1;
        for (int i = start; i >= 1 && i <= cdTrackCount; i += direction)
        {
            if (string.Equals(CdQuery($"status {CdAlias} type track {i}"),
                    "audio", StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    private bool PlayCdTrack(int track, TimeSpan? offset = null)
    {
        if (!cdOpen || track < 1 || track > cdTrackCount) return false;
        if (FindAudioTrack(track, 1) != track) return false;

        // A newly inserted disc takes priority over any previously playing file.
        if (reader != null || output != null) DisposeAudio();
        CdCommand($"stop {CdAlias}");
        var start = offset.GetValueOrDefault();
        string startPosition = $"{track}:{(int)start.TotalMinutes}:{start.Seconds}:{(int)Math.Round(start.Milliseconds * 75.0 / 1000.0)}";
        string end = track < cdTrackCount ? $" to {track + 1}:0:0:0" : "";
        if (CdCommand($"play {CdAlias} from {startPosition}{end}") != 0)
        {
            cdPlaying = cdPaused = false;
            PlayButton.Content = "▶";
            return false;
        }

        cdCurrentTrack = track;
        cdTrackDuration = ParseCdTime(CdQuery($"status {CdAlias} length track {track}"))
                           ?? TimeSpan.Zero;
        cdElapsedBeforePlay = start;
        cdPlayWatch.Restart();
        cdPlaying = true;
        cdPaused = false;
        TrackTitle.Text = $"AUDIO CD - TRACK {track:00}";
        PlayButton.Content = "Ⅱ";
        SetLogoPulse(true);
        UpdateCdDisplay();
        return true;
    }

    private void PauseOrResumeCd()
    {
        if (!cdOpen || cdCurrentTrack < 1) return;
        if (cdPlaying && !cdPaused)
        {
            if (CdCommand($"pause {CdAlias}") == 0)
            {
                cdElapsedBeforePlay += cdPlayWatch.Elapsed;
                cdPlayWatch.Reset();
                cdPaused = true;
                PlayButton.Content = "▶";
                SetLogoPulse(false);
            }
        }
        else if (cdPaused)
        {
            if (CdCommand($"resume {CdAlias}") != 0)
            {
                PlayCdTrack(cdCurrentTrack, cdElapsedBeforePlay);
                return;
            }
            cdPaused = false;
            cdPlayWatch.Restart();
            PlayButton.Content = "Ⅱ";
            SetLogoPulse(true);
        }
        else
            PlayCdTrack(cdCurrentTrack);
    }

    private void MoveCdTrack(int direction)
    {
        if (!cdOpen) return;
        int next = FindAudioTrack(Math.Clamp(cdCurrentTrack + direction, 1, cdTrackCount), direction);
        if (next < 0 && direction > 0) next = FindAudioTrack(1, 1);
        if (next < 0 && direction < 0) next = FindAudioTrack(cdTrackCount, -1);
        if (next > 0) PlayCdTrack(next);
    }

    private void StopCdPlayback(bool closeSession = false)
    {
        if (!cdOpen) return;
        CdCommand($"stop {CdAlias}");
        cdPlaying = false;
        cdPaused = false;
        cdPlayWatch.Reset();
        cdElapsedBeforePlay = TimeSpan.Zero;
        SetLogoPulse(false);
        PlayButton.Content = "▶";
        if (closeSession) CloseCdSession(clearDisplay: true);
        else UpdateCdDisplay();
    }

    private void CloseCdSession(bool clearDisplay)
    {
        if (!cdOpen) return;
        CdCommand($"stop {CdAlias}");
        CdCommand($"close {CdAlias}");
        cdOpen = cdPlaying = cdPaused = false;
        cdDrive = null;
        cdCurrentTrack = cdTrackCount = 0;
        cdTrackDuration = cdElapsedBeforePlay = TimeSpan.Zero;
        cdPlayWatch.Reset();
        SetLogoPulse(false);
        if (clearDisplay && reader == null)
        {
            PlayButton.Content = "▶";
            TrackTitle.Text = "NO TRACK LOADED";
            ElapsedText.Text = DurationText.Text = "0:00";
            changingSeek = true;
            SeekSlider.Value = 0;
            changingSeek = false;
        }
    }

    private void UpdateCdDisplay()
    {
        if (!cdOpen || cdCurrentTrack < 1) return;
        TimeSpan elapsed = cdElapsedBeforePlay + (cdPaused ? TimeSpan.Zero : cdPlayWatch.Elapsed);
        // Some drivers report TMSF positions accurately; use them where possible.
        if (!cdPaused && cdPlaying)
        {
            var position = ParseCdTime(CdQuery($"status {CdAlias} position"));
            if (position.HasValue && position.Value >= TimeSpan.Zero &&
                (cdTrackDuration == TimeSpan.Zero || position.Value <= cdTrackDuration + TimeSpan.FromSeconds(2)))
                elapsed = position.Value;
        }

        if (cdTrackDuration > TimeSpan.Zero && elapsed > cdTrackDuration)
            elapsed = cdTrackDuration;
        ElapsedText.Text = FormatTime(elapsed);
        DurationText.Text = FormatTime(cdTrackDuration);
        changingSeek = true;
        SeekSlider.Value = cdTrackDuration.TotalSeconds > 0
            ? Math.Clamp(elapsed.TotalSeconds / cdTrackDuration.TotalSeconds * 100, 0, 100)
            : 0;
        changingSeek = false;
    }

    private void TickCdPlayback()
    {
        if (!cdOpen || cdCurrentTrack < 1) return;
        if (cdPlaying && !cdPaused)
        {
            string? mode = CdQuery($"status {CdAlias} mode");
            if (string.Equals(mode, "stopped", StringComparison.OrdinalIgnoreCase) &&
                cdPlayWatch.Elapsed >= TimeSpan.FromSeconds(1))
            {
                int next = FindAudioTrack(cdCurrentTrack + 1, 1);
                if (next > 0) PlayCdTrack(next);
                else StopCdPlayback();
                return;
            }
        }
        UpdateCdDisplay();
    }

    private void SeekCdToPercent(double percentage)
    {
        if (!cdOpen || cdCurrentTrack < 1 || cdTrackDuration <= TimeSpan.Zero) return;
        bool wasPaused = cdPaused;
        var offset = TimeSpan.FromSeconds(Math.Clamp(percentage, 0, 99.5) / 100 * cdTrackDuration.TotalSeconds);
        if (PlayCdTrack(cdCurrentTrack, offset) && wasPaused) PauseOrResumeCd();
    }

    private void ApplyCdVolumeIfSupported()
    {
        if (!cdOpen) return;
        // CDAudio MCI drivers vary: this is best-effort, not guaranteed.
        CdCommand($"setaudio {CdAlias} volume to {(int)(volumePercent * 10)}");
    }
}
