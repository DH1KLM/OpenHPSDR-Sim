using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System;
using Avalonia.Threading;
using Avalonia.Interactivity;
using Patroclus.Avalonia.ViewModels;
using Patroclus.Avalonia.Audio;

namespace Patroclus.Avalonia.Views
{
    public class MainWindow : Window
    {
        private readonly DispatcherTimer monitorTimer;
        private MainWindowViewModel recordingViewModel;
        public MainWindow()
        {
            InitializeComponent();
            DataContextChanged += (_, _) =>
            {
                if (recordingViewModel != null) recordingViewModel.RecordingCompleted -= RevealRecording;
                recordingViewModel = DataContext as MainWindowViewModel;
                if (recordingViewModel != null) recordingViewModel.RecordingCompleted += RevealRecording;
            };
            monitorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            monitorTimer.Tick += (_, _) =>
            {
                var radio = (DataContext as MainWindowViewModel)?.radio;
                radio?.Monitor?.Refresh(); radio?.RefreshRecording();
            };
            Opened += (_, _) => monitorTimer.Start();
            Closed += (_, _) =>
            {
                monitorTimer.Stop(); (DataContext as MainWindowViewModel)?.radio?.Stop();
                if (recordingViewModel != null) recordingViewModel.RecordingCompleted -= RevealRecording;
            };

        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void RevealRecording(string path)
        {
            // Never wait for the UI thread here: Stop() can be joining this writer.
            try
            {
                string fullPath = System.IO.Path.GetFullPath(path);
                var start = OperatingSystem.IsWindows()
                    ? new System.Diagnostics.ProcessStartInfo("explorer.exe", "/select,\"" + fullPath + "\"")
                    : new System.Diagnostics.ProcessStartInfo(System.IO.Path.GetDirectoryName(fullPath));
                start.UseShellExecute = true;
                System.Diagnostics.Process.Start(start)?.Dispose();
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => this.FindControl<TextBlock>("RecordingError").Text =
                    "Recording saved, but cannot open its folder: " + ex.Message);
            }
        }

        private async void StartRecording(object sender, RoutedEventArgs e)
        {
            var radio = (DataContext as MainWindowViewModel)?.radio;
            if (radio == null || radio.Recorder.IsRecording) return;
            var error = this.FindControl<TextBlock>("RecordingError");
            error.Text = "";
            try
            {
                var mode = SelectedRecordingMode();
                var dialog = new SaveFileDialog
                {
                    Title = "Save transmitted audio", DefaultExtension = "wav",
                    InitialFileName = $"TX_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}_{mode}.wav"
                };
                dialog.Filters.Add(new FileDialogFilter { Name = "WAV audio", Extensions = { "wav" } });
                string path = await dialog.ShowAsync(this);
                if (string.IsNullOrWhiteSpace(path) || radio != (DataContext as MainWindowViewModel)?.radio) return;
                radio.Recorder.Start(path, mode);
                radio.RefreshRecording();
            }
            catch (Exception ex) { error.Text = "Cannot start recording: " + ex.Message; }
        }

        private void StopRecording(object sender, RoutedEventArgs e)
        {
            var radio = (DataContext as MainWindowViewModel)?.radio;
            radio?.Recorder.Stop(); radio?.RefreshRecording();
        }

        private TxAudioMode SelectedRecordingMode()
            => this.FindControl<ComboBox>("RecordingMode").SelectedItem is ComboBoxItem item && item.Tag is TxAudioMode mode
                ? mode : TxAudioMode.USB;

        private async void ToggleAutoRecording(object sender, RoutedEventArgs e)
        {
            var radio = (DataContext as MainWindowViewModel)?.radio;
            if (radio == null || radio.Recorder.IsRecording) return;
            var error = this.FindControl<TextBlock>("RecordingError"); error.Text = "";
            try
            {
                if (radio.AutoRecorder.IsArmed) { radio.DisableAutoRecording(); return; }
                string folder = await new OpenFolderDialog { Title = "Choose automatic TX recording folder" }.ShowAsync(this);
                if (string.IsNullOrWhiteSpace(folder) || radio != (DataContext as MainWindowViewModel)?.radio) return;
                radio.EnableAutoRecording(folder, SelectedRecordingMode());
            }
            catch (Exception ex) { error.Text = "Cannot enable automatic recording: " + ex.Message; }
        }

        
    }
}
