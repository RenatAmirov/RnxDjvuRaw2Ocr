using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RnxDjvuRaw2Ocr.Core;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.Windows.Storage.Pickers;

namespace RnxDjvuRaw2Ocr.App;

public sealed partial class MainWindow : Window
{
    private readonly OcrService service = new();
    private CancellationTokenSource? cancellation;
    private string? resultPath;
    private bool closeAfterCancellation;
    private bool initializing = true;

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1120, 980));
        AppWindow.Closing += OnClosing;
        var settings = SettingsStore.Load();
        SetTools(settings.Tools);
        LanguagesBox.Text = settings.Languages;
        LayoutBox.SelectedIndex = Math.Clamp(settings.LayoutIndex, 0, 2);
        initializing = false;
    }

    private async void Root_Loaded(object sender, RoutedEventArgs e)
    {
        var argument = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(File.Exists);
        if (argument is not null) InputPathBox.Text = Path.GetFullPath(argument);
        await CheckToolsAsync();
    }

    private ToolPaths ReadTools() => new(DjvuDirectoryBox.Text.Trim().Trim('"'),
        TesseractPathBox.Text.Trim().Trim('"'), TessdataPathBox.Text.Trim().Trim('"'));

    private void SetTools(ToolPaths tools)
    {
        DjvuDirectoryBox.Text = tools.DjvuDirectory;
        TesseractPathBox.Text = tools.TesseractExecutable;
        TessdataPathBox.Text = tools.TessdataDirectory;
    }

    private async void BrowseInput_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker(AppWindow.Id);
            picker.FileTypeFilter.Add(".djvu");
            picker.FileTypeFilter.Add(".djv");
            if (await picker.PickSingleFileAsync() is { } file) InputPathBox.Text = file.Path;
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker(AppWindow.Id)
            {
                SuggestedFileName = Path.GetFileNameWithoutExtension(InputPathBox.Text) + ".ocr",
                DefaultFileExtension = ".djvu"
            };
            picker.FileTypeChoices.Add("DjVu-документ", new List<string> { ".djvu" });
            if (await picker.PickSaveFileAsync() is { } file) OutputPathBox.Text = file.Path;
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void InputPath_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (initializing || OutputPathBox is null) return;
        try
        {
            if (!string.IsNullOrWhiteSpace(InputPathBox.Text))
            {
                var input = Path.GetFullPath(InputPathBox.Text.Trim().Trim('"'));
                var name = Path.Combine(Path.GetDirectoryName(input)!, Path.GetFileNameWithoutExtension(input) + ".ocr");
                var output = name + ".djvu";
                for (var i = 2; File.Exists(output); i++) output = $"{name}-{i}.djvu";
                OutputPathBox.Text = output;
                StatusText.Text = "Документ готов к распознаванию";
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }
    }

    private void LanguagePreset_Click(object sender, RoutedEventArgs e) => LanguagesBox.Text = (string)((Button)sender).Tag;
    private async void CheckTools_Click(object sender, RoutedEventArgs e) => await CheckToolsAsync();
    private async void DiscoverTools_Click(object sender, RoutedEventArgs e)
    {
        SetTools(ToolDiscovery.Discover());
        await CheckToolsAsync();
    }

    private async Task CheckToolsAsync()
    {
        try
        {
            var languages = await service.GetLanguagesAsync(ReadTools());
            ToolsStatusText.Text = $"Инструменты готовы. Доступные языки: {string.Join(", ", languages)}";
            if (Notice.Title == "Инструменты не настроены") Notice.IsOpen = false;
        }
        catch (Exception ex)
        {
            ToolsStatusText.Text = ex.Message;
            ShowNotice("Инструменты не настроены", ex.Message + " Подготовка описана в README.md проекта.", InfoBarSeverity.Warning);
        }
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (cancellation is not null) return;
        try
        {
            var input = InputPathBox.Text.Trim().Trim('"');
            var output = OutputPathBox.Text.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(output))
                throw new ArgumentException("Выберите исходный файл и укажите путь для результата.");
            if (string.Equals(Path.GetFullPath(input), Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Для результата выберите другое имя: исходный файл сохраняется.");
            var overwrite = File.Exists(output);
            if (overwrite)
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = Root.XamlRoot,
                    Title = "Заменить существующий результат?",
                    Content = output,
                    PrimaryButtonText = "Заменить",
                    CloseButtonText = "Отмена",
                    DefaultButton = ContentDialogButton.Close
                };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            }
            var tools = ReadTools();
            tools.Validate();
            SettingsStore.Save(new AppSettings(tools, LanguagesBox.Text.Trim(), LayoutBox.SelectedIndex));
            cancellation = new CancellationTokenSource();
            SetBusy(true);
            Notice.IsOpen = false;
            ResultActions.Visibility = Visibility.Collapsed;
            resultPath = null;
            PreviewTextBox.Text = "";
            OcrProgressBar.Value = 0;
            OcrProgressBar.IsIndeterminate = true;
            var psm = int.Parse((string)((ComboBoxItem)LayoutBox.SelectedItem).Tag, CultureInfo.InvariantCulture);
            var progress = new Progress<OcrProgress>(update =>
            {
                StatusText.Text = update.Message;
                OcrProgressBar.IsIndeterminate = update.TotalPages == 0;
                OcrProgressBar.Value = update.Percent;
                DetailText.Text = update.TotalPages == 0 ? "Подготовка документа" : $"Обработано {update.CompletedPages} из {update.TotalPages} страниц";
                if (update.PageText is not null) PreviewTextBox.Text = string.IsNullOrEmpty(update.PageText) ? "На странице текст не обнаружен." : update.PageText;
            });
            var result = await service.RunAsync(new OcrRequest(input, output, tools, LanguagesBox.Text.Trim(), psm, overwrite), progress, cancellation.Token);
            resultPath = result.OutputPath;
            OcrProgressBar.IsIndeterminate = false;
            OcrProgressBar.Value = 100;
            StatusText.Text = "Готово. Теперь в книге можно искать";
            DetailText.Text = $"{result.Pages} стр. · {result.Words:N0} слов · {result.Elapsed.TotalMinutes:0} мин {result.Elapsed.Seconds} с";
            ResultActions.Visibility = Visibility.Visible;
            var message = result.OutputPath + (result.EmptyPages > 0 ? $"\nСтраниц без найденного текста: {result.EmptyPages}. Проверьте язык и качество сканов." : "");
            ShowNotice("Документ сохранён", message, result.Words == 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Распознавание отменено";
            DetailText.Text = "Незавершённый результат не сохранён.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Не удалось завершить обработку";
            DetailText.Text = "Проверьте сообщение и повторите попытку.";
            ShowError(ex);
        }
        finally
        {
            cancellation?.Dispose();
            cancellation = null;
            OcrProgressBar.IsIndeterminate = false;
            SetBusy(false);
            if (closeAfterCancellation) Close();
        }
    }

    private void SetBusy(bool busy)
    {
        InputPanel.IsEnabled = !busy;
        StartButton.IsEnabled = !busy;
        CancelButton.IsEnabled = true;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        cancellation?.Cancel();
        CancelButton.IsEnabled = false;
        StatusText.Text = "Отмена…";
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (cancellation is null) return;
        args.Cancel = true;
        closeAfterCancellation = true;
        cancellation.Cancel();
    }

    private void ShowError(Exception error) => ShowNotice("Ошибка", error.Message, InfoBarSeverity.Error);
    private void ShowNotice(string title, string message, InfoBarSeverity severity)
    {
        Notice.Title = title;
        Notice.Message = message;
        Notice.Severity = severity;
        Notice.IsOpen = true;
    }

    private void OpenResult_Click(object sender, RoutedEventArgs e)
    {
        try { if (resultPath is not null) Process.Start(new ProcessStartInfo(resultPath) { UseShellExecute = true }); }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ShowResult_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (resultPath is not null) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{resultPath}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void File_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.StorageItems) && cancellation is null ? DataPackageOperation.Copy : DataPackageOperation.None;
    }

    private async void File_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (cancellation is not null || !e.DataView.Contains(StandardDataFormats.StorageItems)) return;
            var items = await e.DataView.GetStorageItemsAsync();
            if (items.Count == 1 && Path.GetExtension(items[0].Path).ToLowerInvariant() is ".djvu" or ".djv")
                InputPathBox.Text = items[0].Path;
            else ShowNotice("Выберите DJVU", "Перетащите один файл .djvu или .djv.", InfoBarSeverity.Warning);
        }
        catch (Exception ex) { ShowError(ex); }
    }
}
