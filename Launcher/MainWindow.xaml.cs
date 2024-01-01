using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Launcher
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly string m_DownloadUrl = "https://pandaexpresswow.com/download/Client.zip";
        private readonly string m_DownloadPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Client.zip");
        private readonly string m_ExtractionPath = AppDomain.CurrentDomain.BaseDirectory;
        private IProgress<double> downloadProgress;
        private IProgress<double> extractionProgress;
        private ProgressBar YourProgressBar;

        private bool _isInstalling;
        public bool IsInstalling
        {
            get { return _isInstalling; }
            set
            {
                _isInstalling = value;
                OnPropertyChanged(nameof(IsInstalling));
                OnPropertyChanged(nameof(IsNotInstalling)); // Notify for the negation of IsInstalling
            }
        }

        public bool IsNotInstalling => !_isInstalling; // Computed property for negation

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private string _progressInfo;
        public string ProgressInfo
        {
            get { return _progressInfo; }
            set
            {
                _progressInfo = value;
                OnPropertyChanged(nameof(ProgressInfo));
            }
        }
        private double _progressValue;
        public double ProgressValue
        {
            get { return _progressValue; }
            set
            {
                _progressValue = value;
                OnPropertyChanged(nameof(ProgressValue));
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            CheckServerStatus("pandaexpresswow.centralus.cloudapp.azure.com", 8085);
            Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
            ContentRendered += MainWindow_ContentRendered;
            installBtn.Click += installBtn_Click;
            playBtn.Click += playBtn_Click;
            webBtn.Click += webBtn_Click;
            discBtn.Click += discBtn_Click;
            Application.Current.Exit += Application_Exit;
            MouseDown += Window_MouseDown;

            DataContext = this;
        }

        private void MainWindow_ContentRendered(object sender, EventArgs e)
        {
            // This event occurs after components are initialized and the window content is rendered
            CheckForInstall(); // Call CheckForInstall here
            CheckForGameUpdates();
            CheckForLauncherUpdates();

        }

        private async void CheckForLauncherUpdates()
        {
            using (HttpClient client = new HttpClient())
            {
                try
                {
                    string launcherVersionUrl = "https://pandaexpresswow.com/download/launcher-version.txt";
                    string localUpdaterPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "updater.exe");

                    DateTime serverLastModifiedDate = await GetLastModifiedDateTimeAsync(client, launcherVersionUrl);

                    DateTime localLastModifiedDate = File.GetLastWriteTime(localUpdaterPath);

                    if (serverLastModifiedDate > localLastModifiedDate)
                    {
                        // Display a message box on the UI thread
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            MessageBoxResult result = MessageBox.Show("A new launcher version is available.", "Update Available", MessageBoxButton.OKCancel, MessageBoxImage.Information);

                            if (result == MessageBoxResult.OK)
                            {
                                // User clicked OK, exit the application
                                Application.Current.Shutdown();

                                // Launch the local updater after the application is closed
                                Process.Start(localUpdaterPath);
                            }
                        });
                    }
                    else
                    {
                        Debug.WriteLine("Launcher is up to date.");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error checking for launcher updates: {ex.Message}");
                }
            }
        }

        private async Task<DateTime> GetLastModifiedDateTimeAsync(HttpClient client, string url)
        {
            var request = new HttpRequestMessage(HttpMethod.Head, url);
            var response = await client.SendAsync(request);

            if (response.Headers.TryGetValues("Last-Modified", out var values))
            {
                if (DateTime.TryParse(values.First(), out var lastModifiedDate))
                {
                    return lastModifiedDate;
                }
            }

            return DateTime.MinValue;
        }

        private void CheckForInstall()
        {
            string executableDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            string gameExecutablePath = Path.Combine(executableDirectory, "Arctium WoW Launcher.exe");

            // Explicitly set the working directory
            Directory.SetCurrentDirectory(executableDirectory);

            if (File.Exists(gameExecutablePath))
            {
                installBtn.Visibility = Visibility.Collapsed;
                playBtn.Visibility = Visibility.Visible;
                progressInfo.Visibility = Visibility.Visible;

            }
            else
            {
                Debug.WriteLine($"Game executable not found in directory: {executableDirectory}");
                installBtn.Visibility = Visibility.Visible;
                playBtn.Visibility = Visibility.Collapsed;
            }
        }

        private async void CheckForGameUpdates()
        {
            using (HttpClient client = new HttpClient())
            {
                try
                {
                    string versionUrl = "https://pandaexpresswow.com/download/game-version.txt";

                    // Use async/await to asynchronously get the latest version
                    string latestVersionString = await client.GetStringAsync(versionUrl).ConfigureAwait(false);
                    latestVersionString = latestVersionString.Trim();

                    Version latestVersion;

                    // Parse the version string into a Version object
                    if (Version.TryParse(latestVersionString, out latestVersion))
                    {
                        // Get the current version from the WoW.exe file
                        string wowExePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "_retail_", "WoW.exe");
                        FileVersionInfo wowExeVersionInfo = FileVersionInfo.GetVersionInfo(wowExePath);

                        // Extract version and revision information
                        string currentFileVersion = wowExeVersionInfo.FileVersion;

                        // Compare versions
                        if (latestVersion > new Version(currentFileVersion))
                        {
                            // New version available
                            Dispatcher.Invoke(() =>
                            {
                                progressInfo.Content = $"New game version available: {latestVersion}";
                                updateBtn.Visibility = Visibility.Visible; // Show the update button
                            }, DispatcherPriority.ContextIdle);

                        }
                        else
                        {
                            // No update available
                            Dispatcher.Invoke(() =>
                            {
                                progressInfo.Content = $"Up to date. Version: {currentFileVersion}";
                                updateBtn.Visibility = Visibility.Collapsed; // Hide the update button
                            });

                            Debug.WriteLine("Game is up to date.");
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"Invalid version format: {latestVersionString}");
                    }

                }
                catch (HttpRequestException ex)
                {
                    Debug.WriteLine($"Error checking for updates: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Unexpected error: {ex.Message}");
                }
            }
        }


        private async void installBtn_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("Install button clicked!");
            if (!IsInstalling)
            {
                IsInstalling = true;
                installBtn.IsEnabled = false; // Disable the install button during installation
                progressInfo.Visibility = Visibility.Visible;
                DownloadArchive();
            }
        }

        // Drag window around holding mouse button
        void Window_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                DragMove();
        }

        // Quit application
        void Close_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        // Allow URL clicking inside the rich text block
        void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.ToString())
            {
                UseShellExecute = true
            });
        }

        void playBtn_Click(object sender, RoutedEventArgs e)
        {
            string gameExecutablePath = Path.Combine(m_ExtractionPath, "Arctium WoW Launcher.exe");
            if (File.Exists(gameExecutablePath))
            {
                try
                {
                    // Set the working directory to the folder containing the game executable
                    string gameDirectory = Path.GetDirectoryName(gameExecutablePath);

                    // Launch the game executable
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = gameExecutablePath,
                        WorkingDirectory = gameDirectory,
                        UseShellExecute = true
                    });

                    // Optionally, close the launcher after launching the game
                    Application.Current.Shutdown();
                }
                catch (Exception ex)
                {
                    // Show an error message in case something goes wrong
                    MessageBox.Show($"An error occurred while launching the game: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        void webBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Open the hyperlink when the button is clicked
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://pandaexpresswow.com")
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while opening the website: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        void discBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Open the hyperlink when the button is clicked
                Process.Start(new ProcessStartInfo("https://discord.gg/MpyUBsAvjD")
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while opening Discord: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void updateBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Disable the update button during the update process
                updateBtn.IsEnabled = false;

                // Download the new version
                await Task.Run(() => DownloadArchive());

                // Extract the downloaded archive
                ExtractArchive();

                // Display a success message with the new version
                MessageBox.Show($"Update completed successfully", "Update Completed", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                // Log the error to the debug console
                Debug.WriteLine($"Error updating: {ex.Message}");
            }
            finally
            {
                // Hide the update button after the update process is complete or encounters an error
                updateBtn.Visibility = Visibility.Collapsed;

                // Enable the play button
                playBtn.IsEnabled = true;
                playBtn.Visibility = Visibility.Visible;
            }
        }

        void CheckServerStatus(string ipAddress, int port)
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    // Attempt to connect to the server
                    client.Connect(ipAddress, port);

                    // Check if the connection was successful
                    bool isServerOnline = client.Connected;

                    // Update the UI based on server status
                    if (isServerOnline)
                    {
                        serverStatus.Text = "Server is Online";
                        serverStatusIcon.Source = new BitmapImage(new Uri("Images/Indicator-Green.png", UriKind.Relative));
                    }
                    else
                    {
                        serverStatus.Text = "Server is Offline";
                        serverStatusIcon.Source = new BitmapImage(new Uri("Images/Indicator-Red.png", UriKind.Relative));
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while checking server status:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                serverStatus.Text = "Server status check failed";
                serverStatusIcon.Source = new BitmapImage(new Uri("Images/Indicator-Red.png", UriKind.Relative));
            }
        }

        public void DownloadArchive()
        {
            using (WebClient client = new WebClient())
            {
                client.DownloadProgressChanged += Client_DownloadProgressChanged;
                client.DownloadFileCompleted += Client_DownloadFileCompleted;

                IsInstalling = true; // Set IsExtracting to false before starting download
                installBtn.IsEnabled = false; // Disable the install button during download

                client.DownloadFileAsync(new Uri(m_DownloadUrl), m_DownloadPath);
            }
        }
        private void Client_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            downloadProgress = new Progress<double>(value =>
            {
                // Update the download progress bar here
                ProgressValue = value;
            });

            extractionProgress = new Progress<double>(value =>
            {
                // Update the extraction progress bar here
                ProgressValue = value;
            });

            Dispatcher.Invoke(() =>
            {
                // Update the ProgressBar value based on the download progress percentage.
                progressBar.Value = e.ProgressPercentage;

                // Calculate the downloaded file size in bytes and convert it to a human-readable format (e.g., KB, MB, GB).
                string downloadedSize = FormatFileSize(e.BytesReceived);

                // Calculate the total file size in bytes and convert it to a human-readable format (e.g., KB, MB, GB).
                string totalSize = FormatFileSize(e.TotalBytesToReceive);

                // Update the label text with the download status.
                progressInfo.Content = $"Downloaded: {downloadedSize} / Total: {totalSize}";

                // Update the ProgressValue directly
                ProgressValue = e.ProgressPercentage;
            });
        }

        private string FormatFileSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int suffixIndex = 0;
            double size = bytes;
            while (size >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                size /= 1024;
                suffixIndex++;
            }
            return $"{size:0.##} {suffixes[suffixIndex]}";
        }


        private void Client_DownloadFileCompleted(object sender, AsyncCompletedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (e.Error != null)
                {
                    // If an error occurred during the download, log the error to the debug console.
                    Debug.WriteLine("Archive download failed. Error: " + e.Error.Message);
                }
                else
                {
                    // The download is completed successfully; display a success message.
                    Debug.WriteLine("Archive download completed successfully!");

                    // Call the ExtractArchive method to extract the downloaded archive.
                    ExtractArchive();

                    CheckForInstall();

                    IsInstalling = false; // Set IsInstalling to false after installation is complete

                    // Delete the zip archive file after successful or unsuccessful download
                    if (File.Exists(m_DownloadPath))
                    {
                        try
                        {
                            File.Delete(m_DownloadPath);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("Error deleting zip archive: " + ex.Message);
                        }
                    }
                }
            });
        }

        private void ExtractArchive()
        {
            try
            {
                // Specify the destination directory as m_ExtractionPath
                ZipFile.ExtractToDirectory(m_DownloadPath, m_ExtractionPath);

                MessageBox.Show("Download complete.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while extracting files: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            // In case of error during extraction, delete the zip archive file.
            if (File.Exists(m_DownloadPath))
            {
                File.Delete(m_DownloadPath);
            }
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            try
            {
                // Delete the zip archive file when the application is closed.
                if (File.Exists(m_DownloadPath))
                {
                    File.Delete(m_DownloadPath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error deleting zip archive: " + ex.Message);
            }
        }
    }
}