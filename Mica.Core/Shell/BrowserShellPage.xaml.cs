using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Mica.Core.Extensions;
using Mica.Core.Integration;
using Mica.Core.Localization;
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
    private string? _detectedExtensionId;
    private bool _installing;
    private bool _started;

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

        // The shell mirrors for right-to-left languages, but the site never does:
        // the page decides its own direction, and forcing it would flip layouts that
        // are already correct.
        FlowDirection = Strings.FlowDirection;
        Browser.FlowDirection = FlowDirection.LeftToRight;

        ApplyStrings();
        UpdateZoomText();
    }

    /// <summary>
    /// Fills in every label and tooltip. The shell lives in a class library, whose
    /// own resources are unreachable at runtime in an unpackaged app, so the strings
    /// come from the executable hosting it rather than from x:Uid.
    /// </summary>
    private void ApplyStrings()
    {
        ToolTipService.SetToolTip(BackButton, Strings.Get("ShellBack"));
        ToolTipService.SetToolTip(ForwardButton, Strings.Get("ShellForward"));
        ToolTipService.SetToolTip(ReloadButton, Strings.Get("ShellReload"));
        ToolTipService.SetToolTip(HomeButton, Strings.Get("ShellHome"));
        ToolTipService.SetToolTip(PinButton, Strings.Get("ShellPinToolbar"));
        ToolTipService.SetToolTip(SettingsButton, Strings.Get("ShellSettings"));

        ZoomHeading.Text = Strings.Get("ShellZoom");
        ToolTipService.SetToolTip(ZoomOutButton, Strings.Get("ShellZoomOut"));
        ToolTipService.SetToolTip(ZoomInButton, Strings.Get("ShellZoomIn"));
        ZoomResetButton.Content = Strings.Get("ShellZoomReset");

        BackgroundHeading.Text = Strings.Get("ShellBackground");
        BackgroundToggle.OnContent = Strings.Get("SwitchOn");
        BackgroundToggle.OffContent = Strings.Get("SwitchOff");
        BackgroundHint.Text = Strings.Get("ShellBackgroundHint");
        BackgroundToggle.IsOn = _config.RunInBackground;

        ExtensionsHeading.Text = Strings.Get("ShellExtensions");
        ExtensionsHint.Text = Strings.Get("ShellExtensionsHint");
        OpenStoreButton.Content = Strings.Get("ShellOpenWebStore");
        ExtensionInput.PlaceholderText = Strings.Get("ShellExtensionLink");
        InstallExtensionButton.Content = Strings.Get("ShellInstall");
        PromptInstallButton.Content = Strings.Get("ShellInstallInApp");
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
            StartupLog.WriteException("ShellStart", ex);
            ErrorText.Text = Strings.Format("ShellStartFailed", ex.Message);
            ErrorOverlay.Visibility = Visibility.Visible;
        }
    }

    private async Task StartAsync()
    {
        StartupLog.Write("Creating WebView2 environment");

        // Extensions can only be turned on when the environment is created.
        var options = new CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = true };

        // Which user data folder an app uses is what decides whether it gets its own
        // browser engine: WebView2 gives every client sharing a folder one browser,
        // GPU and network process between them. Sign-ins stay separate either way —
        // a shared folder still holds one named profile per app.
        var userDataFolder = _config.DedicatedEngine
            ? MicaPaths.ProfileFolder(_config.Slug)
            : MicaPaths.SharedBrowserFolder;

        Directory.CreateDirectory(userDataFolder);
        var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, userDataFolder, options);

        if (_config.DedicatedEngine)
        {
            await Browser.EnsureCoreWebView2Async(environment);
        }
        else
        {
            var controllerOptions = environment.CreateCoreWebView2ControllerOptions();
            controllerOptions.ProfileName = ProfileName(_config.Slug);
            await Browser.EnsureCoreWebView2Async(environment, controllerOptions);
        }

        StartupLog.Write(_config.DedicatedEngine
            ? "Running on a dedicated browser engine"
            : $"Sharing the browser engine, profile '{ProfileName(_config.Slug)}'");

        _core = Browser.CoreWebView2;
        _profile = _core.Profile;
        StartupLog.Write("WebView2 ready");

        ConfigureWebView(_core);

        await _core.AddScriptToExecuteOnDocumentCreatedAsync(ShellScript);
        await ApplyZoomAsync();
        await RestoreExtensionsAsync();

        _notifications.Register(
            MicaPaths.AppUserModelId(_config.Slug),
            _config.Name,
            MicaPaths.IconFile(_config.Slug));
        _notifications.Attach(_core);

        StartupLog.Write($"Navigating to {_config.StartUrl}");
        _core.Navigate(_config.StartUrl);
    }

    /// <summary>
    /// A slug as WebView2 will accept it for a profile name: letters, digits, dashes
    /// and underscores only, not starting or ending with one, at most 64 characters.
    /// Mica's slugs already satisfy this, but a profile name is a stricter contract
    /// than a folder name and a rejected one fails the whole launch.
    /// </summary>
    private static string ProfileName(string slug)
    {
        var cleaned = new string([.. slug.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')])
            .Trim('-', '_');

        if (cleaned.Length > 64)
        {
            cleaned = cleaned[..64].Trim('-', '_');
        }

        return cleaned.Length == 0 ? "default" : cleaned;
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
        webView.WebMessageReceived += OnWebMessageReceived;
        webView.DownloadStarting += OnDownloadStarting;
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

          // Passive: a non-passive wheel listener takes scrolling off the compositor
          // and makes every wheel event wait on the page's main thread. Nothing here
          // needs to cancel the event — the browser's own Ctrl+wheel zoom is already
          // switched off — and scrolling is not worth risking for a zoom gesture.
          addEventListener('wheel', e => {
            if (e.ctrlKey) { post(e.deltaY < 0 ? 'mica:zoom-in' : 'mica:zoom-out'); }
          }, { capture: true, passive: true });

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

    /// <summary>
    /// Raised when the user changes whether the app keeps running once its window is
    /// closed. The window owns the notification-area icon, so it has to react.
    /// </summary>
    public event EventHandler? BackgroundModeChanged;

    private void OnBackgroundToggled(object sender, RoutedEventArgs e)
    {
        if (_config.RunInBackground == BackgroundToggle.IsOn)
        {
            return;
        }

        _config.RunInBackground = BackgroundToggle.IsOn;
        SaveConfig();
        BackgroundModeChanged?.Invoke(this, EventArgs.Empty);
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
        UpdateExtensionPrompt();
    }

    private void OnHistoryChanged(object? sender, object e) => UpdateNavigationButtons();

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

    private void OnInstallExtensionClick(object sender, RoutedEventArgs e)
    {
        // An empty box means "install what I am looking at" — the common case once
        // the user has browsed to the extension's page inside the app.
        var input = string.IsNullOrWhiteSpace(ExtensionInput.Text)
            ? _core?.Source
            : ExtensionInput.Text;

        if (CrxInstaller.ExtractId(input ?? string.Empty) is not { } id)
        {
            ShowExtensionStatus(Strings.Get("ShellNotAnExtensionLink"));
            return;
        }

        _ = InstallExtensionAsync(id);
    }

    private void OnInstallDetectedClick(object sender, RoutedEventArgs e)
    {
        if (_detectedExtensionId is { } id)
        {
            _ = InstallExtensionAsync(id);
        }
    }

    /// <summary>
    /// The store's own "Add to Chrome" button cannot install anything here — inside an
    /// embedded browser it degrades to downloading the package, which then fails as an
    /// interrupted download. Catch that download and run the real install instead.
    /// </summary>
    private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        if (CrxInstaller.ExtractId(e.DownloadOperation.Uri) is not { } id)
        {
            return;
        }

        e.Cancel = true;
        _ = InstallExtensionAsync(id);
    }

    /// <summary>Offers a one-click install while the user is on an extension's store page.</summary>
    private void UpdateExtensionPrompt()
    {
        if (_installing)
        {
            return;
        }

        var source = _core?.Source;
        var onStorePage = source is not null
            && source.Contains("chromewebstore.google.com/detail/", StringComparison.OrdinalIgnoreCase);

        _detectedExtensionId = onStorePage ? CrxInstaller.ExtractId(source!) : null;

        if (_detectedExtensionId is null)
        {
            ExtensionPrompt.IsOpen = false;
            return;
        }

        ShowPrompt(
            InfoBarSeverity.Informational,
            Strings.Get("ShellDetectedTitle"),
            Strings.Format("ShellDetectedBody", _config.Name),
            showAction: true);
    }

    private async Task InstallExtensionAsync(string id)
    {
        if (_profile is null || _core is null || _installing)
        {
            return;
        }

        _installing = true;
        InstallExtensionButton.IsEnabled = false;
        ShowPrompt(
            InfoBarSeverity.Informational,
            Strings.Get("ShellInstallingTitle"),
            Strings.Get("ShellDownloading"),
            showAction: false);
        ShowExtensionStatus(Strings.Get("ShellDownloading"));

        try
        {
            var folder = await CrxInstaller.InstallAsync(
                id,
                MicaPaths.ExtensionsFolder(_config.Slug),
                _core.Environment.BrowserVersionString);

            var extension = await _profile.AddBrowserExtensionAsync(folder);

            ExtensionInput.Text = string.Empty;
            _detectedExtensionId = null;

            var installed = Strings.Format("ShellInstalledTitle", extension.Name);
            var activate = Strings.Get("ShellReloadToActivate");

            ShowPrompt(InfoBarSeverity.Success, installed, activate, showAction: false);
            ShowExtensionStatus($"{installed} {activate}");

            await RefreshExtensionListAsync();
        }
        catch (Exception ex)
        {
            StartupLog.WriteException("Extensions.Install", ex);
            ShowPrompt(InfoBarSeverity.Error, Strings.Get("ShellInstallFailedTitle"), ex.Message, showAction: false);
            ShowExtensionStatus($"{Strings.Get("ShellInstallFailedTitle")} : {ex.Message}");
        }
        finally
        {
            _installing = false;
            InstallExtensionButton.IsEnabled = true;
        }
    }

    private void ShowPrompt(InfoBarSeverity severity, string title, string message, bool showAction)
    {
        ExtensionPrompt.Severity = severity;
        ExtensionPrompt.Title = title;
        ExtensionPrompt.Message = message;
        PromptInstallButton.Visibility = showAction ? Visibility.Visible : Visibility.Collapsed;
        ExtensionPrompt.IsOpen = true;
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

            ShowExtensionStatus(Strings.Get("ShellExtensionRemoved"));
            await RefreshExtensionListAsync();
        }
        catch (Exception ex)
        {
            StartupLog.WriteException("Extensions.Remove", ex);
            ShowExtensionStatus($"{Strings.Get("ShellRemoveFailedTitle")} : {ex.Message}");
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
        var removeLabel = Strings.Get("ShellRemoveExtension");

        ExtensionList.ItemsSource = extensions
            .Where(extension => Directory.Exists(Path.Combine(root, extension.Id)))
            .Select(extension => new ExtensionEntry(extension.Id, extension.Name, removeLabel))
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

    /// <summary>
    /// One row of the installed-extension list. The remove tooltip travels with the
    /// row because a DataTemplate inside a class library cannot reach the resources.
    /// </summary>
    public sealed record ExtensionEntry(string Id, string Name, string RemoveLabel);
}
