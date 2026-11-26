using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NEONWAVE;

public partial class MainWindow
{
    private int dimLevelPercent = 10;
    private Window? dimSettingsWindow;
    private readonly System.Collections.Generic.Dictionary<int, Button> dimChoiceButtons = new();

    private static string DimLevelSettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Darkwave+CD", "dim-level.txt");

    private void LoadDimLevel()
    {
        try
        {
            if (File.Exists(DimLevelSettingsPath) &&
                int.TryParse(File.ReadAllText(DimLevelSettingsPath).Trim(), out int saved) &&
                (saved == 10 || saved == 15 || saved == 20 || saved == 25))
                dimLevelPercent = saved;
        }
        catch (Exception) { /* An inaccessible settings folder must not prevent playback. */ }
    }

    private void SelectDimLevel(int value)
    {
        if (value != 10 && value != 15 && value != 20 && value != 25) return;
        dimLevelPercent = value;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DimLevelSettingsPath)!);
            File.WriteAllText(DimLevelSettingsPath, value.ToString());
        }
        catch (Exception) { /* Continue using the choice for this session. */ }

        // Changing the setting while DIM is active updates its actual volume,
        // but leaves the original pre-DIM level intact for restoration.
        if (dimRestoreVolume.HasValue) SetVolume(dimLevelPercent);
        else UpdateVolumeControl();

        foreach (var option in dimChoiceButtons)
        {
            option.Value.Content = option.Key == dimLevelPercent ? "●" : "○";
            option.Value.Foreground = option.Key == dimLevelPercent
                ? new SolidColorBrush(Color.FromRgb(242, 165, 75))
                : new SolidColorBrush(Color.FromRgb(157, 167, 172));
        }
    }

    private void DimSettings_Click(object sender, RoutedEventArgs e)
    {
        if (dimSettingsWindow != null)
        {
            PositionDimSettingsWindow();
            dimSettingsWindow.Activate();
            return;
        }

        var popup = new Window
        {
            Title = "DIM LEVEL",
            Width = 170,
            Height = 186,
            MinWidth = 170,
            MinHeight = 186,
            MaxWidth = 170,
            MaxHeight = 186,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Background = new SolidColorBrush(Color.FromRgb(21, 27, 30)),
            Foreground = new SolidColorBrush(Color.FromRgb(220, 228, 228)),
            Owner = this
        };

        var outer = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(92, 97, 99)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(10, 7, 10, 8),
            Background = new SolidColorBrush(Color.FromRgb(31, 37, 40))
        };
        var layout = new StackPanel();
        var header = new Grid { Height = 25, Margin = new Thickness(0, 0, 0, 6) };
        header.Children.Add(new TextBlock
        {
            Text = "DIM LEVEL", FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(202, 211, 213)),
            VerticalAlignment = VerticalAlignment.Center
        });
        var closeButton = new Button
        {
            Content = "×", Width = 22, Height = 20, Padding = new Thickness(0),
            FontSize = 13, FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(53, 59, 62)),
            Foreground = new SolidColorBrush(Color.FromRgb(233, 235, 236)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(96, 105, 108)),
            BorderThickness = new Thickness(1)
        };
        closeButton.Click += (_, _) => popup.Close();
        header.Children.Add(closeButton);
        layout.Children.Add(header);

        dimChoiceButtons.Clear();
        foreach (int level in new[] { 10, 15, 20, 25 })
        {
            var line = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Height = 29,
                Margin = new Thickness(4, 0, 0, 2)
            };
            var option = new Button
            {
                Width = 19, Height = 19, Padding = new Thickness(0),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12, FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromRgb(38, 45, 49)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(104, 111, 112)),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center
            };
            option.Click += (_, _) => SelectDimLevel(level);
            dimChoiceButtons[level] = option;
            line.Children.Add(option);
            var label = new TextBlock
            {
                Text = level + "%", FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(213, 222, 223)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };
            line.Children.Add(label);
            layout.Children.Add(line);
        }
        outer.Child = layout;
        popup.Content = outer;
        dimSettingsWindow = popup;
        popup.Closed += (_, _) =>
        {
            dimSettingsWindow = null;
            dimChoiceButtons.Clear();
        };
        SelectDimLevel(dimLevelPercent);
        PositionDimSettingsWindow();
        popup.Show();
    }

    private void PositionDimSettingsWindow()
    {
        if (dimSettingsWindow == null) return;
        // Match the top of the playback control pane, just right of the player.
        double paneOffset = PlaybackControlsPanel.TranslatePoint(new Point(0, 0), this).Y;
        var area = SystemParameters.WorkArea;
        double preferredLeft = Left + ActualWidth + 8;
        dimSettingsWindow.Left = preferredLeft + dimSettingsWindow.Width > area.Right
            ? Math.Max(area.Left, Left - dimSettingsWindow.Width - 8)
            : preferredLeft;
        dimSettingsWindow.Top = Math.Clamp(Top + paneOffset, area.Top,
            Math.Max(area.Top, area.Bottom - dimSettingsWindow.Height));
    }
}
