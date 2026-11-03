using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GlassRadio;

public sealed partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        RootDataContext();
        _ = _viewModel.LoadAsync(CancellationToken.None);
    }

    private void RootDataContext()
    {
        if (Content is FrameworkElement element) element.DataContext = _viewModel;
    }

    private void SettingsNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && Enum.TryParse<SettingsSection>(item.Tag?.ToString(), out var section))
            _viewModel.ActiveSection = section;
    }
}
