using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI.Core;
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

        private int _timeAcceleration = 1, _prevTimeAcceleration = 1;
        public int TimeAcceleration { get { return _timeAcceleration; } set { _timeAcceleration = value; PropChange("TimeAcceleration"); } }

        private bool _isPaused = false;
        private AutoResetEvent pauseEvent = new AutoResetEvent(true);
        public bool IsPaused { get { return _isPaused; } set { _isPaused = value; PropChange("IsPaused"); SetPaused(value); } }

        private DateTime _nextEvent;
        public DateTime NextEvent { get { return _nextEvent; } set { _nextEvent = value; PropChange("NextEvent", "NextEventText"); } }

        public ObservableCollection<string> LogEvents { get; set; } = new ObservableCollection<string>();
 
        private DispatcherTimer _timer;
        public string CurrentTimeText { get { return $"Current time: {DateTime.UtcNow.ToString("HH:mm:ss")}."; } }

        public string NextEventText { get { return $"Next event is at: {NextEvent.ToString("HH:mm:ss")}."; } }

        private CancellationTokenSource _waitForNextEventTokenSource = new CancellationTokenSource();

        public MainPage()
        {
            this.InitializeComponent();

            cboHours.ItemsSource = Enumerable.Range(0, 24).Select(i => i.ToString("00"));
            cboMinutes.ItemsSource = Enumerable.Range(0, 60).Select(i => i.ToString("00"));
            cboSeconds.ItemsSource = Enumerable.Range(0, 60).Select(i => i.ToString("00"));

            DataContext = this;

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += (s, e) => PropChange("CurrentTimeText");
            _timer.Start();

            PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "TimeAcceleration")
                {
                    var timeToGo = NextEvent - DateTime.UtcNow;
                    var newTimeToGo = (timeToGo.TotalMilliseconds * _prevTimeAcceleration) / TimeAcceleration;
                    NextEvent = DateTime.UtcNow + TimeSpan.FromMilliseconds(newTimeToGo);
                    _prevTimeAcceleration = TimeAcceleration;
                    _waitForNextEventTokenSource.Cancel();
                }
            };
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private async void PropChange(params string[] propertyNames)
        {
            foreach (var propertyName in propertyNames)
                // make sure this is run on the UI thread
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
                });
        }

        private void SetPaused(bool isPaused)
        {
            if (isPaused)
                pauseEvent.Reset();
            else
                pauseEvent.Set();
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

        private async void btnStart_Click(object sender, RoutedEventArgs e)
        {
            if (!IsRunning)
            {
                IsRunning = true;
                await Task.Run(() => FeedLog());
            }
            else
            {
                IsRunning = false;
            }
        }


        private Regex lineParser = new Regex(@"^{(\d{2}:\d{2}:\d{2})}", RegexOptions.Compiled);

        private async Task FeedLog()
        {
            try
            {
                var lines = await FileIO.ReadLinesAsync(InputFile);

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => LogEvents.Clear());

                // create the output file
                var outputFileName = $"netLog.{DateTime.UtcNow.ToString("yyMMddHHmmss")}.01.log";
                var outputFile = await OutputFolder.CreateFileAsync(outputFileName);

                NextEvent = DateTime.Now;
                DateTime logTime = DateTime.MinValue, previousLogTime;

                using (var trans = await outputFile.OpenStreamForWriteAsync())
                using (var writer = new DataWriter(trans.AsOutputStream()))
                {
                    await writer.StoreAsync();

                    foreach (string line in lines)
                    {
                        if (!IsRunning)
                            break;

                        var match = lineParser.Match(line);
                        if (match.Success)
                        {
                            // extract timestamp from line
                            var timestamp = DateTime.ParseExact(match.Groups[1].Value, "HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

                            // wait until it's time for this line (take time acceleration into account)
                            previousLogTime = logTime;
                            logTime = timestamp;
                            int delta = previousLogTime == DateTime.MinValue
                                ? 0
                                : (int)Math.Max(0, (logTime - previousLogTime).TotalMilliseconds) / TimeAcceleration;
                            NextEvent = DateTime.UtcNow + TimeSpan.FromMilliseconds(delta);

                            TimeSpan timeToGo = NextEvent - DateTime.UtcNow;
                            while (timeToGo > TimeSpan.Zero)
                            {
                                try
                                {
                                    await Task.Delay(timeToGo, _waitForNextEventTokenSource.Token);
                                }
                                catch (TaskCanceledException)
                                {
                                    // the delay has been cancelled due to a change in the time acceleration
                                    _waitForNextEventTokenSource = new CancellationTokenSource();
                                }

                                timeToGo = NextEvent - DateTime.UtcNow;
                            }

                            if (!IsRunning)
                                break;

                            // check if we're hitting the breakpoint
                            IsPaused = IsBreakpointEnabled && BreakpointTime.HasValue && timestamp >= BreakpointTime.Value;

                            // check if we're paused
                            // if so, wait until we're unpaused before writing the line to the file
                            while (IsPaused && IsRunning && !pauseEvent.WaitOne(100))
                                ;

                        }
                        // ELSE: this is part of the header probably, just flush it out to the output

                        // append the line to the output file 
                        writer.WriteString(line + Environment.NewLine);
                        // flush it out so that it is actually written to disk.
                        await writer.StoreAsync();

                        if (!IsRunning)
                            break;

                        // update the listbox on the UI thread
                        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => {
                            LogEvents.Add(line);
                            lbxEvents.ScrollIntoView(line);
                        });
                    }
                }
            }
            catch { }
            finally
            {
                IsRunning = false;
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

    class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool b = value as bool? ?? false;
            return b ? "Visible" : "Collapsed";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
