using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mica.Core.Localization;

namespace Mica.Builder;

/// <summary>
/// Asks which apps to move, in either direction. Returns the ones the user ticked
/// through <see cref="Selected"/>; the caller decides what that means.
/// </summary>
public sealed partial class TransferDialog : ContentDialog
{
    /// <summary>One app offered for transfer.</summary>
    public sealed class Row(object payload, string name, string detail, bool enabled) : INotifyPropertyChanged
    {
        private bool _isChecked = enabled;

        /// <summary>Whatever the caller needs back: a config on export, an entry on import.</summary>
        public object Payload { get; } = payload;

        public string Name { get; } = name;

        public string Detail { get; } = detail;

        /// <summary>False for an app that cannot be transferred — one already installed.</summary>
        public bool IsEnabled { get; } = enabled;

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value)
                {
                    return;
                }

                _isChecked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        // What the row is called in the automation tree; without it a screen reader
        // announces the type name.
        public override string ToString() => Name;
    }

    private readonly List<Row> _rows;
    private bool _suppressCascade;

    public TransferDialog(string title, string body, string actionLabel, IEnumerable<Row> rows)
    {
        InitializeComponent();

        _rows = [.. rows];

        Title = title;
        BodyText.Text = body;
        PrimaryButtonText = actionLabel;
        CloseButtonText = Strings.Get("Cancel");
        FlowDirection = Strings.FlowDirection;

        SelectAllBox.Content = Strings.Get("SelectAll");
        NoteBar.Title = Strings.Get("TransferNoteTitle");
        NoteBar.Message = Strings.Get("TransferNoteBody");

        Rows.ItemsSource = _rows;
        UpdateSelectAll();
        UpdatePrimaryState();
    }

    /// <summary>The payloads of the ticked rows, in the order they were offered.</summary>
    public List<object> Selected => [.. _rows.Where(row => row.IsChecked).Select(row => row.Payload)];

    private void OnSelectAllClick(object sender, RoutedEventArgs e)
    {
        // Whatever the box shows after the click is what every row should become;
        // an indeterminate state means "not all", so the click resolves to all on.
        var wanted = SelectAllBox.IsChecked == true;

        _suppressCascade = true;

        foreach (var row in _rows.Where(row => row.IsEnabled))
        {
            row.IsChecked = wanted;
        }

        _suppressCascade = false;

        SelectAllBox.IsChecked = wanted;
        UpdatePrimaryState();
    }

    private void OnRowToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressCascade)
        {
            return;
        }

        UpdateSelectAll();
        UpdatePrimaryState();
    }

    private void UpdateSelectAll()
    {
        var transferable = _rows.Where(row => row.IsEnabled).ToList();
        var checkedCount = transferable.Count(row => row.IsChecked);

        _suppressCascade = true;

        SelectAllBox.IsEnabled = transferable.Count > 0;
        SelectAllBox.IsChecked = checkedCount == 0
            ? false
            : checkedCount == transferable.Count ? true : null;

        _suppressCascade = false;
    }

    // An action button that does nothing is worse than one that is visibly not
    // available yet.
    private void UpdatePrimaryState() => IsPrimaryButtonEnabled = _rows.Any(row => row.IsChecked);
}
