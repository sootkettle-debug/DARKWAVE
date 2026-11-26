using System;
using System.Windows;
using System.Windows.Interop;
using NAudio.Wave;

namespace NEONWAVE;

public partial class MainWindow
{
    private const int WmAppCommand = 0x0319;
    private HwndSource? mediaKeySource;

    // Handle the standard Windows multimedia keyboard messages while our
    // window receives input. This does not seize media keys from other apps.
    private void InstallMediaKeyHandler()
    {
        SourceInitialized += (_, _) =>
        {
            mediaKeySource = PresentationSource.FromVisual(this) as HwndSource;
            mediaKeySource?.AddHook(MediaKeyWindowProc);
        };
        Closed += (_, _) => mediaKeySource?.RemoveHook(MediaKeyWindowProc);
    }

    private IntPtr MediaKeyWindowProc(IntPtr hwnd, int message, IntPtr wParam,
        IntPtr lParam, ref bool handled)
    {
        if (message != WmAppCommand) return IntPtr.Zero;

        // HIWORD(lParam) contains the application command and its source flags.
        int command = (int)((lParam.ToInt64() >> 16) & 0x0FFF);
        handled = HandleMediaCommand(command);
        return handled ? new IntPtr(1) : IntPtr.Zero;
    }

    private bool HandleMediaCommand(int command)
    {
        switch (command)
        {
            case 14: // APPCOMMAND_MEDIA_PLAY_PAUSE
                Play_Click(this, new RoutedEventArgs());
                return true;
            case 11: // APPCOMMAND_MEDIA_NEXTTRACK
                Next_Click(this, new RoutedEventArgs());
                return true;
            case 12: // APPCOMMAND_MEDIA_PREVIOUSTRACK
                Previous_Click(this, new RoutedEventArgs());
                return true;
            case 13: // APPCOMMAND_MEDIA_STOP
                Stop_Click(this, new RoutedEventArgs());
                return true;
            case 46: // APPCOMMAND_MEDIA_PLAY (discrete play key)
                if (discModeSelected ? !cdPlaying : output?.PlaybackState != PlaybackState.Playing)
                    Play_Click(this, new RoutedEventArgs());
                return true;
            case 47: // APPCOMMAND_MEDIA_PAUSE (discrete pause key)
                if (discModeSelected ? cdPlaying : output?.PlaybackState == PlaybackState.Playing)
                    Play_Click(this, new RoutedEventArgs());
                return true;
            default:
                return false;
        }
    }
}
