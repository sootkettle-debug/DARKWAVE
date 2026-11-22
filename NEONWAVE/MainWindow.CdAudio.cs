using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace NEONWAVE;

public partial class MainWindow
{
    private bool discModeSelected = true;
    private CdDigitalAudio? cdReader;
    private WaveOutEvent? cdOutput;
    private VolumeSampleProvider? cdVolume;
    private string? cdDrive;
    private bool cdOpen;
    private bool cdPlaying;
    private bool cdPaused;
    private int cdCurrentTrack;
    private int cdTrackCount;
    private TimeSpan cdTrackDuration;
    private DateTime cdLastOpenAttemptUtc = DateTime.MinValue;

    private void DiscMode_Click(object sender, RoutedEventArgs e) => SelectPlaybackMode(true);
    private void FileMode_Click(object sender, RoutedEventArgs e) => SelectPlaybackMode(false);

    private void SelectPlaybackMode(bool selectDisc)
    {
        if (discModeSelected == selectDisc) return;
        discModeSelected = selectDisc;
        if (selectDisc)
        {
            if (output != null || reader != null) DisposeAudio();
            ShowNoDisc();
            cdLastOpenAttemptUtc = DateTime.MinValue;
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

    private void ShowNoDisc()
    {
        if (!discModeSelected) return;
        TrackTitle.Text = "NO DISC";
        PlayButton.Content = "▶";
        SetLogoPulse(false);
        ElapsedText.Text = DurationText.Text = "0:00";
        changingSeek = true;
        SeekSlider.Value = 0;
        changingSeek = false;
    }

    private void UpdateSourceModeDisplays()
    {
        if (DiscIndicator == null || FileIndicator == null ||
            DiscModeButton == null || FileModeButton == null) return;
        var off = new SolidColorBrush(Color.FromRgb(81, 88, 92));
        DiscIndicator.Foreground = discModeSelected ? currentAccentBrush : off;
        FileIndicator.Foreground = discModeSelected ? off : currentAccentBrush;
        DiscIndicator.Effect = discModeSelected ? new DropShadowEffect
            { Color = currentAccentColor, BlurRadius = 8, ShadowDepth = 0, Opacity = .85 } : null;
        FileIndicator.Effect = !discModeSelected ? new DropShadowEffect
            { Color = currentAccentColor, BlurRadius = 8, ShadowDepth = 0, Opacity = .85 } : null;
        DiscModeButton.Foreground = discModeSelected ? currentAccentBrush : off;
        FileModeButton.Foreground = discModeSelected ? off : currentAccentBrush;
    }

    private void PollAudioCd(string[] opticalDrives)
    {
        if (!discModeSelected) return;
        if (cdOpen)
        {
            // A CD-DA disc has no filesystem. Reopen the TOC only when the
            // drive reports loss of media, not on every 2-second UI tick.
            if (cdReader != null && !cdReader.IsDriveAvailable())
            {
                CloseCdSession(clearDisplay: false);
                ShowNoDisc();
                cdLastOpenAttemptUtc = DateTime.MinValue;
            }
            else return;
        }

        if (opticalDrives.Length == 0)
        {
            ShowNoDisc();
            DiscIndicator.ToolTip = "No CD/DVD drive detected by Windows";
            return;
        }
        if (DateTime.UtcNow - cdLastOpenAttemptUtc < TimeSpan.FromSeconds(3)) return;
        cdLastOpenAttemptUtc = DateTime.UtcNow;

        foreach (var drive in opticalDrives)
        {
            if (!CdDigitalAudio.TryOpen(drive, out var discovered) || discovered == null)
                continue;
            cdReader = discovered;
            cdDrive = drive;
            cdOpen = true;
            cdTrackCount = discovered.Tracks.Count;
            DiscIndicator.ToolTip = $"Audio CD found in {drive} (digital CD-DA)";
            int track = discovered.Tracks.First(t => t.IsAudio).Number;
            if (!PlayCdTrack(track))
            {
                TrackTitle.Text = "CD PLAYBACK ERROR";
                DiscIndicator.ToolTip = "Disc found, but the drive cannot stream CD audio";
            }
            return;
        }

        ShowNoDisc();
        DiscIndicator.ToolTip = "Drive found, but CD audio TOC was unavailable";
    }

    private bool PlayCdTrack(int track, TimeSpan? offset = null)
    {
        if (!cdOpen || cdReader == null) return false;
        try
        {
            if (reader != null || output != null) DisposeAudio();
            // Stop the callback thread BEFORE seeking or changing the raw-CD track.
            DisposeCdOutput();
            if (!cdReader.SelectTrack(track)) return false;
            cdReader.Position = Math.Clamp((long)(offset.GetValueOrDefault().TotalSeconds * cdReader.WaveFormat.AverageBytesPerSecond), 0L, cdReader.Length);
            cdCurrentTrack = track;
            cdTrackDuration = cdReader.TotalTime;
            var pcm = new WaveToSampleProvider(cdReader);
            cdVolume = new VolumeSampleProvider(pcm) { Volume = (float)(volumePercent / 100.0) };
            equalizer = new FiveBandEqualizer(cdVolume, eqGainDb);
            var player = new WaveOutEvent();
            cdOutput = player;
            player.Init(new SampleToWaveProvider(equalizer));
            player.PlaybackStopped += (_, args) =>
            {
                if (!ReferenceEquals(cdOutput, player)) return;
                Dispatcher.BeginInvoke(() =>
                {
                    if (!ReferenceEquals(cdOutput, player)) return;
                    if (args.Exception != null)
                    {
                        cdPlaying = false;
                        TrackTitle.Text = "CD READ ERROR";
                        DiscIndicator.ToolTip = args.Exception.Message;
                        StopCdPlayback();
                    }
                });
            };
            player.Play();
            cdPlaying = true;
            cdPaused = false;
            TrackTitle.Text = $"AUDIO CD - TRACK {track:00}";
            PlayButton.Content = "Ⅱ";
            SetLogoPulse(true);
            UpdateCdDisplay();
            return true;
        }
        catch (Exception error)
        {
            DisposeCdOutput();
            cdPlaying = cdPaused = false;
            DiscIndicator.ToolTip = $"Audio CD error: {error.Message}";
            TrackTitle.Text = "CD PLAYBACK ERROR";
            PlayButton.Content = "▶";
            return false;
        }
    }

    private void DisposeCdOutput()
    {
        var oldOutput = cdOutput;
        cdOutput = null;
        if (oldOutput != null)
        {
            try { oldOutput.Stop(); } catch (Exception) { }
            oldOutput.Dispose();
        }
        cdVolume = null;
        equalizer = null;
    }

    private void PauseOrResumeCd()
    {
        if (!cdOpen || cdReader == null) return;
        if (cdOutput == null)
        {
            PlayCdTrack(cdCurrentTrack > 0 ? cdCurrentTrack :
                cdReader.Tracks.First(t => t.IsAudio).Number);
            return;
        }
        if (cdOutput.PlaybackState == PlaybackState.Playing)
        {
            cdOutput.Pause();
            cdPaused = true;
            PlayButton.Content = "▶";
            SetLogoPulse(false);
        }
        else
        {
            cdOutput.Play();
            cdPaused = false;
            cdPlaying = true;
            PlayButton.Content = "Ⅱ";
            SetLogoPulse(true);
        }
    }

    private void MoveCdTrack(int direction)
    {
        if (cdReader == null) return;
        var audioTracks = cdReader.Tracks.Where(t => t.IsAudio).Select(t => t.Number).ToList();
        if (audioTracks.Count == 0) return;
        int pos = audioTracks.IndexOf(cdCurrentTrack);
        int next = (Math.Max(pos, 0) + direction + audioTracks.Count) % audioTracks.Count;
        PlayCdTrack(audioTracks[next]);
    }

    private void StopCdPlayback(bool closeSession = false)
    {
        DisposeCdOutput();
        cdPlaying = cdPaused = false;
        SetLogoPulse(false);
        PlayButton.Content = "▶";
        if (cdReader != null) cdReader.Position = 0;
        if (closeSession) CloseCdSession(clearDisplay: true);
        else UpdateCdDisplay();
    }

    private void CloseCdSession(bool clearDisplay)
    {
        DisposeCdOutput();
        cdReader?.Dispose();
        cdReader = null;
        cdOpen = cdPlaying = cdPaused = false;
        cdDrive = null;
        cdCurrentTrack = cdTrackCount = 0;
        cdTrackDuration = TimeSpan.Zero;
        if (clearDisplay) ShowNoDisc();
    }

    private void UpdateCdDisplay()
    {
        if (!cdOpen || cdReader == null) return;
        ElapsedText.Text = FormatTime(cdReader.CurrentTime);
        DurationText.Text = FormatTime(cdTrackDuration);
        if (draggingSeek) return;
        changingSeek = true;
        SeekSlider.Value = cdTrackDuration.TotalSeconds > 0
            ? Math.Clamp(cdReader.CurrentTime.TotalSeconds / cdTrackDuration.TotalSeconds * 100, 0, 100) : 0;
        changingSeek = false;
    }

    private void TickCdPlayback()
    {
        if (!cdOpen || cdReader == null) return;
        if (cdPlaying && !cdPaused && cdReader.Position >= cdReader.Length)
        {
            var tracks = cdReader.Tracks.Where(t => t.IsAudio).Select(t => t.Number).ToList();
            int index = tracks.IndexOf(cdCurrentTrack);
            if (index >= 0 && index + 1 < tracks.Count)
                PlayCdTrack(tracks[index + 1]);
            else
                StopCdPlayback();
        }
        UpdateCdDisplay();
    }

    private void SeekCdToPercent(double percent)
    {
        if (!cdOpen || cdReader == null || cdTrackDuration <= TimeSpan.Zero) return;
        var offset = TimeSpan.FromSeconds(Math.Clamp(percent, 0, 99.5) * cdTrackDuration.TotalSeconds / 100);
        bool wasPaused = cdPaused;
        if (PlayCdTrack(cdCurrentTrack, offset) && wasPaused) PauseOrResumeCd();
    }

    private void ApplyCdVolumeIfSupported()
    {
        if (cdVolume != null) cdVolume.Volume = (float)(volumePercent / 100.0);
    }
}
