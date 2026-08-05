using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Mica.Core.Extensions;
using Mica.Core.Integration;
using Mica.Core.Sites;

namespace Mica.Core.Shell;

/// <summary>
/// The window content of every generated app: the site, full bleed, with no
/// browser furniture. A slim toolbar appears on <c>Alt</c> and can be pinned;
/// settings are deliberately limited to zoom and extensions.
/// </summary>
public sealed partial class BrowserShellPage : UserControl
{
    private const double MinZoom = 0.5;
    private const double MaxZoom = 3.0;

    private static readonly double[] ZoomSteps =
        [0.5, 0.67, 0.75, 0.8, 0.9, 1.0, 1.1, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0];

    private readonly NotificationBridge _notifications = new();

    private WebAppConfig _config = null!;
    private CoreWebView2? _core;
    private CoreWebView2Profile? _profile;
    private string? _zoomScriptId;
    private bool _started;

    /// <summary>Raised when the page title changes, so the window can follow it.</summary>
    public event EventHandler<string>? TitleChanged;

    public BrowserShellPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>Must be called before the control loads.</summary>
    public void Configure(WebAppConfig config)
    {
        _config = config;
        PinButton.IsChecked = config.ToolbarPinned;
        Toolbar.Visibility = config.ToolbarPinned ? Visibility.Visible : Visibility.Collapsed;
        UpdateZoomText();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_started)
        {
            return;
        }

        _started = true;

        try
        {
            await StartAsync();
        }
        catch (Exception ex)
        {
            StartupLog.WriteException("Shell.Start", ex);
            ErrorText.Text = $"Impossible de démarrer le navigateur intégré.\n\n{ex.Message}";
            ErrorOverlay.Visibility = Visibility.Visible;
        }
    }

    private async Task StartAsync()
    {
        StartupLog.Write("Creating WebView2 environment");

        // Extensions can only be turned on when the environment is created, and the
        // profile folder is what keeps each app's sign-ins separate.
        var options = new CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = true };
        var profileFolder = MicaPaths.ProfileFolder(_config.Slug);
        Directory.CreateDirectory(profileFolder);

        var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, profileFolder, options);
        await Browser.EnsureCoreWebView2Async(environment);

        _core = Browser.CoreWebView2;
        _profile = _core.Profile;
        StartupLog.Write("WebView2 ready");

        ConfigureWebView(_core);

        await _core.AddScriptToExecuteOnDocumentCreatedAsync(ShellScript);
        await ApplyZoomAsync();
        await RestoreExtensionsAsync();

        _notifications.Register();
        _notifications.Attach(_core);

        StartupLog.Write($"Navigating to {_config.StartUrl}");
        _core.Navigate(_config.StartUrl);
    }

    private void ConfigureWebView(CoreWebView2 webView)
    {
        webView.Settings.IsStatusBarEnabled = false;
        webView.Settings.AreDefaultContextMenusEnabled = true;
        webView.Settings.AreDevToolsEnabled = true;

        // Mica owns zoom so it can persist it and apply it to every page; leaving the
        // browser's own zoom on as well would stack two independent scale factors.
        webView.Settings.IsZoomControlEnabled = false;

        webView.NavigationStarting += OnNavigationStarting;
        webView.NavigationCompleted += OnNavigationCompleted;
        webView.NewWindowRequested += OnNewWindowRequested;
        webView.HistoryChanged += OnHistoryChanged;
        webView.DocumentTitleChanged += OnDocumentTitleChanged;
        webView.WebMessageReceived += OnWebMessageReceived;
    }

    // ---- Toolbar reveal ------------------------------------------------------

    /// <summary>
    /// Injected into every page. The toolbar is hidden, so the reveal gesture has
    /// to be observed inside the page where the keyboard focus actually is; the
    /// same listener also routes the familiar zoom shortcuts back to the shell.
    /// </summary>
    private const string ShellScript = """
        (() => {
          const post = m => { try { window.chrome.webview.postMessage(m); } catch (e) { } };
          let altAlone = false;

          addEventListener('keydown', e => {
            altAlone = e.key === 'Alt' && !e.ctrlKey && !e.shiftKey && !e.metaKey && !e.repeat;

            if (e.key === 'Escape') { post('mica:hide'); return; }
            if (!e.ctrlKey) { return; }

            if (e.key === '+' || e.key === '=') { post('mica:zoom-in'); e.preventDefault(); }
            else if (e.key === '-') { post('mica:zoom-out'); e.preventDefault(); }
            else if (e.key === '0') { post('mica:zoom-reset'); e.preventDefault(); }
          }, true);

          addEventListener('keyup', e => {
            if (e.key === 'Alt' && altAlone) { post('mica:toggle'); }
            altAlone = false;
          }, true);

          addEventListener('wheel', e => {
            if (!e.ctrlKey) { return; }
            e.preventDefault();
            post(e.deltaY < 0 ? 'mica:zoom-in' : 'mica:zoom-out');
          }, { capture: true, passive: false });

          addEventListener('blur', () => { altAlone = false; });
          addEventListener('mousedown', () => { altAlone = false; }, true);
        })();
        """;

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string message;
        try
        {
            message = e.TryGetWebMessageAsString();
        }
        catch
        {
            // Pages can post structured messages of their own; ignore those.
            return;
        }

        switch (message)
        {
            case "mica:toggle":
                ToggleToolbar();
                break;
            case "mica:hide":
                HideToolbar();
                break;
            case "mica:zoom-in":
                _ = StepZoomAsync(1);
                break;
            case "mica:zoom-out":
                _ = StepZoomAsync(-1);
                break;
            case "mica:zoom-reset":
                _ = SetZoomAsync(1.0);
                break;
        }
    }

    private void ToggleToolbar()
    {
        if (Toolbar.Visibility == Visibility.Visible)
        {
            HideToolbar();
        }
        else
        {
            Toolbar.Visibility = Visibility.Visible;
        }
    }

    private void HideToolbar()
    {
        if (!_config.ToolbarPinned)
        {
            Toolbar.Visibility = Visibility.Collapsed;
        }
    }

    private void OnPinClick(object sender, RoutedEventArgs e)
    {
        _config.ToolbarPinned = PinButton.IsChecked == true;
        SaveConfig();
    }

    // ---- Navigation ----------------------------------------------------------

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        LoadingBar.IsIndeterminate = true;
        LoadingBar.Visibility = Visibility.Visible;

        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri))
        {
            return;
        }

        if (uri.Scheme is "chrome-extension" or "about" or "blob" or "data")
        {
            return;
        }

        if (uri.Scheme is not ("https" or "http"))
        {
            e.Cancel = true;
            _ = Windows.System.Launcher.LaunchUriAsync(uri);
            return;
        }

        // A redirect, a script-driven navigation or a sign-in hop belongs to the
        // site's own flow: sending those to the system browser would strand the
        // session outside the app.
        var staysInApp = _config.IsAllowedHost(uri.Host)
            || e.IsRedirected
            || !e.IsUserInitiated
            || HostRules.LooksLikeSignIn(uri);

        if (!staysInApp)
        {
            e.Cancel = true;
            LoadingBar.Visibility = Visibility.Collapsed;
            _ = Windows.System.Launcher.LaunchUriAsync(uri);
        }
    }

    /// <summary>
    /// Links that ask for a new window are the deliberate "open elsewhere" gesture,
    /// so they take the strict rule: anything off-site leaves for the browser.
    /// </summary>
    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;

        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri))
        {
            return;
        }

        if (_config.IsAllowedHost(uri.Host) || uri.Scheme == "chrome-extension")
        {
            _core?.Navigate(e.Uri);
        }
        else
        {
            _ = Windows.System.Launcher.LaunchUriAsync(uri);
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        LoadingBar.IsIndeterminate = false;
        LoadingBar.Visibility = Visibility.Collapsed;
        UpdateNavigationButtons();
    }

    private void OnHistoryChanged(object? sender, object e) => UpdateNavigationButtons();

    private void OnDocumentTitleChanged(object? sender, object e)
    {
        var title = _core?.DocumentTitle;
        TitleChanged?.Invoke(this, string.IsNullOrWhiteSpace(title) ? _config.Name : title);
    }

    private void UpdateNavigationButtons()
    {
        BackButton.IsEnabled = _core?.CanGoBack ?? false;
        ForwardButton.IsEnabled = _core?.CanGoForward ?? false;
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (_core?.CanGoBack == true)
        {
            _core.GoBack();
        }
    }

    private void OnForwardClick(object sender, RoutedEventArgs e)
    {
        if (_core?.CanGoForward == true)
        {
            _core.GoForward();
        }
    }

    private void OnReloadClick(object sender, RoutedEventArgs e) => _core?.Reload();

    private void OnHomeClick(object sender, RoutedEventArgs e) => _core?.Navigate(_config.StartUrl);

    // ---- Zoom ----------------------------------------------------------------

    private void OnZoomInClick(object sender, RoutedEventArgs e) => _ = StepZoomAsync(1);

    private void OnZoomOutClick(object sender, RoutedEventArgs e) => _ = StepZoomAsync(-1);

    private void OnZoomResetClick(object sender, RoutedEventArgs e) => _ = SetZoomAsync(1.0);

    private Task StepZoomAsync(int direction)
    {
        var index = Array.FindIndex(ZoomSteps, step => step >= _config.Zoom - 0.001);
        if (index < 0)
        {
            index = Array.IndexOf(ZoomSteps, 1.0);
        }

        var next = Math.Clamp(index + direction, 0, ZoomSteps.Length - 1);
        return SetZoomAsync(ZoomSteps[next]);
    }

    private async Task SetZoomAsync(double zoom)
    {
        _config.Zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        SaveConfig();
        UpdateZoomText();
        await ApplyZoomAsync();
    }

    private void UpdateZoomText() =>
        ZoomText.Text = string.Format(CultureInfo.CurrentCulture, "{0:P0}", _config.Zoom);

    /// <summary>
    /// Applies the zoom now and re-registers it for future navigations. WinUI's
    /// WebView2 has no ZoomFactor, so the shell scales the document itself —
    /// Chromium's CSS zoom reflows the layout exactly like browser zoom does.
    /// </summary>
    private async Task ApplyZoomAsync()
    {
        if (_core is null)
        {
            return;
        }

        var factor = _config.Zoom.ToString("0.###", CultureInfo.InvariantCulture);

        if (_zoomScriptId is not null)
        {
            _core.RemoveScriptToExecuteOnDocumentCreated(_zoomScriptId);
        }

        _zoomScriptId = await _core.AddScriptToExecuteOnDocumentCreatedAsync(
            $$"""
              (() => {
                const apply = () => {
                  if (document.documentElement) { document.documentElement.style.zoom = '{{factor}}'; }
                };
                apply();
                document.addEventListener('DOMContentLoaded', apply);
              })();
              """);

        await _core.ExecuteScriptAsync(
            $"if (document.documentElement) document.documentElement.style.zoom = '{factor}';");
    }

    // ---- Extensions ----------------------------------------------------------

    private void OnOpenStoreClick(object sender, RoutedEventArgs e) =>
        _core?.Navigate("https://chromewebstore.google.com/");

    private async void OnInstallExtensionClick(object sender, RoutedEventArgs e)
    {
        if (_profile is null || _core is null)
        {
            return;
        }

        // An empty box means "install what I am looking at" — the common case once
        // the user has browsed to the extension's page inside the app.
        var input = string.IsNullOrWhiteSpace(ExtensionInput.Text)
            ? _core.Source
            : ExtensionInput.Text;

        var id = CrxInstaller.ExtractId(input ?? string.Empty);
        if (id is null)
        {
            ShowExtensionStatus("Ce lien ne correspond pas à une extension du Chrome Web Store.");
            return;
        }

        InstallExtensionButton.IsEnabled = false;
        ShowExtensionStatus("Téléchargement de l'extension…");

        try
        {
            var folder = await CrxInstaller.InstallAsync(
                id,
                MicaPaths.ExtensionsFolder(_config.Slug),
                _core.Environment.BrowserVersionString);

            var extension = await _profile.AddBrowserExtensionAsync(folder);

            ExtensionInput.Text = string.Empty;
            ShowExtensionStatus($"« {extension.Name} » est installée. Rechargez la page pour l'activer.");
            await RefreshExtensionListAsync();
        }
        catch (Exception ex)
        {
            StartupLog.WriteException("Extensions.Install", ex);
            ShowExtensionStatus($"Installation impossible : {ex.Message}");
        }
        finally
        {
            InstallExtensionButton.IsEnabled = true;
        }
    }

    private async void OnRemoveExtensionClick(object sender, RoutedEventArgs e)
    {
        if (_profile is null || sender is not FrameworkElement { Tag: string id })
        {
            return;
        }

        try
        {
            foreach (var extension in await _profile.GetBrowserExtensionsAsync())
            {
                if (extension.Id == id)
                {
                    await extension.RemoveAsync();
                }
            }

            var folder = Path.Combine(MicaPaths.ExtensionsFolder(_config.Slug), id);
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }

            ShowExtensionStatus("Extension retirée.");
            await RefreshExtensionListAsync();
        }
        catch (Exception ex)
        {
            StartupLog.WriteException("Extensions.Remove", ex);
            ShowExtensionStatus($"Suppression impossible : {ex.Message}");
        }
    }

    /// <summary>
    /// Re-adds extensions the user installed previously. The unpacked folders are
    /// ours, so an extension survives a profile reset instead of silently vanishing.
    /// </summary>
    private async Task RestoreExtensionsAsync()
    {
        if (_profile is null)
        {
            return;
        }

        try
        {
            var installed = (await _profile.GetBrowserExtensionsAsync())
                .Select(extension => extension.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var root = MicaPaths.ExtensionsFolder(_config.Slug);
            if (Directory.Exists(root))
            {
                foreach (var folder in Directory.EnumerateDirectories(root))
                {
                    if (!installed.Contains(Path.GetFileName(folder))
                        && File.Exists(Path.Combine(folder, "manifest.json")))
                    {
                        await _profile.AddBrowserExtensionAsync(folder);
                    }
                }
            }

            await RefreshExtensionListAsync();
        }
        catch (Exception ex)
        {
            StartupLog.WriteException("Extensions.Restore", ex);
        }
    }

    private async Task RefreshExtensionListAsync()
    {
        if (_profile is null)
        {
            return;
        }

        var root = MicaPaths.ExtensionsFolder(_config.Slug);
        var extensions = await _profile.GetBrowserExtensionsAsync();

        // Only list what the user installed. WebView2 also reports its own built-in
        // extensions (the PDF viewer, the clipboard helper), which cannot be removed
        // and would just look like clutter the user is responsible for.
        ExtensionList.ItemsSource = extensions
            .Where(extension => Directory.Exists(Path.Combine(root, extension.Id)))
            .Select(extension => new ExtensionEntry(extension.Id, extension.Name))
            .ToList();
    }

    private void ShowExtensionStatus(string message)
    {
        ExtensionStatus.Text = message;
        ExtensionStatus.Visibility = Visibility.Visible;
    }

    private void SaveConfig()
    {
        if (string.IsNullOrEmpty(_config.Slug))
        {
            return;
        }

        try
        {
            _config.Save(MicaPaths.ConfigFile(_config.Slug));
        }
        catch (Exception ex)
        {
            StartupLog.WriteException("Config.Save", ex);
        }
    }

    /// <summary>One row of the installed-extension list.</summary>
    public sealed record ExtensionEntry(string Id, string Name);
}
