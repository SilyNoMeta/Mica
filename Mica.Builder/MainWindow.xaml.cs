using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Mica.Core;
using Mica.Core.Apps;
using Mica.Core.Localization;
using Mica.Core.Updates;
using Windows.Graphics;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Mica.Builder;

/// <summary>
/// Lists the apps the user has built and creates new ones. Removing an app here
/// removes it everywhere: shortcut, browser profile and Installed apps entry.
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <param name="pendingPack">
    /// An app pack the shell handed us — Mica was started by double-clicking a
    /// <c>.mica</c> file. Offered once the window is up, not from the constructor:
    /// a dialog needs a XamlRoot, which does not exist yet.
    /// </param>
    public MainWindow(string? pendingPack = null)
    {
        InitializeComponent();

        _pendingPack = pendingPack;
        Root.Loaded += OnRootLoaded;

        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(icon))
        {
            AppWindow.SetIcon(icon);
        }

        // Tall enough for the per-app settings dialog to show all three options at
        // once in the longer languages, rather than putting the last one below a
        // fold nobody looks for.
        AppWindow.Resize(new SizeInt32(880, 820));

        Root.FlowDirection = Strings.FlowDirection;
        ApplyStrings();

        _ = RefreshAsync();
        _ = CheckForUpdateAsync();
    }

    private string? _pendingPack;

    private void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        Root.Loaded -= OnRootLoaded;

        if (_pendingPack is { } path)
        {
            _pendingPack = null;
            OfferImport(path);
        }
    }

    // ---- Updates -------------------------------------------------------------

    private UpdateInfo? _update;

    /// <summary>
    /// Looks for a newer Mica in the background, at most once a day. Nothing is
    /// downloaded until the user presses the button: the installer is unsigned, and
    /// SmartScreen questioning it is far better met after a deliberate click.
    /// </summary>
    private async Task CheckForUpdateAsync()
    {
        _update = await UpdateChecker.CheckAsync();
        if (_update is null)
        {
            return;
        }

        UpdateButton.Content = Strings.Get("UpdateAction");
        UpdateBar.Severity = InfoBarSeverity.Informational;
        UpdateBar.Title = Strings.Format("UpdateAvailableTitle", _update.Version);
        UpdateBar.Message = _update.Notes;
        UpdateBar.IsOpen = true;
    }

    private async void OnUpdateClick(object sender, RoutedEventArgs e)
    {
        if (_update is not { } update)
        {
            return;
        }

        UpdateButton.IsEnabled = false;
        UpdateBar.Title = Strings.Format("UpdateDownloading", update.Version);
        UpdateBar.Message = Strings.Get("UpdateInstalling");

        try
        {
            var installer = await UpdateChecker.DownloadAsync(update);
            UpdateChecker.InstallAndExit(installer);
        }
        catch (Exception ex)
        {
            UpdateBar.Severity = InfoBarSeverity.Error;
            UpdateBar.Title = Strings.Get("UpdateFailedTitle");
            UpdateBar.Message = ex.Message;
            UpdateButton.IsEnabled = true;
        }
    }

    private void ApplyStrings()
    {
        HeadingText.Text = Strings.Get("YourApps");
        AddAppText.Text = Strings.Get("AddApp");
        EmptyTitleText.Text = Strings.Get("NoAppsYetTitle");
        EmptyBodyText.Text = Strings.Get("EmptyBody");
        EmptyAddButton.Content = Strings.Get("AddAppButton");
        VersionText.Text = $"{MicaPaths.ProductName} {UpdateChecker.Current.ToString(3)}";

        // The button carries only a glyph, so its name has to be published as well as
        // shown: a tooltip is not something a screen reader reads out.
        ToolTipService.SetToolTip(TransferButton, Strings.Get("Transfer"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(TransferButton, Strings.Get("Transfer"));
        ExportMenuItem.Text = Strings.Get("ExportApps");
        ImportMenuItem.Text = Strings.Get("ImportApps");
    }

    // ---- App list ------------------------------------------------------------

    private async Task RefreshAsync()
    {
        var apps = AppRegistry.List();
        var items = new List<AppListItem>(apps.Count);

        var moreActions = Strings.Get("MoreActions");
        var open = Strings.Get("Open");
        var refresh = Strings.Get("RefreshIcon");
        var settings = Strings.Get("AppSettings");
        var remove = Strings.Get("Remove");

        foreach (var app in apps)
        {
            items.Add(new AppListItem(
                app.Slug,
                app.Name,
                app.StartUri.Host,
                app.StartUrl,
                // 128 rather than the 36 the row draws it at: the decode is in physical
                // pixels, so a 200% display already needs 72 and a 300% one 108. The list
                // is built before the window has a XamlRoot to read a scale off, so it
                // decodes for the largest scale instead and lets XAML shrink from there —
                // and 128 is an entry in the .ico, so nothing is resampled on the way in.
                await ImageLoader.LoadAsync(MicaPaths.IconFile(app.Slug), 128),
                moreActions,
                open,
                refresh,
                settings,
                remove));
        }

        AppList.ItemsSource = items;

        var hasApps = items.Count > 0;
        AppList.Visibility = hasApps ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = hasApps ? Visibility.Collapsed : Visibility.Visible;
        SubtitleText.Text = items.Count switch
        {
            0 => Strings.Get("NoAppsYet"),
            1 => Strings.Get("AppsCountOne"),
            _ => Strings.Format("AppsCountMany", items.Count),
        };
    }

    // ---- Commands ------------------------------------------------------------

    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        var dialog = new AddAppDialog
        {
            XamlRoot = Content.XamlRoot,
            FlowDirection = Strings.FlowDirection,
        };
        await ShowDialogAsync(dialog);

        if (dialog.Created is not { } created)
        {
            return;
        }

        await RefreshAsync();
        ShowStatus(
            InfoBarSeverity.Success,
            Strings.Format("ReadyTitle", created.Name),
            Strings.Format("ReadyBody", MicaPaths.ProductName));
    }

    // ---- Moving apps between machines ----------------------------------------

    /// <summary>
    /// Writes the chosen apps to one file: their addresses, names, host rules and
    /// settings, plus the store ids of the extensions they run. Neither the icons nor
    /// the extensions themselves travel — both are fetched again on import, which
    /// keeps the file at a few kilobytes and delivers current versions rather than a
    /// snapshot of today.
    /// </summary>
    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        var apps = AppRegistry.List();
        if (apps.Count == 0)
        {
            ShowStatus(InfoBarSeverity.Informational, Strings.Get("NothingToExport"), string.Empty);
            return;
        }

        var entries = apps.Select(AppPack.Describe).ToList();

        var dialog = new TransferDialog(
            Strings.Get("ExportTitle"),
            Strings.Get("ExportBody"),
            Strings.Get("ExportAction"),
            entries.Select(entry => new TransferDialog.Row(entry, entry.Name, Describe(entry), enabled: true)))
        {
            XamlRoot = Content.XamlRoot,
        };

        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary)
        {
            return;
        }

        var chosen = dialog.Selected.Cast<AppPack.Entry>().ToList();

        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, App.WindowHandle);
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add(MicaPaths.ProductName, [AppPack.FileExtension]);
        picker.SuggestedFileName = chosen.Count == 1 ? chosen[0].Slug : "mica-apps";

        if (await picker.PickSaveFileAsync() is not { } file)
        {
            return;
        }

        try
        {
            new AppPack
            {
                CreatedBy = UpdateChecker.Current.ToString(3),
                Apps = chosen,
            }.Save(file.Path);

            ShowStatus(
                InfoBarSeverity.Success,
                chosen.Count == 1 ? Strings.Get("ExportedOne") : Strings.Format("ExportedMany", chosen.Count),
                file.Path);
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, Strings.Get("ExportFailedTitle"), ex.Message);
        }
    }

    /// <summary>
    /// Reads a pack and builds the apps the user picks out of it. An app already
    /// installed here is shown but cannot be ticked: importing it again would leave
    /// two entries for one site rather than updating anything.
    /// </summary>
    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, App.WindowHandle);
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add(AppPack.FileExtension);

        if (await picker.PickSingleFileAsync() is { } file)
        {
            await OfferImportAsync(file.Path);
        }
    }

    /// <summary>
    /// Offers a pack the shell handed over — a double-clicked <c>.mica</c>, whether
    /// it started Mica or arrived while it was already open.
    /// </summary>
    public async void OfferImport(string path)
    {
        // A file can be double-clicked at any moment, including while the user is
        // in the middle of another dialog; a second one would throw rather than
        // queue. Dropping the request is better than crashing on it, and the user
        // still has ⋯ › Import.
        if (_openDialogs > 0)
        {
            return;
        }

        try
        {
            await OfferImportAsync(path);
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, Strings.Get("ImportFailedTitle"), ex.Message);
        }
    }

    private async Task OfferImportAsync(string path)
    {
        AppPack pack;
        try
        {
            pack = AppPack.Load(path);
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, Strings.Get("ImportFailedTitle"), ex.Message);
            return;
        }

        if (pack.Apps.Count == 0)
        {
            ShowStatus(InfoBarSeverity.Informational, Strings.Get("PackEmpty"), path);
            return;
        }

        var rows = pack.Apps.Select(entry =>
        {
            var installed = AppRegistry.FindByStartUrl(entry.StartUrl) is not null;

            return new TransferDialog.Row(
                entry,
                string.IsNullOrWhiteSpace(entry.Name) ? entry.StartUri.Host : entry.Name,
                installed ? Strings.Get("AlreadyInstalled") : Describe(entry),
                enabled: !installed);
        });

        var dialog = new TransferDialog(
            Strings.Get("ImportTitle"),
            Strings.Get("ImportBody"),
            Strings.Get("ImportAction"),
            rows)
        {
            XamlRoot = Content.XamlRoot,
        };

        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary)
        {
            return;
        }

        await ImportAsync([.. dialog.Selected.Cast<AppPack.Entry>()]);
    }

    private async Task ImportAsync(List<AppPack.Entry> entries)
    {
        var imported = 0;
        var missingExtensions = 0;
        var failures = new List<string>();

        SetBusy(true);

        try
        {
            foreach (var entry in entries)
            {
                var name = string.IsNullOrWhiteSpace(entry.Name) ? entry.StartUri.Host : entry.Name;
                BusyText.Text = Strings.Format("ImportingApp", name);

                try
                {
                    var progress = new Progress<string>(_ =>
                        BusyText.Text = Strings.Format("ImportingExtensions", name));

                    var result = await AppRegistry.ImportAsync(entry, progress);

                    imported++;
                    missingExtensions += result.MissingExtensions.Count;
                }
                catch (Exception ex)
                {
                    failures.Add($"{name} — {ex.Message}");
                }
            }
        }
        finally
        {
            SetBusy(false);
        }

        await RefreshAsync();

        if (failures.Count > 0)
        {
            ShowStatus(
                imported > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Error,
                Strings.Get("ImportFailedTitle"),
                string.Join(Environment.NewLine, failures));

            return;
        }

        ShowStatus(
            InfoBarSeverity.Success,
            imported == 1 ? Strings.Get("ImportedOne") : Strings.Format("ImportedMany", imported),
            missingExtensions > 0
                ? Strings.Format("ImportedMissingExtensions", missingExtensions)
                : Strings.Get("ImportedBody"));
    }

    /// <summary>The second line of a transfer row: where the app points, and what it carries.</summary>
    private static string Describe(AppPack.Entry entry)
    {
        var host = entry.StartUri.Host;

        return entry.Extensions.Count switch
        {
            0 => host,
            1 => $"{host} · {Strings.Get("ExtensionsCountOne")}",
            _ => $"{host} · {Strings.Format("ExtensionsCountMany", entry.Extensions.Count)}",
        };
    }

    private void OnAppClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AppListItem item)
        {
            Launch(item.Slug);
        }
    }

    private void OnOpenAppClick(object sender, RoutedEventArgs e)
    {
        if (SlugOf(sender) is { } slug)
        {
            Launch(slug);
        }
    }

    private void Launch(string slug)
    {
        var config = AppRegistry.Find(slug);
        if (config is null)
        {
            return;
        }

        try
        {
            AppRegistry.Launch(config);
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, Strings.Get("OpenFailedTitle"), ex.Message);
        }
    }

    private async void OnRefreshIconClick(object sender, RoutedEventArgs e)
    {
        if (SlugOf(sender) is not { } slug || AppRegistry.Find(slug) is not { } config)
        {
            return;
        }

        SetBusy(true, Strings.Format("FetchingIcon", config.Name));

        try
        {
            await AppRegistry.RefreshAsync(config);
            await RefreshAsync();
            ShowStatus(
                InfoBarSeverity.Success,
                Strings.Get("IconRefreshedTitle"),
                Strings.Format("IconRefreshedBody", config.Name));
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, Strings.Get("RefreshFailedTitle"), ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnAppSettingsClick(object sender, RoutedEventArgs e)
    {
        if (SlugOf(sender) is not { } slug || AppRegistry.Find(slug) is not { } config)
        {
            return;
        }

        var dialog = new AppSettingsDialog(config) { XamlRoot = Content.XamlRoot };
        await ShowDialogAsync(dialog);
    }

    private async void OnRemoveAppClick(object sender, RoutedEventArgs e)
    {
        if (SlugOf(sender) is not { } slug || AppRegistry.Find(slug) is not { } config)
        {
            return;
        }

        var confirm = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            FlowDirection = Strings.FlowDirection,
            Title = Strings.Format("RemoveConfirmTitle", config.Name),
            Content = Strings.Get("RemoveConfirmBody"),
            PrimaryButtonText = Strings.Get("Remove"),
            CloseButtonText = Strings.Get("Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        if (await ShowDialogAsync(confirm) != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            AppRegistry.Remove(config);
            await RefreshAsync();
            ShowStatus(InfoBarSeverity.Informational, Strings.Format("RemovedTitle", config.Name), string.Empty);
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, Strings.Get("RemoveFailedTitle"), ex.Message);
        }
    }

    // ---- Helpers -------------------------------------------------------------

    private static string? SlugOf(object sender) =>
        sender is FrameworkElement { Tag: string slug } ? slug : null;

    // A XamlRoot hosts one ContentDialog at a time; a second call throws rather
    // than waiting. Every dialog goes through here so anything arriving from
    // outside the UI — a double-clicked file — can tell whether it may show one.
    private int _openDialogs;

    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        _openDialogs++;

        try
        {
            return await dialog.ShowAsync();
        }
        finally
        {
            _openDialogs--;
        }
    }

    private void SetBusy(bool busy, string message = "")
    {
        BusyText.Text = message;
        BusyOverlay.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowStatus(InfoBarSeverity severity, string title, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Title = title;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }

    /// <summary>
    /// One row of the app list. Its labels travel with it because a DataTemplate
    /// binds against the row, not against the window.
    /// </summary>
    public sealed record AppListItem(
        string Slug,
        string Name,
        string Host,
        string Url,
        BitmapImage? Icon,
        string MoreActionsLabel,
        string OpenLabel,
        string RefreshLabel,
        string SettingsLabel,
        string RemoveLabel);
}
