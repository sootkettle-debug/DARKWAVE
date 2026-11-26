using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace NEONWAVE;

public partial class MainWindow
{
    private bool discModeSelected = false;
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
    private readonly ObservableCollection<Track> cdListTracks = new();
    private readonly System.Collections.Generic.Dictionary<Track, int> cdTrackNumbers = new();
    private System.Collections.IEnumerable? fileTrackListSource;
    private string fileTrackListHeading = "TRACKS";

    private void DiscMode_Click(object sender, RoutedEventArgs e) => SelectPlaybackMode(true);
    private void FileMode_Click(object sender, RoutedEventArgs e) => SelectPlaybackMode(false);

    private void SelectPlaybackMode(bool selectDisc)
    {
        if (discModeSelected == selectDisc) return;
        discModeSelected = selectDisc;
        if (selectDisc)
        {
            if (output != null || reader != null) DisposeAudio();
            ShowCdTrackList();
            ShowNoDisc();
            cdLastOpenAttemptUtc = DateTime.MinValue;
            UpdateDiscIndicator();
        }
        else
        {
            if (cdOpen) CloseCdSession(clearDisplay: false);
            RestoreFileTrackList();
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

    private void ShowCdTrackList()
    {
        if (ReferenceEquals(FolderTracks.ItemsSource, cdListTracks)) return;
        fileTrackListSource = FolderTracks.ItemsSource;
        fileTrackListHeading = FolderContentsTitle.Text;
        FolderTracks.ItemsSource = cdListTracks;
        FolderContentsTitle.Text = "AUDIO CD TRACKS";
    }

    private void RestoreFileTrackList()
    {
        if (!ReferenceEquals(FolderTracks.ItemsSource, cdListTracks)) return;
        FolderTracks.ItemsSource = fileTrackListSource;
        FolderContentsTitle.Text = fileTrackListHeading;
        fileTrackListSource = null;
    }

    private string CdTrackName(int number)
    {
        var title = cdReader?.TrackTitles.GetValueOrDefault(number);
        return string.IsNullOrWhiteSpace(title)
            ? $"AUDIO CD - TRACK {number:00}"
            : title;
    }

    private void LoadCdTrackList()
    {
        ShowCdTrackList();
        cdTrackNumbers.Clear();
        cdListTracks.Clear();
        if (cdReader == null) return;
        foreach (var item in cdReader.Tracks.Where(t => t.IsAudio))
        {
            var track = new Track
            {
                Path = $"cd-track-{item.Number:00}",
                DisplayNameOverride = $"{item.Number:00}  {CdTrackName(item.Number)}"
            };
            cdTrackNumbers[track] = item.Number;
            cdListTracks.Add(track);
        }
        FolderContentsTitle.Text = "AUDIO CD TRACKS";
        FolderTracks.SelectedItem = cdListTracks.FirstOrDefault();
        cdCurrentTrack = cdReader.Tracks.First(t => t.IsAudio).Number;
        cdReader.SelectTrack(cdCurrentTrack);
        cdTrackDuration = cdReader.TotalTime;
        TrackTitle.Text = CdTrackName(cdCurrentTrack);
        PlayButton.Content = "▶";
        UpdateCdDisplay();
    }

    private void SelectCdTrack(int number, bool startPlayback)
    {
        if (!cdOpen || cdReader == null) return;
        if (startPlayback)
        {
            PlayCdTrack(number);
            return;
        }
        DisposeCdOutput();
        if (!cdReader.SelectTrack(number)) return;
        cdCurrentTrack = number;
        cdTrackDuration = cdReader.TotalTime;
        cdPlaying = cdPaused = false;
        foreach (var item in cdListTracks) item.IsPlaying = false;
        TrackTitle.Text = CdTrackName(number);
        PlayButton.Content = "▶";
        SetLogoPulse(false);
        UpdateCdDisplay();
    }

    private void CdTrack_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!discModeSelected || !cdOpen ||
            FolderTracks.SelectedItem is not Track selected ||
            !cdTrackNumbers.TryGetValue(selected, out var number)) return;
        if (number != cdCurrentTrack) SelectCdTrack(number, startPlayback: false);
    }

    private int SelectedCdTrackNumber()
    {
        if (FolderTracks.SelectedItem is Track selected &&
            cdTrackNumbers.TryGetValue(selected, out var number)) return number;
        return cdCurrentTrack > 0 ? cdCurrentTrack : cdReader?.Tracks.First(t => t.IsAudio).Number ?? 1;
    }

    private void ShowNoDisc()
    {
        if (!discModeSelected) return;
        if (ReferenceEquals(FolderTracks.ItemsSource, cdListTracks))
        {
            cdListTracks.Clear();
            cdTrackNumbers.Clear();
            FolderContentsTitle.Text = "AUDIO CD TRACKS";
        }
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
        DiscSelectorDot.Fill = discModeSelected ? currentAccentBrush : off;
        FileSelectorDot.Fill = discModeSelected ? off : currentAccentBrush;
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
            // Detect and list the disc without starting audio.
            LoadCdTrackList();
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
            // CD-DA sectors are signed PCM16, not IEEE floating point.
            var pcm = new Pcm16BitToSampleProvider(cdReader);
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
            TrackTitle.Text = CdTrackName(track);
            var row = cdTrackNumbers.FirstOrDefault(pair => pair.Value == track).Key;
            if (row != null)
            {
                if (FolderTracks.SelectedItem != row)
                    FolderTracks.SelectedItem = row;
                foreach (var item in cdListTracks) item.IsPlaying = item == row;
                FolderTracks.ScrollIntoView(row);
            }
            PlayButton.Content = "Ⅱ";
            SetLogoPulse(true);
            UpdateCdDisplay();
            return true;
        }
        catch (Exception error)
        {
            DisposeCdOutput();
            cdPlaying = cdPaused = false;
            DiscIndicator.ToolTip = $"Audio CD playback failed: {error.GetType().Name}: {error.Message}";
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
            PlayCdTrack(SelectedCdTrackNumber());
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
        SelectCdTrack(audioTracks[next], startPlayback: cdPlaying || cdPaused);
    }

    private void StopCdPlayback(bool closeSession = false)
    {
        DisposeCdOutput();
        cdPlaying = cdPaused = false;
        SetLogoPulse(false);
        PlayButton.Content = "▶";
        if (cdReader != null) cdReader.Position = 0;
        foreach (var track in cdListTracks) track.IsPlaying = false;
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
        cdTrackNumbers.Clear();
        cdListTracks.Clear();
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
