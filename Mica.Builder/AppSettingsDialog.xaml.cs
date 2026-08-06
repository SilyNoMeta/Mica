using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mica.Core;
using Mica.Core.Apps;
using Mica.Core.Localization;

namespace Mica.Builder;

/// <summary>
/// The per-app settings Mica owns: whether the app keeps running once closed, and
/// whether Windows starts it. Both are properties of the installation rather than of
/// the site, which is why they live here beside the shortcut and the uninstall entry
/// — and why they can be changed without opening the app.
/// </summary>
public sealed partial class AppSettingsDialog : ContentDialog
{
    private readonly WebAppConfig _config;
    private bool _loading = true;

    public AppSettingsDialog(WebAppConfig config)
    {
        _config = config;
        InitializeComponent();

        FlowDirection = Strings.FlowDirection;
        ApplyStrings();

        BackgroundToggle.IsOn = config.RunInBackground;
        DedicatedToggle.IsOn = config.DedicatedEngine;

        // Read the registry rather than the config: the user may have switched the
        // app off in Windows' own startup settings, and that decision wins.
        StartupToggle.IsOn = StartupRegistration.IsEnabled(config);

        _loading = false;
    }

    private void ApplyStrings()
    {
        Title = Strings.Format("AppSettingsTitle", _config.Name);
        PrimaryButtonText = Strings.Get("Done");

        BackgroundToggle.Header = Strings.Get("SettingsBackground");
        BackgroundToggle.OnContent = Strings.Get("SwitchOn");
        BackgroundToggle.OffContent = Strings.Get("SwitchOff");
        BackgroundHint.Text = Strings.Get("SettingsBackgroundHint");

        StartupToggle.Header = Strings.Get("SettingsStartup");
        StartupToggle.OnContent = Strings.Get("SwitchOn");
        StartupToggle.OffContent = Strings.Get("SwitchOff");
        StartupHint.Text = Strings.Get("SettingsStartupHint");

        DedicatedToggle.Header = Strings.Get("SettingsDedicated");
        DedicatedToggle.OnContent = Strings.Get("SwitchOn");
        DedicatedToggle.OffContent = Strings.Get("SwitchOff");
        DedicatedHint.Text = Strings.Get("SettingsDedicatedHint");
    }

    private void OnBackgroundToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _config.RunInBackground = BackgroundToggle.IsOn;
        Save();
    }

    private void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        StartupRegistration.Set(_config, StartupToggle.IsOn);
        _config.StartWithWindows = StartupToggle.IsOn;
        Save();
    }

    private void OnDedicatedToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _config.DedicatedEngine = DedicatedToggle.IsOn;
        Save();
    }

    private void Save() => _config.Save(MicaPaths.ConfigFile(_config.Slug));
}
