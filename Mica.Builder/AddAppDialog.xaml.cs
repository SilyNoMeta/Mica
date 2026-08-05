using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mica.Core;
using Mica.Core.Apps;

namespace Mica.Builder;

/// <summary>
/// Asks for a site address and builds the app from it. Everything else — name,
/// icon, which domains stay inside the app — is read from the site itself, so the
/// common case is one field and one click.
/// </summary>
public sealed partial class AddAppDialog : ContentDialog
{
    /// <summary>The app that was created, or null when the user cancelled.</summary>
    public WebAppConfig? Created { get; private set; }

    public AddAppDialog()
    {
        InitializeComponent();
        PrimaryButtonClick += OnCreateClick;
        Loaded += (_, _) => UrlBox.Focus(FocusState.Programmatic);
    }

    private async void OnCreateClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // The dialog stays open while the site is read; closing it here would kill
        // the work before the app folder is written.
        var deferral = args.GetDeferral();
        args.Cancel = true;

        try
        {
            if (!TryParseUrl(UrlBox.Text, out var url))
            {
                ShowError("Cette adresse n'est pas valide. Exemple : gemini.google.com");
                return;
            }

            SetBusy(true);

            Created = await AppRegistry.CreateAsync(
                url,
                NameBox.Text,
                HostsBox.Text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries));

            args.Cancel = false;
            Hide();
        }
        catch (Exception ex)
        {
            ShowError($"L'app n'a pas pu être créée : {ex.Message}");
        }
        finally
        {
            SetBusy(false);
            deferral.Complete();
        }
    }

    /// <summary>Accepts what people actually type: a bare host, with or without a scheme.</summary>
    private static bool TryParseUrl(string input, out Uri url)
    {
        url = null!;
        input = input.Trim();

        if (input.Length == 0)
        {
            return false;
        }

        if (!input.Contains("://", StringComparison.Ordinal))
        {
            input = "https://" + input;
        }

        return Uri.TryCreate(input, UriKind.Absolute, out url!)
            && url.Scheme is "https" or "http"
            && url.Host.Contains('.', StringComparison.Ordinal);
    }

    private void SetBusy(bool busy)
    {
        BusyPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        IsPrimaryButtonEnabled = !busy;
        UrlBox.IsEnabled = !busy;

        if (busy)
        {
            ErrorBar.IsOpen = false;
        }
    }

    private void ShowError(string message)
    {
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }
}
