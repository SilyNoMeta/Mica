using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Mica.Core;
using Mica.Core.Apps;
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

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(icon))
        {
            AppWindow.SetIcon(icon);
        }

        AppWindow.Resize(new SizeInt32(880, 720));

        _ = RefreshAsync();
    }

    // ---- App list ------------------------------------------------------------

    private async Task RefreshAsync()
    {
        var apps = AppRegistry.List();
        var items = new List<AppListItem>(apps.Count);

        foreach (var app in apps)
        {
            items.Add(new AppListItem(
                app.Slug,
                app.Name,
                app.StartUri.Host,
                app.StartUrl,
                await ImageLoader.LoadAsync(MicaPaths.IconFile(app.Slug))));
        }

        AppList.ItemsSource = items;

        var hasApps = items.Count > 0;
        AppList.Visibility = hasApps ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = hasApps ? Visibility.Collapsed : Visibility.Visible;
        SubtitleText.Text = items.Count switch
        {
            0 => "Aucune app pour l'instant",
            1 => "1 app · raccourcis dans Démarrer › Mica",
            _ => $"{items.Count} apps · raccourcis dans Démarrer › Mica",
        };
    }

    // ---- Commands ------------------------------------------------------------

    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        var dialog = new AddAppDialog { XamlRoot = Content.XamlRoot };
        await dialog.ShowAsync();

        if (dialog.Created is not { } created)
        {
            return;
        }

        await RefreshAsync();
        ShowStatus(
            InfoBarSeverity.Success,
            $"{created.Name} est prête",
            $"Raccourci ajouté dans le menu Démarrer, dossier « {MicaPaths.ProductName} ».");
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
            ShowStatus(InfoBarSeverity.Error, "Ouverture impossible", ex.Message);
        }
    }

    private async void OnRefreshIconClick(object sender, RoutedEventArgs e)
    {
        if (SlugOf(sender) is not { } slug || AppRegistry.Find(slug) is not { } config)
        {
            return;
        }

        SetBusy(true, $"Récupération de l'icône de {config.Name}…");

        try
        {
            await AppRegistry.RefreshAsync(config);
            await RefreshAsync();
            ShowStatus(InfoBarSeverity.Success, "Icône actualisée", $"{config.Name} utilise l'icône actuelle du site.");
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Actualisation impossible", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
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
            Title = $"Retirer {config.Name} ?",
            Content = "Le raccourci, les réglages et la session connectée de cette app seront supprimés. "
                + "Le site lui-même n'est pas affecté.",
            PrimaryButtonText = "Retirer",
            CloseButtonText = "Annuler",
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
            ShowStatus(InfoBarSeverity.Informational, $"{config.Name} a été retirée", string.Empty);
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Suppression impossible", ex.Message);
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

    /// <summary>One row of the app list.</summary>
    public sealed record AppListItem(
        string Slug,
        string Name,
        string Host,
        string Url,
        BitmapImage? Icon);
}
