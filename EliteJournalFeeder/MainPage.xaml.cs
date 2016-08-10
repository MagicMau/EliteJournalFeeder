using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace EliteJournalFeeder
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page, INotifyPropertyChanged
    {
        private StorageFile _inputFile;
        public StorageFile InputFile { get { return _inputFile; } set { _inputFile = value; PropChange("InputFile", "IsStartEnabled"); } }

        private StorageFolder _outputFolder;
        public StorageFolder OutputFolder { get { return _outputFolder; } set { _outputFolder = value; PropChange("OutputFolder", "IsStartEnabled"); } }

        public bool IsStartEnabled { get { return InputFile != null && OutputFolder != null; } }

        private bool _isRunning = false;
        public bool IsRunning { get { return _isRunning; } set { _isRunning = value; PropChange("IsRunning"); } }

        private bool _isBreakpointEnabled = false;
        public bool IsBreakpointEnabled { get { return _isBreakpointEnabled; } set { _isBreakpointEnabled = value; PropChange("IsBreakpointEnabled"); } }

        private DateTime? _breakpointTime;
        public DateTime? BreakpointTime { get { return _breakpointTime; } set { _breakpointTime = value; PropChange("BreakpointTime"); } }

        private int _timeAcceleration = 1;
        public int TimeAcceleration { get { return _timeAcceleration; } set { _timeAcceleration = value; PropChange("TimeAcceleration"); } }

        public MainPage()
        {
            this.InitializeComponent();

            cboHours.ItemsSource = Enumerable.Range(0, 24).Select(i => i.ToString("00"));
            cboMinutes.ItemsSource = Enumerable.Range(0, 60).Select(i => i.ToString("00"));
            cboSeconds.ItemsSource = Enumerable.Range(0, 60).Select(i => i.ToString("00"));

            DataContext = this;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void PropChange(params string[] propertyNames)
        {
            foreach(var propertyName in propertyNames)
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private async void btnDestFolderPicker_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add(".log");
            OutputFolder = await picker.PickSingleFolderAsync();
        }

        private async void btnSourceFilePicker_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".log");

            InputFile = await picker.PickSingleFileAsync();
        }

        private void BreakpointTime_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var hours = cboHours.SelectedValue;
            var minutes = cboMinutes.SelectedValue;
            var seconds = cboSeconds.SelectedValue;

            if (hours != null && minutes != null && seconds != null)
            {
                BreakpointTime = DateTime.ParseExact($"{hours}:{minutes}:{seconds}", "HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }

    class NullIsFalseConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return value != null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    class StartButtonConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool isRunning = value as bool? ?? false;
            return isRunning ? "Stop" : "Start";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
