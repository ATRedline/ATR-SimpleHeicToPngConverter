using ImageMagick;
using Microsoft.WindowsAPICodePack.Dialogs;
using SimpleHeicToPngConverter.models;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace SimpleHeicToPngConverter
{
    public partial class MainWindow : Window
    {
        private CancellationTokenSource? _cancellationTokenSource = null;
        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
        private readonly string _convertButtonText = "Конвертировать .heic to {extension} (Convert .heic to {extension})";

        public MainWindow()
        {
            InitializeComponent();
        }

        private void QualitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (QualityLabel is not null)
                QualityLabel.Content = $"{QualitySlider.Value}%";
        }

        private void SayThanks_Button_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                UseShellExecute = true,
                FileName = "https://pay.cloudtips.ru/p/982d3ecd",
            });
        }

        private async void Convert_Button_Click(object sender, RoutedEventArgs e)
        {
            if (_cancellationTokenSource is not null)
            {
                _cancellationTokenSource.Cancel();
                Convert_Button.IsEnabled = false;
            }
            else
            {
                var heicSelectorDialog = new CommonOpenFileDialog
                {
                    Multiselect = true,
                    IsFolderPicker = false,
                    Title = "Выберите файлы для конвертации",

                };

                heicSelectorDialog.Filters.Add(new CommonFileDialogFilter("Heic/dng images", "*.heic;*.dng"));

                var heicSelectorResult = heicSelectorDialog.ShowDialog();

                if (heicSelectorResult is CommonFileDialogResult.Ok)
                {
                    if (heicSelectorDialog.FileNames.Any(x => !x.ToLower().EndsWith("heic")))
                    {
                        ShowMessageBoxSafe("Для конвертации подходят только файлы в формате .heic", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    else
                    {
                        var outputExtensionString = OutputPngTypeCheckbox.IsChecked is true ? "png" : "jpg";
                        var outputFolderSelectorDialog = new CommonOpenFileDialog
                        {
                            IsFolderPicker = true,
                            Title = $"Выберите папку для размещения .{outputExtensionString} файлов"
                        };

                        var outputFolderSelectorResult = outputFolderSelectorDialog.ShowDialog();

                        if (outputFolderSelectorResult is CommonFileDialogResult.Ok)
                        {
                            var quality = (int)Math.Round(QualitySlider.Value);
                            var outputFormat = OutputPngTypeCheckbox.IsChecked is true ? MagickFormat.Png : MagickFormat.Jpg;

                            _cancellationTokenSource = new CancellationTokenSource();

                            try
                            {
                                _dispatcher.Invoke(() => SetSettingsElementsState(isProcessInProgress: true));

                                await ConvertFiles([.. heicSelectorDialog.FileNames], outputFolderSelectorDialog.FileName, quality, outputFormat, _cancellationTokenSource.Token);

                                ShowMessageBoxSafe("Все файлы сконвертированы успешно (All files converted successfully)", "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
                            }
                            catch (Exception ex)
                            {
                                if (_cancellationTokenSource.IsCancellationRequested)
                                    return;

                                ShowMessageBoxSafe("Ошибка в процессе записи файлов.\nПроверьте свободное место на диске и проверьте\nдоступность записи в выбранную папку текущего пользователя\n(Error during file converting progress,\ncheck for free disk space and current user\nwrite access to selected folder)", "Ошибка в процессе записи файлов", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                            finally
                            {
                                _cancellationTokenSource = null;
                                _dispatcher.Invoke(() => SetSettingsElementsState(isProcessInProgress: false));
                            }
                        }
                    }
                }
            }
        }

        private async Task ConvertFiles(string[] filesPaths, string outputDirectory, int quality, MagickFormat outputFormat, CancellationToken cancellationToken)
        {
            double systemCoresCount = (Environment.ProcessorCount / (double)100) * 25;
            var threadsCount = (int)(Environment.ProcessorCount - Math.Round(systemCoresCount));

            var progress = new Progress<int>(value => ConvertationProgressBar.Value = value);

            var processed = 0;
            var totalFiles = filesPaths.Count();
            var semaphore = new SemaphoreSlim(threadsCount);
            var tasks = new LimitedConcurrentQueue<Task>(threadsCount);

            _dispatcher.Invoke(() => ConvertationProgressBar.Maximum = filesPaths.Length);

            foreach (var filePath in filesPaths)
            {
                var fileInfo = new FileInfo(filePath);
                var targetFileName = fileInfo.Name.Replace(fileInfo.Extension, outputFormat is MagickFormat.Png ? ".png" : ".jpg");
                var targetFilePath = $"{outputDirectory}{Path.DirectorySeparatorChar}{targetFileName}";

                await semaphore.WaitAsync(cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                    break;

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        RemoveFileIfExists(targetFilePath);

                        using (var image = new MagickImage(filePath))
                        {
                            image.Format = outputFormat;
                            image.Quality = (uint)quality;

                            await image.WriteAsync(targetFilePath, cancellationToken);
                        }

                        var filesDone = Interlocked.Increment(ref processed);

                        _dispatcher.Invoke(() => ConvertationProgressBar.Value = filesDone);
                    }
                    catch (Exception ex)
                    {
                        RemoveFileIfExists(filePath);

                        if (cancellationToken.IsCancellationRequested)
                            return;

                        throw;
                    }
                    finally
                    {
                        semaphore.Release();
                    }

                }, cancellationToken));
            }

            await Task.WhenAll(tasks);
        }

        private void SetSettingsElementsState(bool isProcessInProgress)
        {
            QualitySlider.IsEnabled = !isProcessInProgress;
            OutputPngTypeCheckbox.IsEnabled = !isProcessInProgress;
            OutputJpgTypeCheckbox.IsEnabled = !isProcessInProgress;

            if (!isProcessInProgress)
            {

                Convert_Button.IsEnabled = true;
                ConvertationProgressBar.Visibility = Visibility.Hidden;
                Convert_Button.Content = _convertButtonText.Replace("{extension}", OutputJpgTypeCheckbox.IsChecked is true ? ".png" : ".jpg");
            }
            else
            {
                Convert_Button.Content = "Отмена";
                ConvertationProgressBar.Value = 0;
                ConvertationProgressBar.Visibility = Visibility.Visible;
            }
        }

        private void OutputPngTypeCheckbox_Click(object sender, RoutedEventArgs e)
        {
            QualitySlider.Value = 0;
            SliderLabel.Content = "Степень сжатия (Compression rate):";
            Convert_Button.Content = _convertButtonText.Replace("{extension}", ".png");
        }

        private void OutputJpgTypeCheckbox_Click(object sender, RoutedEventArgs e)
        {
            QualitySlider.Value = 100;
            SliderLabel.Content = "Качество конвертации (Quality):";
            Convert_Button.Content = _convertButtonText.Replace("{extension}", ".jpg");
        }

        private void RemoveFileIfExists(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                var lastFile = new FileInfo(path);

                if (lastFile.Exists)
                    lastFile.Delete();
            }
        }

        private void ShowMessageBoxSafe(string message, string title, MessageBoxButton button, MessageBoxImage icon)
            => _dispatcher.Invoke(() => MessageBox.Show(message, title, button, icon));
    }
}