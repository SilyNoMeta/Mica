using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Mica.Core;
using Mica.Core.Apps;
using Mica.Core.Localization;
using Windows.Graphics;

namespace Mica.Builder;

/// <summary>
/// Lists the apps the user has built and creates new ones. Removing an app here
/// removes it everywhere: shortcut, browser profile and Installed apps entry.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

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
    }

    private void ApplyStrings()
    {
        HeadingText.Text = Strings.Get("YourApps");
        AddAppText.Text = Strings.Get("AddApp");
        EmptyTitleText.Text = Strings.Get("NoAppsYetTitle");
        EmptyBodyText.Text = Strings.Get("EmptyBody");
        EmptyAddButton.Content = Strings.Get("AddAppButton");
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
                await ImageLoader.LoadAsync(MicaPaths.IconFile(app.Slug)),
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
        await dialog.ShowAsync();

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
        await dialog.ShowAsync();
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

        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
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
