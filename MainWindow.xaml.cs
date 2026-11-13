using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security;
using System.Text.RegularExpressions;
using System.Management;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Collections.Generic;

using Windows.Media.Control;
using Windows.Storage.Streams;

using NAudio.Wave;
using NAudio.CoreAudioApi;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace NovaIsland
{
    public partial class MainWindow : Window
    {
        // Window sizes
        private const double CompactWidth = 300;
        private const double CompactHeight = 54;
        private const double ExpandedWidth = 400;
        private const double ExpandedHeight = 280;
        private const double TopMargin = 10;

        // Island state
        private bool isExpanded;
        private bool isAnimating;
        private bool isPageAnimating;
        private int currentPageIndex;
        private bool isSeeking;
        private bool isRefreshingMedia;
        private bool mediaIsPlaying;
        private string lastMediaKey = string.Empty;

        // Windows media session
        private GlobalSystemMediaTransportControlsSessionManager? mediaManager;
        private GlobalSystemMediaTransportControlsSession? currentSession;

        // Timers
        private readonly DispatcherTimer expandTimer;
        private readonly DispatcherTimer collapseTimer;
        private readonly DispatcherTimer mediaTimer;
        private readonly DispatcherTimer audioUiTimer;
        private readonly DispatcherTimer clockTimer;

        // Page navigation
        private readonly Grid[] pages;
        private readonly Border[] pageDots;
        private readonly string[] pageTitles =
        {
            "MUSIC & LYRICS",
            "SYSTEM MONITOR",
            "DATE & CALENDAR",
            "QUICK SETTINGS",
            "NOTIFICATIONS",
            "DEVICES & ALERTS",
            "AI AGENT",
            "FOCUS"
        };

        // Global audio visualizer. It updates the entire IslandShell,
        // so the island reacts whichever page is currently visible.
        private WasapiRecorder? audioRecorder;
        private readonly object audioLock = new object();
        private readonly double[] targetAudioBars = new double[5];
        private readonly double[] displayedAudioBars = new double[5];
        private DateTime lastAudioPacketUtc = DateTime.MinValue;
        private readonly Rectangle[] compactWaveBars;
        private readonly Rectangle[] globalWaveBars;
        private SolidColorBrush? islandBackground;
        private SolidColorBrush? islandBorderBrush;
        private DropShadowEffect? islandGlow;
        private Color albumAccentColor = Color.FromRgb(184, 146, 239);
        private SolidColorBrush? albumAccentBrush;

        // Apple-style Control Centre state.
        private readonly ObservableCollection<WifiNetworkItem> wifiNetworks = new();
        private readonly ObservableCollection<BluetoothDeviceItem> bluetoothDevices = new();
        private DeviceWatcher? bluetoothWatcher;
        private Radio? wifiRadio;
        private Radio? bluetoothRadio;
        private bool radioAccessGranted = false;
        private bool radioToggleInProgress = false;
        private MMDeviceEnumerator? audioEnumerator;
        private MMDevice? audioEndpoint;
        private readonly DispatcherTimer brightnessDebounceTimer;
        private readonly DispatcherTimer connectionStatusTimer;
        
private readonly DispatcherTimer volumeUpdateTimer;

private bool isSettingBrightness = false;
private bool hasPendingBrightness = false;
private bool isSyncingBrightness = false;

private int pendingBrightness = 0;
private int lastAppliedBrightness = -1;

private double pendingVolume = 0;

        private bool isInitializingQuickSettings = true;

        public MainWindow()
        {
            InitializeComponent();
            // Expose the development instance to native UI inspection tools.
            // Normal desktop-island launches remain absent from the taskbar.
            if (Environment.GetCommandLineArgs().Contains("--devtools")) ShowInTaskbar = true;

            SourceInitialized += (_, _) => InitializeClipboardMonitor();

            pages = new Grid[]
            {
                PageMusic,
                PageSystem,
                PageCalendar,
                PageQuickSettings,
                PageNotifications,
                PageDevices
            };

            pageDots = new Border[]
            {
                PageDot0,
                PageDot1,
                PageDot2,
                PageDot3,
                PageDot4,
                PageDot5
            };

            compactWaveBars = new Rectangle[]
            {
                CompactWaveBar1,
                CompactWaveBar2,
                CompactWaveBar3,
                CompactWaveBar4,
                CompactWaveBar5
            };

            globalWaveBars = new Rectangle[]
            {
                GlobalWaveBar1,
                GlobalWaveBar2,
                GlobalWaveBar3,
                GlobalWaveBar4,
                GlobalWaveBar5
            };

            WifiNetworksList.ItemsSource = wifiNetworks;
            BluetoothListBox.ItemsSource = bluetoothDevices;
            albumAccentBrush = new SolidColorBrush(Colors.White);

            brightnessDebounceTimer = new DispatcherTimer
{
    Interval = TimeSpan.FromMilliseconds(120)
};

brightnessDebounceTimer.Tick += BrightnessDebounceTimer_Tick;
connectionStatusTimer = new DispatcherTimer
{
    Interval = TimeSpan.FromSeconds(3)
};

connectionStatusTimer.Tick += async (_, _) =>
{
    await RefreshConnectionStatusAsync();
};
this.Closed += (_, _) =>
{
    connectionStatusTimer.Stop();
};

// Volume updates are throttled separately.
volumeUpdateTimer = new DispatcherTimer
{
    Interval = TimeSpan.FromMilliseconds(40)
};

volumeUpdateTimer.Tick += VolumeUpdateTimer_Tick;

            // Expand after a short hover delay.
            expandTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(55)
            };
            expandTimer.Tick += (s, e) =>
            {
                expandTimer.Stop();
                if (IsMouseOver && !isExpanded)
                    AnimateIsland(true);
            };

            // Collapse only after the pointer has left for a moment.
            collapseTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(55)
            };
            collapseTimer.Tick += (s, e) =>
            {
                collapseTimer.Stop();
                if (!IsMouseOver && isExpanded)
                    AnimateIsland(false);
            };

            // Media metadata and timeline refresh.
            mediaTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            mediaTimer.Tick += MediaTimer_Tick;

            // Smooth UI-side audio meter, decays to zero during silence.
            audioUiTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            audioUiTimer.Tick += (s, e) => UpdateAudioVisuals();

            // Clock page.
            clockTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            clockTimer.Tick += ClockTimer_Tick;
        }

        // Initial window setup
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Width = CompactWidth;
            Height = CompactHeight;
            CenterIsland();

            await InitializeWebViewAsync();
            if (isClosed) return;

            // Native fallback shares the album accent with the web island.
            islandBackground = IslandShell.Background as SolidColorBrush
                ?? new SolidColorBrush(Color.FromRgb(10, 10, 13));
            IslandShell.Background = islandBackground;

            islandBorderBrush = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255));
            IslandShell.BorderBrush = islandBorderBrush;

            islandGlow = new DropShadowEffect
            {
                Color = Colors.White,
                BlurRadius = 14,
                ShadowDepth = 0,
                Opacity = 0.12
            };
            IslandShell.Effect = islandGlow;

            UpdateNavigationDots();
            UpdateClock();
            clockTimer.Start();
            InitializeSystemMonitor();
            InitializeWeather();
            InitializeAiMonitor();
            RefreshHotspotState();
            await RefreshNotificationsAsync();
            if (isClosed) return;
            InitializeQuickSettingsControls();
            await InitializeRadiosAsync();
            if (isClosed) return;
            await RefreshConnectionStatusAsync();
            if (isClosed) return;
connectionStatusTimer.Start();

            await InitializeMediaAsync();
            if (isClosed) return;
            mediaTimer.Start();

            StartAudioCapture();
            audioUiTimer.Start();
        }

        private void CenterIsland()
        {
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = TopMargin;
        }

        // Hover interaction
        private void Window_MouseEnter(object sender, MouseEventArgs e)
        {
            if (webReady) return; // HTML owns hover; transparent glow is not a target.
            collapseTimer.Stop();
            if (!isExpanded)
                expandTimer.Start();
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            if (webReady) { CheckWebPointer(); return; }
            expandTimer.Stop();
            if (isExpanded)
                collapseTimer.Start();
        }

        private void CloseIsland_Click(object sender, RoutedEventArgs e)
        {
            collapseTimer.Stop();
            AnimateIsland(false);
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (webReady) return;
            if (e.Key == Key.Escape && isExpanded)
            {
                AnimateIsland(false);
                e.Handled = true;
            }
        }

        // Vertical mouse-wheel paging: wheel down = next page; wheel up = previous.
        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (webReady) return;
            if (!isExpanded || isPageAnimating)
                return;

            // Let sliders and scrollable controls handle their own scrolling.
            DependencyObject? source =e.OriginalSource as DependencyObject;

            while (source != null){
            if (source is Slider ||source is ScrollViewer ||source is ListBox)
            {
                return;
                }
            source = VisualTreeHelper.GetParent(source);
            }

            // Let the Wi-Fi and Bluetooth lists consume the wheel for their own scrolling.
            if (WifiPickerPanel.Visibility == Visibility.Visible ||
                BluetoothPickerPanel.Visibility == Visibility.Visible)
                return;

            int target = currentPageIndex + (e.Delta < 0 ? 1 : -1);
            if (target >= 0 && target < pages.Length)
                NavigateToPage(target);

            e.Handled = true;
        }

        private void PageDot_Click(object sender, RoutedEventArgs e)
        {
            if (!isExpanded || isPageAnimating)
                return;

            if (sender is Button button &&
                int.TryParse(button.Tag?.ToString(), out int target))
            {
                NavigateToPage(target);
            }
        }

        private void NavigateToPage(int targetIndex)
        {
            if (webReady)
            {
                if (targetIndex < 0 || targetIndex >= pageTitles.Length || targetIndex == currentPageIndex)
                    return;
                if (currentPageIndex < pages.Length)
                    pages[currentPageIndex].Visibility = Visibility.Collapsed;
                currentPageIndex = targetIndex;
                if (currentPageIndex < pages.Length)
                    pages[currentPageIndex].Visibility = Visibility.Visible;
                PageTitleText.Text = pageTitles[currentPageIndex];
                if (currentPageIndex == 1) _ = RefreshSystemAsync();
                if (currentPageIndex == 6) _ = RefreshAiAsync();
                return;
            }

            if (targetIndex < 0 || targetIndex >= pages.Length ||
                targetIndex == currentPageIndex || isPageAnimating)
                return;

            isPageAnimating = true;

            int direction = targetIndex > currentPageIndex ? 1 : -1;
            Grid oldPage = pages[currentPageIndex];
            Grid newPage = pages[targetIndex];

            newPage.Visibility = Visibility.Visible;
            newPage.Opacity = 0;

            var oldTransform = new TranslateTransform(0, 0);
            var newTransform = new TranslateTransform(0, direction * 22);
            oldPage.RenderTransform = oldTransform;
            newPage.RenderTransform = newTransform;

            var duration = TimeSpan.FromMilliseconds(210);
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

            oldPage.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, 0, duration) { EasingFunction = easing });
            oldTransform.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, -direction * 18, duration) { EasingFunction = easing });
            newPage.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, duration) { EasingFunction = easing });
            newTransform.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(direction * 22, 0, duration) { EasingFunction = easing });

            currentPageIndex = targetIndex;
            PageTitleText.Text = pageTitles[currentPageIndex];
            UpdateNavigationDots();

            var finish = new DispatcherTimer { Interval = duration };
            finish.Tick += (s, e) =>
            {
                finish.Stop();
                oldPage.BeginAnimation(OpacityProperty, null);
                oldTransform.BeginAnimation(TranslateTransform.YProperty, null);
                newPage.BeginAnimation(OpacityProperty, null);
                newTransform.BeginAnimation(TranslateTransform.YProperty, null);

                oldPage.Visibility = Visibility.Collapsed;
                oldPage.Opacity = 1;
                oldPage.RenderTransform = Transform.Identity;
                newPage.Visibility = Visibility.Visible;
                newPage.Opacity = 1;
                newPage.RenderTransform = Transform.Identity;
                isPageAnimating = false;
            };
            finish.Start();
        }

        private void UpdateNavigationDots()
        {
            for (int i = 0; i < pageDots.Length; i++)
            {
                bool active = i == currentPageIndex;
                pageDots[i].Width = 6;
                pageDots[i].Height = active ? 14 : 6;
                Color dotColor = active ? albumAccentColor : Colors.White;
                pageDots[i].Background = new SolidColorBrush(
                    Color.FromArgb(active ? (byte)255 : (byte)95, dotColor.R, dotColor.G, dotColor.B));
            }
        }

        // Window expansion and collapse
        private void AnimateIsland(bool expand)
        {
            if (webReady) { AnimateWebIsland(expand); return; }
            if (isAnimating)
            {
                pendingExpansion = expand;
                return;
            }
            if (isClosed || isExpanded == expand)
                return;

            isAnimating = true;
            isExpanded = expand;
            PublishWebState();
            expandTimer.Stop();
            collapseTimer.Stop();

            double startWidth = ActualWidth > 0 ? ActualWidth : Width;
            double startHeight = ActualHeight > 0 ? ActualHeight : Height;
            double startLeft = Left;
            double targetWidth = expand ? ExpandedWidth : CompactWidth;
            double targetHeight = expand ? ExpandedHeight : CompactHeight;
            double targetLeft = (SystemParameters.PrimaryScreenWidth - targetWidth) / 2;

            if (expand)
            {
                CompactView.Visibility = Visibility.Collapsed;
                ExpandedView.Visibility = Visibility.Visible;
                ExpandedView.Opacity = 0;
                PageTitleText.Text = pageTitles[currentPageIndex];
                UpdateNavigationDots();
            }
            else
            {
                CompactView.Visibility = Visibility.Visible;
                CompactView.Opacity = 0;
            }

            var duration = TimeSpan.FromMilliseconds(135);
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

            var widthAnimation = new DoubleAnimation(startWidth, targetWidth, duration)
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop
            };
            var heightAnimation = new DoubleAnimation(startHeight, targetHeight, duration)
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop
            };
            var leftAnimation = new DoubleAnimation(startLeft, targetLeft, duration)
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop
            };

            if (expand)
            {
                ExpandedView.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(95)));
            }
            else
            {
                ExpandedView.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(80)));
                CompactView.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(90)));
            }

            widthAnimation.Completed += (s, e) =>
            {
                BeginAnimation(WidthProperty, null);
                BeginAnimation(HeightProperty, null);
                BeginAnimation(LeftProperty, null);
                Width = targetWidth;
                Height = targetHeight;
                Left = targetLeft;

                if (expand)
                {
                    CompactView.Visibility = Visibility.Collapsed;
                    ExpandedView.Visibility = Visibility.Visible;
                    ExpandedView.Opacity = 1;
                }
                else
                {
                    ExpandedView.Visibility = Visibility.Collapsed;
                    ExpandedView.Opacity = 0;
                    CompactView.Visibility = Visibility.Visible;
                    CompactView.Opacity = 1;
                }

                isExpanded = expand;
                isAnimating = false;
                if (pendingExpansion is bool pending)
                {
                    pendingExpansion = null;
                    AnimateIsland(pending);
                }
            };

            BeginAnimation(WidthProperty, widthAnimation);
            BeginAnimation(HeightProperty, heightAnimation);
            BeginAnimation(LeftProperty, leftAnimation);
        }

        // Media initialization
        private async Task InitializeMediaAsync()
        {
            try
            {
                mediaManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                await RefreshMediaAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Media initialization error: " + ex);
                ShowNoMedia();
            }
        }

        private async void MediaTimer_Tick(object? sender, EventArgs e)
        {
            await RefreshMediaAsync();
        }

        private async Task RefreshMediaAsync()
        {
            if (isRefreshingMedia)
                return;

            isRefreshingMedia = true;
            try
            {
                mediaManager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                currentSession = mediaManager.GetCurrentSession();

                if (currentSession == null)
                {
                    ShowNoMedia();
                    return;
                }

                var properties = await currentSession.TryGetMediaPropertiesAsync();
                if (properties == null)
                {
                    ShowNoMedia();
                    return;
                }

                string title = string.IsNullOrWhiteSpace(properties.Title)
                    ? "Unknown title" : properties.Title;
                string artist = string.IsNullOrWhiteSpace(properties.Artist)
                    ? "Unknown artist" : properties.Artist;
                string album = string.IsNullOrWhiteSpace(properties.AlbumTitle)
                    ? currentSession.SourceAppUserModelId : properties.AlbumTitle;

                CompactTitle.Text = title;
                CompactArtist.Text = artist;
                ExpandedTitle.Text = title;
                ExpandedArtist.Text = artist;
                ExpandedAlbum.Text = album;

                var playback = currentSession.GetPlaybackInfo();
                mediaIsPlaying = playback.PlaybackStatus ==
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                SetPlaybackButtonIcon(mediaIsPlaying);
                PlayPauseButton.IsEnabled = true;

                string mediaKey = currentSession.SourceAppUserModelId + "|" +
                                  title + "|" + artist + "|" + album;

                bool mediaChanged = mediaKey != lastMediaKey;
                if (mediaChanged || CompactAlbumArt.Source == null)
                {
                    lastMediaKey = mediaKey;
                    CompactAlbumArt.Source = null;
                    ExpandedAlbumArt.Source = null;
                    CompactArtPlaceholder.Visibility = Visibility.Visible;
                    ExpandedArtPlaceholder.Visibility = Visibility.Visible;

                    BitmapImage? artwork = await LoadAlbumArtAsync(properties.Thumbnail);
                    if (artwork != null)
                    {
                        CompactAlbumArt.Source = artwork;
                        ExpandedAlbumArt.Source = artwork;
                        CompactArtPlaceholder.Visibility = Visibility.Collapsed;
                        ExpandedArtPlaceholder.Visibility = Visibility.Collapsed;
                        UpdateAlbumAccent(artwork);
                    }
                    else
                    {
                        ResetAlbumAccent();
                    }
                }

                UpdateTimeline(currentSession.GetTimelineProperties());

                if (mediaChanged)
                    _ = LoadLyricsAsync(artist: artist, title: title, mediaKey: mediaKey);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Media refresh error: " + ex);
            }
            finally
            {
                isRefreshingMedia = false;
            }
        }

        private async Task<BitmapImage?> LoadAlbumArtAsync(IRandomAccessStreamReference? thumbnail)
        {
            try
            {
                if (thumbnail == null)
                    return null;

                using var stream = await thumbnail.OpenReadAsync();
                if (stream.Size == 0 || stream.Size > 15_000_000)
                    return null;

                using var reader = new DataReader(stream.GetInputStreamAt(0));
                uint size = checked((uint)stream.Size);
                await reader.LoadAsync(size);
                byte[] bytes = new byte[(int)size];
                reader.ReadBytes(bytes);

                using var memory = new MemoryStream(bytes);
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 256;
                image.StreamSource = memory;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Album artwork error: " + ex.Message);
                return null;
            }
        }

        private void ResetAlbumAccent()
        {
            albumAccentColor = Color.FromRgb(184, 146, 239);
            albumAccentBrush = new SolidColorBrush(albumAccentColor);
            ApplyAlbumAccentToUi();
        }

        private void ApplyAlbumAccentToUi()
        {
            if (albumAccentBrush == null) return;
            PageTitleText.Foreground = albumAccentBrush;
            CurrentLyricText.Foreground = albumAccentBrush;
            SeekSlider.Foreground = albumAccentBrush;
            foreach (var bar in compactWaveBars) bar.Fill = albumAccentBrush;
            foreach (var bar in globalWaveBars) bar.Fill = albumAccentBrush;
            if (islandBorderBrush != null)
                islandBorderBrush.Color = Color.FromArgb(105, albumAccentColor.R, albumAccentColor.G, albumAccentColor.B);
            if (islandGlow != null)
            {
                islandGlow.Color = albumAccentColor;
                islandGlow.Opacity = 0.20;
            }
            if (islandBackground != null)
                islandBackground.Color = Color.FromRgb(
                    BlendChannel(9, albumAccentColor.R, 0.10),
                    BlendChannel(9, albumAccentColor.G, 0.10),
                    BlendChannel(12, albumAccentColor.B, 0.10));
            UpdateNavigationDots();
        }

        private void SetPlaybackButtonIcon(bool playing)
        {
            string geometry = playing
                ? "M 6,4 H 10 V 20 H 6 Z M 14,4 H 18 V 20 H 14 Z"
                : "M 7,3 L 21,12 L 7,21 Z";
            PlayPauseButton.Content = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(geometry),
                Fill = Brushes.Black,
                Width = 20,
                Height = 20,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private void UpdateTimeline(GlobalSystemMediaTransportControlsSessionTimelineProperties timeline)
        {
            TimeSpan duration = timeline.EndTime - timeline.StartTime;
            TimeSpan position = timeline.Position - timeline.StartTime;

            if (duration <= TimeSpan.Zero)
            {
                SeekSlider.IsEnabled = false;
                PositionText.Text = "0:00";
                DurationText.Text = "--:--";
                return;
            }

            if (position < TimeSpan.Zero)
                position = TimeSpan.Zero;
            if (position > duration)
                position = duration;

            SeekSlider.Maximum = duration.TotalSeconds;
            SeekSlider.IsEnabled = true;

            if (!isSeeking)
                SeekSlider.Value = Math.Min(position.TotalSeconds, SeekSlider.Maximum);

            PositionText.Text = FormatTime(position);
            DurationText.Text = FormatTime(duration);
            UpdateCurrentLyric(position);
        }

        private static string FormatTime(TimeSpan time)
        {
            if (time.TotalHours >= 1)
                return $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}";
            return $"{(int)time.TotalMinutes}:{time.Seconds:00}";
        }

        private void SeekSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            isSeeking = true;
        }

        private async void SeekSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!isSeeking)
                return;

            isSeeking = false;
            await SeekToPositionAsync();
        }

        private async Task SeekToPositionAsync()
        {
            if (currentSession == null || !SeekSlider.IsEnabled)
                return;

            try
            {
                var timeline = currentSession.GetTimelineProperties();
                long requestedTicks = timeline.StartTime.Ticks +
                    TimeSpan.FromSeconds(SeekSlider.Value).Ticks;
                bool success = await currentSession.TryChangePlaybackPositionAsync(requestedTicks);
                if (!success)
                    Debug.WriteLine("The media session did not accept the seek request.");
                await RefreshMediaAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Seek error: " + ex.Message);
            }
        }

        private async void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            var session = currentSession;
            if (session == null)
                return;

            try
            {
                await session.TryTogglePlayPauseAsync();
                await RefreshMediaAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Play/pause error: " + ex.Message);
            }
        }

        private async void Previous_Click(object sender, RoutedEventArgs e)
        {
            var session = currentSession;
            if (session == null)
                return;

            try
            {
                await session.TrySkipPreviousAsync();
                await Task.Delay(350);
                await RefreshMediaAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Previous-track error: " + ex.Message);
            }
        }

        private async void Next_Click(object sender, RoutedEventArgs e)
        {
            var session = currentSession;
            if (session == null)
                return;

            try
            {
                await session.TrySkipNextAsync();
                await Task.Delay(350);
                await RefreshMediaAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Next-track error: " + ex.Message);
            }
        }

        private void ShowNoMedia()
        {
            currentSession = null;
            mediaIsPlaying = false;
            lastMediaKey = string.Empty;

            CompactTitle.Text = "Nothing playing";
            CompactArtist.Text = "Nothing playing";
            ExpandedTitle.Text = "Nothing playing";
            ExpandedArtist.Text = "Start music on your PC";
            ExpandedAlbum.Text = "Media player";
            CurrentLyricText.Text = "Lyrics will appear here when available";

            CompactAlbumArt.Source = null;
            ExpandedAlbumArt.Source = null;
            CompactArtPlaceholder.Visibility = Visibility.Visible;
            ExpandedArtPlaceholder.Visibility = Visibility.Visible;

            SetPlaybackButtonIcon(false);
            PlayPauseButton.IsEnabled = false;
            ResetAlbumAccent();
            ClearLyrics();
            SeekSlider.IsEnabled = false;
            SeekSlider.Value = 0;
            PositionText.Text = "0:00";
            DurationText.Text = "--:--";
        }

        // Start system-wide output capture. Audio is analysed in memory only.
        private void StartAudioCapture()
        {
            try
            {
                audioRecorder = new WasapiRecorderBuilder()
                    .WithLoopbackCapture()
                    .WithFormat(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2))
                    .Build();

                audioRecorder.DataAvailable += (buffer, flags, devicePosition, qpcPosition) =>
                {
                    ProcessAudioBuffer(buffer);
                };

                audioRecorder.StartRecording();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Audio loopback could not start: " + ex);
                // Media controls and pages still work if audio capture is unavailable.
            }
        }

        // Calculate five RMS bands from float32 system-output samples.
        private void ProcessAudioBuffer(ReadOnlySpan<byte> buffer)
        {
            const int bytesPerSample = 4;
            int sampleCount = buffer.Length / bytesPerSample;
            if (sampleCount <= 0)
                return;

            double[] sums = new double[5];
            int[] counts = new int[5];

            for (int i = 0; i < sampleCount; i++)
            {
                float sample = BitConverter.ToSingle(buffer.Slice(i * bytesPerSample, bytesPerSample));
                if (!float.IsFinite(sample))
                    continue;

                int band = Math.Min(4, (i * 5) / sampleCount);
                sums[band] += sample * sample;
                counts[band]++;
            }

            double[] levels = new double[5];
            for (int i = 0; i < 5; i++)
            {
                double rms = counts[i] > 0 ? Math.Sqrt(sums[i] / counts[i]) : 0;
                levels[i] = Math.Clamp(rms * 4.0, 0.0, 1.0);
            }

            lock (audioLock)
            {
                Array.Copy(levels, targetAudioBars, 5);
                lastAudioPacketUtc = DateTime.UtcNow;
            }
        }

        // Runs on the UI thread. It updates both waveforms and the whole shell,
        // independent of the currently selected page.
        private void UpdateAudioVisuals()
        {
            double[] targets = new double[5];
            DateTime lastPacket;
            lock (audioLock)
            {
                Array.Copy(targetAudioBars, targets, 5);
                lastPacket = lastAudioPacketUtc;
            }

            bool audioIsFresh = lastPacket != DateTime.MinValue &&
                (DateTime.UtcNow - lastPacket).TotalMilliseconds < 220;
            if (!audioIsFresh)
                Array.Clear(targets, 0, targets.Length);

            double strongest = 0;
            for (int i = 0; i < 5; i++)
            {
                displayedAudioBars[i] += (targets[i] - displayedAudioBars[i]) * 0.38;
                strongest = Math.Max(strongest, displayedAudioBars[i]);
                if (webReady) continue;
                double height = 4 + displayedAudioBars[i] * 20;
                compactWaveBars[i].Height = height;
                globalWaveBars[i].Height = 3 + displayedAudioBars[i] * 14;
                compactWaveBars[i].Opacity = 0.35 + displayedAudioBars[i] * 0.65;
                globalWaveBars[i].Opacity = 0.35 + displayedAudioBars[i] * 0.65;
            }

            SendWebMessage(new { type = "audio", data = new { levels = displayedAudioBars, peak = strongest } });

            if (webReady) return; // Do not repaint the hidden native shadow at 30 Hz.

            if (islandBackground == null || islandBorderBrush == null || islandGlow == null)
                return;

            // Whole-shell response: album tint is present while playing; audio level
            // increases its strength. This runs independently of the selected page.
            double tint = 0.015 + strongest * 0.12;
            Color baseColor = Color.FromRgb(9, 9, 12);
            Color accent = albumAccentColor;
            islandBackground.Color = Color.FromRgb(
                BlendChannel(baseColor.R, accent.R, tint),
                BlendChannel(baseColor.G, accent.G, tint),
                BlendChannel(baseColor.B, accent.B, tint));

            byte borderAlpha = (byte)Math.Clamp(45 + strongest * 150, 45, 195);
            islandBorderBrush.Color = Color.FromArgb(borderAlpha,
                albumAccentColor.R, albumAccentColor.G, albumAccentColor.B);
            islandGlow.Color = albumAccentColor;
            islandGlow.BlurRadius = 12 + strongest * 26;
            islandGlow.Opacity = 0.08 + strongest * 0.48;
        }

        private static byte BlendChannel(byte from, byte to, double amount)
        {
            amount = Math.Clamp(amount, 0, 1);
            return (byte)Math.Clamp((int)Math.Round(from + (to - from) * amount), 0, 255);
        }

        // Clock page
        private void ClockTimer_Tick(object? sender, EventArgs e) => UpdateClock();

        private void UpdateClock()
        {
            if (ClockText == null || DateText == null)
                return;

            DateTime now = DateTime.Now;
            ClockText.Text = now.ToString("h:mm:ss tt");
            DateText.Text = now.ToString("dddd, dd MMMM yyyy");
        }

        // ================= APPLE-STYLE QUICK SETTINGS =================

        private void InitializeQuickSettingsControls()
        {
            isInitializingQuickSettings = true;
            try
            {
                audioEnumerator = new MMDeviceEnumerator();
                audioEndpoint = audioEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                audioEndpoint.AudioEndpointVolume.OnVolumeNotification +=AudioEndpointVolume_OnVolumeNotification;
                VolumeSlider.Value = Math.Round(audioEndpoint.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
                VolumeValueText.Text = $"{VolumeSlider.Value:0}%";
            }
            catch (Exception ex)
            {
                VolumeSlider.IsEnabled = false;
                Debug.WriteLine("Could not initialize system volume: " + ex.Message);
            }

            try
            {
                int? brightness = ReadDisplayBrightness();
                if (brightness.HasValue)
                {
                    BrightnessSlider.Value = brightness.Value;
                    BrightnessValueText.Text = $"{brightness.Value}%";
                }
                else
                {
                    BrightnessSlider.IsEnabled = false;
                    BrightnessValueText.Text = "Display";
                }
            }
            catch (Exception ex)
            {
                BrightnessSlider.IsEnabled = false;
                BrightnessValueText.Text = "Display";
                Debug.WriteLine("Could not read display brightness: " + ex.Message);
            }
            finally
            {
                isInitializingQuickSettings = false;
            }

            _ = RefreshCurrentWifiNameAsync();
        }

        

private void VolumeSlider_ValueChanged(
    object sender,
    RoutedPropertyChangedEventArgs<double> e)
{
    if (VolumeValueText == null)
        return;

    // Update the label immediately.
    VolumeValueText.Text = $"{e.NewValue:0}%";

    if (isInitializingQuickSettings ||
        audioEndpoint == null)
        return;

    pendingVolume = e.NewValue;

    if (!volumeUpdateTimer.IsEnabled)
        volumeUpdateTimer.Start();
}

private void VolumeUpdateTimer_Tick(
    object? sender,
    EventArgs e)
{
    volumeUpdateTimer.Stop();

            if (isClosed || audioEndpoint == null)
                return;

    try
    {
        audioEndpoint.AudioEndpointVolume.MasterVolumeLevelScalar =
            (float)(pendingVolume / 100.0);

        // Moving the slider automatically unmutes audio.
        audioEndpoint.AudioEndpointVolume.Mute = false;
    }
    catch (Exception ex)
    {
        Debug.WriteLine(
            "Volume update error: " + ex.Message);
    }
}

private void AudioEndpointVolume_OnVolumeNotification(AudioVolumeNotificationData data)
{
    Dispatcher.BeginInvoke(new Action(() =>
    {
        if (audioEndpoint == null)
            return;

        try
        {
            double actualVolume =
                audioEndpoint.AudioEndpointVolume
                    .MasterVolumeLevelScalar * 100;

            // Don't fight the user's hand while they're dragging.
            if (!VolumeSlider.IsMouseCaptureWithin && !volumeUpdateTimer.IsEnabled)
            {
                isInitializingQuickSettings = true;
                try { VolumeSlider.Value = actualVolume; }
                finally { isInitializingQuickSettings = false; }
            }

            VolumeValueText.Text = $"{actualVolume:0}%";
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Volume update error: " + ex.Message);
        }
    }));
}


        
private void BrightnessSlider_ValueChanged(
    object sender,
    RoutedPropertyChangedEventArgs<double> e)
{
    if (BrightnessValueText == null)
        return;

    // Update the displayed percentage immediately.
    BrightnessValueText.Text = $"{e.NewValue:0}%";

    if (isInitializingQuickSettings ||
        isSyncingBrightness ||
        !BrightnessSlider.IsEnabled)
        return;

    // Store the latest requested brightness.
    pendingBrightness = (int)Math.Round(e.NewValue);

    hasPendingBrightness = true;

    // Start a timer only if one isn't already running.
    // This is throttling, not a repeated stop/start debounce.
    if (!isSettingBrightness &&
        !brightnessDebounceTimer.IsEnabled)
    {
        brightnessDebounceTimer.Start();
    }
}


        


private async void BrightnessDebounceTimer_Tick(
    object? sender,
    EventArgs e)
{
    brightnessDebounceTimer.Stop();

    if (!hasPendingBrightness || isSettingBrightness)
        return;

    int requestedBrightness = pendingBrightness;

    hasPendingBrightness = false;

    if (requestedBrightness == lastAppliedBrightness)
        return;

    isSettingBrightness = true;

    try
    {
        // Send the request in the background.
        bool success = await Task.Run(
            () => SetDisplayBrightness(requestedBrightness)
        );

        if (success)
        {
            lastAppliedBrightness = requestedBrightness;

            BrightnessValueText.Text =
                $"{requestedBrightness}%";
        }
        else
        {
            // Read the actual display value if the request fails.
            int? actualBrightness =
                await Task.Run(ReadDisplayBrightness);

            if (actualBrightness.HasValue)
            {
                isSyncingBrightness = true;

                BrightnessSlider.Value =
                    actualBrightness.Value;

                isSyncingBrightness = false;

                BrightnessValueText.Text =
                    $"{actualBrightness.Value}%";

                lastAppliedBrightness =
                    actualBrightness.Value;
            }
            else
            {
                BrightnessValueText.Text = "Unavailable";
            }
        }
    }
    catch (Exception ex)
    {
        isSyncingBrightness = false;

        Debug.WriteLine(
            "Brightness update error: " + ex.Message);
    }
    finally
    {
        isSettingBrightness = false;

        // Apply the newest value if the user moved the slider
        // while the previous hardware update was running.
        if (hasPendingBrightness && !isClosed)
            brightnessDebounceTimer.Start();
    }
}



       private static int? ReadDisplayBrightness()
{
    using var searcher = new ManagementObjectSearcher(
        @"root\WMI",
        "SELECT * FROM WmiMonitorBrightness"
    );

    using var results = searcher.Get();

    foreach (ManagementObject monitor in results)
    {
        using (monitor)
        {
            if (monitor["Active"] is bool active && active)
            {
                return Convert.ToInt32(
                    monitor["CurrentBrightness"]
                );
            }
        }
    }

    return null;
}
        



private static bool SetDisplayBrightness(int brightness)
{
    byte target = (byte)Math.Clamp(brightness, 0, 100);

    try
    {
        using var searcher = new ManagementObjectSearcher(
            @"root\WMI",
            "SELECT * FROM WmiMonitorBrightnessMethods"
        );

        using var results = searcher.Get();

        foreach (ManagementObject monitor in results)
        {
            using (monitor)
            {
                if (monitor["Active"] is not bool active || !active)
                    continue;

                // Invoke Windows brightness control directly.
                // The actual display update is handled by Windows.

                object? result = monitor.InvokeMethod(
                    "WmiSetBrightness",
                    new object[] { (uint)1, target }
                );

                if (result is ManagementBaseObject output)
                    output.Dispose();
                if (result is uint status && status != 0) return false;

                // The WMI provider may return an empty result
                // even when the brightness change succeeds.
                return true;
            }
        }

        return false;
    }
    catch (Exception ex)
    {
        Debug.WriteLine(
            "Brightness hardware error: " + ex.Message);

        return false;
    }
}


        private void WifiTile_Click(object sender, RoutedEventArgs e)
        {
            QuickSettingsHome.Visibility = Visibility.Collapsed;
            BluetoothPickerPanel.Visibility = Visibility.Collapsed;
            WifiPickerPanel.Visibility = Visibility.Visible;
            _ = RefreshWifiNetworksAsync();
        }

        private void BackFromWifi_Click(object sender, RoutedEventArgs e)
        {
            WifiPickerPanel.Visibility = Visibility.Collapsed;
            QuickSettingsHome.Visibility = Visibility.Visible;
        }

        private async void RefreshWifi_Click(object sender, RoutedEventArgs e)
        {
            await RefreshWifiNetworksAsync();
        }

        private async Task RefreshWifiNetworksAsync(bool throwOnError = false)
        {
            if (wifiRadio != null &&
    wifiRadio.State != RadioState.On)
{
    WifiStatusText.Text = "Wi-Fi is turned off.";
    WifiCurrentSsidText.Text = "Not connected.";
    wifiNetworks.Clear();
    if (throwOnError) throw new InvalidOperationException("Wi-Fi is turned off.");
    return;
}
            WifiStatusText.Text = "Scanning nearby networks…";
            WifiNetworksList.SelectedItem = null;
            WifiPasswordBox.Visibility = Visibility.Collapsed;
            WifiConnectButton.IsEnabled = false;
            wifiNetworks.Clear();

            try
            {
                var found = await Services.WifiService.ScanAsync();
                foreach (var network in found)
                    wifiNetworks.Add(new WifiNetworkItem(network.Ssid, network.Security, (int)network.Signal, network.Secure)
                    { Source = network });

                WifiStatusText.Text = wifiNetworks.Count == 0
                    ? "No networks found. Check Wi-Fi and Location permissions."
                    : $"{wifiNetworks.Count} networks nearby";

                await RefreshCurrentWifiNameAsync();
            }
            catch (Exception ex)
            {
                WifiStatusText.Text = ex.Message;
                Debug.WriteLine("Wi-Fi scan failed: " + ex);
                if (throwOnError) throw;
            }
        }

        private void WifiNetworksList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (WifiNetworksList.SelectedItem is not WifiNetworkItem selected)
            {
                WifiPasswordBox.Visibility = Visibility.Collapsed;
                WifiConnectButton.IsEnabled = false;
                return;
            }

            WifiPasswordBox.Visibility = selected.IsSecure ? Visibility.Visible : Visibility.Collapsed;
            WifiPasswordBox.Password = string.Empty;
            WifiConnectButton.IsEnabled = true;
            WifiConnectButton.Content = "Join";
        }

        private async void ConnectWifi_Click(object sender, RoutedEventArgs e)
        {
            if (WifiNetworksList.SelectedItem is not WifiNetworkItem network) return;
            WifiConnectButton.IsEnabled = false;
            WifiStatusText.Text = $"Connecting to {network.Ssid}…";

            try
            {
                await ConnectToWifiAsync(network, WifiPasswordBox.Password);
            }
            catch (Exception ex)
            {
                WifiStatusText.Text = ex.Message;
                Debug.WriteLine("Wi-Fi connection error: " + ex);
            }
            finally
            {
                WifiConnectButton.IsEnabled = true;
                WifiPasswordBox.Clear();
            }
        }

        private void BluetoothTile_Click(object sender, RoutedEventArgs e)
        {
            QuickSettingsHome.Visibility = Visibility.Collapsed;
            WifiPickerPanel.Visibility = Visibility.Collapsed;
            BluetoothPickerPanel.Visibility = Visibility.Visible;
            _ = StartBluetoothDiscoveryAsync();
        }

        private void BackFromBluetooth_Click(object sender, RoutedEventArgs e)
        {
            StopBluetoothWatcher();
            BluetoothPickerPanel.Visibility = Visibility.Collapsed;
            QuickSettingsHome.Visibility = Visibility.Visible;
        }

        private async void RefreshBluetooth_Click(object sender, RoutedEventArgs e)
        {
            await StartBluetoothDiscoveryAsync();
        }

        private void BluetoothListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BluetoothListBox.SelectedItem is not BluetoothDeviceItem item)
            {
                PairBluetoothButton.IsEnabled = false;
                return;
            }
            PairBluetoothButton.IsEnabled = item.CanPair && !item.IsPaired;
            PairBluetoothButton.Content = item.IsPaired ? "Paired" : "Pair";
            BluetoothFooterText.Text = item.IsPaired
                ? "This device is already paired with Windows."
                : "Select Pair to continue with Windows confirmation.";
        }

        private async void PairBluetooth_Click(object sender, RoutedEventArgs e)
        {
            if (BluetoothListBox.SelectedItem is not BluetoothDeviceItem item) return;
            PairBluetoothButton.IsEnabled = false;
            BluetoothStatusText.Text = $"Pairing with {item.Name}…";
            try
            {
                var result = await item.Device.Pairing.PairAsync();
                BluetoothStatusText.Text = "Pairing: " + result.Status;
                await Task.Delay(800);
                await StartBluetoothDiscoveryAsync();
            }
            catch (Exception ex)
            {
                BluetoothStatusText.Text = "Pairing could not start.";
                BluetoothFooterText.Text = "Open Windows Bluetooth settings to finish pairing.";
                Debug.WriteLine("Bluetooth pairing: " + ex);
            }
            finally
            {
                PairBluetoothButton.IsEnabled = BluetoothListBox.SelectedItem is BluetoothDeviceItem selected && selected.CanPair && !selected.IsPaired;
            }
        }

        private void OpenSettingsPage(string uri, bool throwOnError = false)
        {
            try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
            catch (Exception ex)
            {
                Debug.WriteLine("Could not open Windows Settings: " + ex.Message);
                if (throwOnError) throw;
            }
        }

        

        public sealed class WifiNetworkItem
        {
            internal Services.WifiService.Network? Source { get; init; }
            public string Ssid { get; }
            public string Authentication { get; }
            public int SignalPercent { get; }
            public bool IsSecure { get; }
            public string SignalLabel => SignalPercent + "%";
            public string SecurityLabel => IsSecure ? Authentication : "Open network";
            public Visibility LockVisibility => IsSecure ? Visibility.Visible : Visibility.Collapsed;
            public WifiNetworkItem(string ssid, string authentication, int signal, bool secure)
            {
                Ssid = ssid; Authentication = authentication; SignalPercent = signal; IsSecure = secure;
            }
        }

        public sealed class BluetoothDeviceItem
        {
            public DeviceInformation Device { get; }
            public string Id => Device.Id;
            public string Name => string.IsNullOrWhiteSpace(Device.Name) ? "Bluetooth device" : Device.Name;
            public bool IsPaired => Device.Pairing.IsPaired;
            public bool CanPair => Device.Pairing.CanPair;
            public string StateLabel => IsPaired ? "Paired with Windows" : "Nearby device";
            public string ActionLabel => IsPaired ? "✓" : (CanPair ? "Pair" : "—");
            public BluetoothDeviceItem(DeviceInformation device) => Device = device;
        }

        // Stop timers and release the audio capture cleanly.
        private async void Window_Closed(object? sender, EventArgs e)
        {
            isClosed = true;
            systemTimer.Stop();
            webStateTimer.Stop();
            connectionStatusTimer.Stop();
            webReady = false;
            webView?.Dispose();
            StopClipboardMonitor();
            DisposeVoiceCommands();
            if (wifiRadio != null) wifiRadio.StateChanged -= Radio_StateChanged;
            if (bluetoothRadio != null) bluetoothRadio.StateChanged -= Radio_StateChanged;
            expandTimer.Stop();
            collapseTimer.Stop();
            mediaTimer.Stop();
            audioUiTimer.Stop();
            clockTimer.Stop();
            weatherTimer.Stop();
            brightnessDebounceTimer.Stop();
            volumeUpdateTimer.Stop();
            StopBluetoothWatcher();

            if (audioEndpoint != null)
            {
                audioEndpoint.AudioEndpointVolume.OnVolumeNotification -= AudioEndpointVolume_OnVolumeNotification;
                try { audioEndpoint.Dispose(); } catch { }
                audioEndpoint = null;
            }
            if (audioEnumerator != null)
            {
                try { audioEnumerator.Dispose(); } catch { }
                audioEnumerator = null;
            }

            if (audioRecorder != null)
            {
                try
                {
                    audioRecorder.StopRecording();
                    await audioRecorder.DisposeAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Audio capture shutdown: " + ex.Message);
                }
                finally
                {
                    audioRecorder = null;
                }
            }
        }



        
        // ==========================================
        // RADIO INITIALIZATION
        // ==========================================

        private async Task InitializeRadiosAsync()
        {
            try
            {
                var radios = await Radio.GetRadiosAsync();

                wifiRadio = radios.FirstOrDefault(
                    r => r.Kind == RadioKind.WiFi);

                bluetoothRadio = radios.FirstOrDefault(
                    r => r.Kind == RadioKind.Bluetooth);

                if (wifiRadio != null)
                {
                    wifiRadio.StateChanged += Radio_StateChanged;
                }

                if (bluetoothRadio != null)
                {
                    bluetoothRadio.StateChanged += Radio_StateChanged;
                }

                UpdateRadioTiles();

                if (wifiRadio?.State == RadioState.On)
                {
                    await RefreshCurrentWifiNameAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Radio initialization: " + ex);

                CurrentWifiText.Text = "Unavailable";
                BluetoothRadioStatusText.Text = "Unavailable";
            }
        }


        // ==========================================
        // RADIO STATE CHANGE EVENT
        // ==========================================

        private void Radio_StateChanged(Radio sender, object args)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateRadioTiles();

                _ = RefreshConnectionStatusAsync();
            }));
        }


        // ==========================================
        // UPDATE TILE APPEARANCE
        // ==========================================

        private void UpdateRadioTiles()
        {
            bool wifiOn = wifiRadio?.State == RadioState.On;
            bool bluetoothOn = bluetoothRadio?.State == RadioState.On;

            // Wi-Fi
            WifiRadioButton.Background =
                new SolidColorBrush(
                    wifiOn
                    ? Color.FromRgb(245, 245, 247)
                    : Color.FromRgb(32, 32, 38));

            WifiIconCircle.Background =
                new SolidColorBrush(
                    wifiOn
                    ? Color.FromRgb(35, 35, 42)
                    : Color.FromRgb(48, 48, 56));

            WifiRadioLabel.Foreground =
                wifiOn ? Brushes.Black : Brushes.White;

            if (wifiRadio == null)
                CurrentWifiText.Text = "Unavailable";
            else if (!wifiOn)
                CurrentWifiText.Text = "Off";

            // Bluetooth
            BluetoothRadioButton.Background =
                new SolidColorBrush(
                    bluetoothOn
                    ? Color.FromRgb(245, 245, 247)
                    : Color.FromRgb(32, 32, 38));

            BluetoothIconCircle.Background =
                new SolidColorBrush(
                    bluetoothOn
                    ? Color.FromRgb(35, 35, 42)
                    : Color.FromRgb(48, 48, 56));

            BluetoothRadioLabel.Foreground =
                bluetoothOn ? Brushes.Black : Brushes.White;

            BluetoothRadioStatusText.Foreground =
                bluetoothOn
                ? Brushes.Black
                : new SolidColorBrush(
                    Color.FromRgb(184, 184, 190));

            CurrentWifiText.Foreground = wifiOn ? Brushes.DimGray : Brushes.LightGray;
            if (bluetoothRadio == null) BluetoothRadioStatusText.Text = "Unavailable";
            else if (!bluetoothOn) BluetoothRadioStatusText.Text = "Off";
            WifiRadioButton.IsEnabled = wifiRadio != null && !radioToggleInProgress;
            BluetoothRadioButton.IsEnabled = bluetoothRadio != null && !radioToggleInProgress;
        }


        // ==========================================
        // WIFI TOGGLE BUTTON
        // ==========================================

        private async void WifiRadioToggle_Click(
            object sender,
            RoutedEventArgs e)
        {
            await ToggleRadioAsync(wifiRadio, "Wi-Fi");
        }


        // ==========================================
        // BLUETOOTH TOGGLE BUTTON
        // ==========================================

        private async void BluetoothRadioToggle_Click(
            object sender,
            RoutedEventArgs e)
        {
            await ToggleRadioAsync(bluetoothRadio, "Bluetooth");
        }


        // ==========================================
        // COMMON RADIO TOGGLE LOGIC
        // ==========================================

        private async Task ToggleRadioAsync(
            Radio? radio,
            string radioName)
        {
            try
            {
                await SetRadioStateAsync(radio, radio?.State != RadioState.On);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    radioName + " toggle error: " + ex);

                SetRadioStatus(radioName, ex.Message);
            }
        }


        // ==========================================
        // RADIO STATUS TEXT
        // ==========================================

        private void SetRadioStatus(
            string radioName,
            string message)
        {
            if (radioName == "Wi-Fi")
                CurrentWifiText.Text = message;
            else
                BluetoothRadioStatusText.Text = message;
        }

    }
}
