using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using Microsoft.UI.Text;
using GlassRadio.Domain;
using System.ComponentModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;
using Windows.UI;

namespace GlassRadio;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly Func<Window> _settingsFactory;
    private bool _leftDragging;
    private bool _rightDragging;
    private double _leftDragStartX;
    private double _leftDragStartWidth;
    private double _rightDragStartX;
    private double _rightDragStartWidth;
    private bool _layoutInitialized;
    private bool _lastCompact;
    private bool _lastMedium;
    private bool _lastLeftHidden;
    private bool _lastRightHidden;
    private bool _paneWidthsUserAdjusted;

    public MainWindow(MainViewModel viewModel, Func<Window> settingsFactory)
    {
        _viewModel = viewModel;
        _settingsFactory = settingsFactory;
        InitializeComponent();
        Root.DataContext = viewModel;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragRegion);
        ConfigureTitleBar();
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        viewModel.LayoutSettingsLoaded += ViewModel_LayoutSettingsLoaded;
        Closed += OnClosed;
        UpdateResponsiveLayout(Root.ActualWidth);
        _ = _viewModel.InitializeCommand.ExecuteAsync(null);
    }

    private void Root_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateResponsiveLayout(e.NewSize.Width);

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // The persisted slider value is loaded before the window is created, but WinUI can
        // materialize the first media-control binding afterwards. Reapply at the window boundary
        // so the visible value and the engine's first output are guaranteed to agree.
        await _viewModel.ReapplyVolumeAsync();
        // WinUI can create the native audio output one dispatcher turn after Loaded. Apply once
        // more after that turn so the saved level is audible immediately, without requiring the
        // user to touch the slider.
        await Task.Delay(250);
        await _viewModel.ReapplyVolumeAsync();
    }

    private void ConfigureTitleBar()
    {
        // Belt-and-suspenders for this self-contained, unpackaged app: <ApplicationIcon> in the
        // csproj embeds the icon into the compiled exe (taskbar/Explorer/Alt+Tab), but the running
        // AppWindow's own icon is set separately at runtime.
        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "AppIcon.ico");
        if (System.IO.File.Exists(iconPath)) AppWindow.SetIcon(iconPath);

        if (!AppWindowTitleBar.IsCustomizationSupported()) return;

        var titleBar = AppWindow.TitleBar;
        titleBar.ExtendsContentIntoTitleBar = true;
        var dark = Root.ActualTheme != ElementTheme.Light;
        var background = dark ? Color.FromArgb(255, 36, 36, 36) : Color.FromArgb(255, 239, 239, 239);
        var foreground = dark ? Color.FromArgb(255, 245, 245, 245) : Color.FromArgb(255, 32, 32, 32);
        titleBar.BackgroundColor = background;
        titleBar.InactiveBackgroundColor = background;
        titleBar.ForegroundColor = foreground;
        titleBar.InactiveForegroundColor = foreground;
        titleBar.ButtonBackgroundColor = Color.FromArgb(0, 0, 0, 0);
        titleBar.ButtonInactiveBackgroundColor = Color.FromArgb(0, 0, 0, 0);
        titleBar.ButtonHoverBackgroundColor = dark ? Color.FromArgb(35, 255, 255, 255) : Color.FromArgb(35, 0, 0, 0);
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = foreground;
        titleBar.ButtonHoverForegroundColor = foreground;
    }

    private void UpdateResponsiveLayout(double width)
    {
        // The Mac layout is intentionally information-dense, but Windows must remain
        // usable on smaller displays. Keep the full three-column view when it fits,
        // remove the countries rail before squeezing the catalogue, then collapse
        // the library rail on compact windows. The same content remains reachable
        // through Home/Search/Discover and the country selector in onboarding.
        var compact = width < 900;
        var medium = width < 1250;
        // A narrow window and an explicit user toggle both hide a pane the same way - there is
        // one combined "is this pane hidden" state, not two separate mechanisms fighting over
        // Visibility/column width.
        var leftHidden = compact || !_viewModel.ShowFavoritesNav;
        var rightHidden = medium || !_viewModel.ShowCountriesPanel;

        var pagePadding = GetLayoutThickness(compact ? "GlassPagePaddingCompact" : medium ? "GlassPagePaddingMedium" : "GlassPagePadding");
        const double titleBarHeight = 44;
        Root.Padding = new Thickness(pagePadding.Left, pagePadding.Top + titleBarHeight, pagePadding.Right, pagePadding.Bottom);
        AppTitleBar.Margin = new Thickness(-pagePadding.Left, -(pagePadding.Top + titleBarHeight), -pagePadding.Right, 0);
        // AppTitleBar's position just moved (it may move on every call, since the margin above is
        // recomputed from pagePadding each time) - re-register the drag region so Windows doesn't
        // keep hit-testing against a now-stale rectangle from before the move. This is the known
        // fix for the "blank titlebar space doesn't drag" class of bug; see truth/windows-build-
        // plan.md's "unified titlebar drag surface" entry, which left this exact behaviour as an
        // open, never-closed manual-verification gate.
        SetTitleBar(TitleBarDragRegion);
        QuickSearchBox.Width = compact
            ? Math.Clamp(width - 380, 120, 220)
            : medium ? 240 : 300;

        // A Mac split view keeps the user's pane widths while the window is
        // resized. Only apply breakpoint/toggle defaults when the combined hidden state actually
        // changes; otherwise an ordinary resize must not undo a drag operation.
        var hiddenStateChanged = !_layoutInitialized || leftHidden != _lastLeftHidden || rightHidden != _lastRightHidden;
        if (hiddenStateChanged)
        {
            // A pane can be temporarily collapsed by a breakpoint or a user toggle. When it
            // returns, restore its current/saved width instead of reintroducing a breakpoint
            // default and making the user's split feel lost.
            var leftWidth = LeftColumn.Width.Value > 0 ? LeftColumn.Width.Value : _viewModel.LeftPaneWidth;
            var rightWidth = RightColumn.Width.Value > 0 ? RightColumn.Width.Value : _viewModel.RightPaneWidth;
            LeftColumn.Width = leftHidden ? new GridLength(0) : new GridLength(Math.Clamp(leftWidth, 180, 340));
            RightColumn.Width = rightHidden ? new GridLength(0) : new GridLength(Math.Clamp(rightWidth, 220, 380));
            _lastCompact = compact;
            _lastMedium = medium;
            _lastLeftHidden = leftHidden;
            _lastRightHidden = rightHidden;
            _layoutInitialized = true;
        }
        else
        {
            if (leftHidden) LeftColumn.Width = new GridLength(0);
            if (rightHidden) RightColumn.Width = new GridLength(0);
        }
        SidebarPanel.Visibility = leftHidden ? Visibility.Collapsed : Visibility.Visible;
        CountriesPanel.Visibility = rightHidden ? Visibility.Collapsed : Visibility.Visible;
        LeftSplitter.Visibility = leftHidden ? Visibility.Collapsed : Visibility.Visible;
        RightSplitter.Visibility = rightHidden ? Visibility.Collapsed : Visibility.Visible;

        UpdateContentMetrics(width, compact, medium);

        // Deferred one dispatch cycle so this reads POST-layout bounds for QuickSearchBox (its
        // Width was just set above) and ViewControlButton (AppTitleBar's margin was just set
        // above too) - reading synchronously here would use stale pre-layout bounds.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateTitleBarInteractiveRegions);
    }

    // SetTitleBar(element) alone is not sufficient for interactive controls placed inside the
    // title bar: it defines the drag surface, but WinUI 3 does not reliably treat an overlapping
    // sibling that merely renders on top (by z-order) as exempt from that drag surface - this is
    // a documented, known WinUI issue (microsoft/microsoft-ui-xaml#8496 and others), not a timing
    // quirk in this app's own code, confirmed against Microsoft's own title-bar customization
    // docs before writing this. The documented fix is InputNonClientPointerSource.SetRegionRects
    // with NonClientRegionKind.Passthrough, recalculated whenever the interactive elements' real
    // on-screen bounds can have changed.
    private void UpdateTitleBarInteractiveRegions()
    {
        if (!ExtendsContentIntoTitleBar) return;
        var xamlRoot = Root.XamlRoot;
        if (xamlRoot is null) return;

        var scale = xamlRoot.RasterizationScale;
        var rects = new List<Windows.Graphics.RectInt32>(2);
        foreach (var element in new FrameworkElement[] { ViewControlButton, QuickSearchBox })
        {
            if (element.ActualWidth <= 0 || element.ActualHeight <= 0) continue;
            var bounds = element.TransformToVisual(null).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            rects.Add(new Windows.Graphics.RectInt32(
                (int)Math.Round(bounds.X * scale),
                (int)Math.Round(bounds.Y * scale),
                (int)Math.Round(bounds.Width * scale),
                (int)Math.Round(bounds.Height * scale)));
        }
        if (rects.Count == 0) return;

        InputNonClientPointerSource.GetForWindowId(AppWindow.Id).SetRegionRects(NonClientRegionKind.Passthrough, rects.ToArray());
    }

    private void UpdateContentMetrics(double width, bool compact, bool medium)
    {
        var leftWidth = compact ? 0 : LeftColumn.Width.Value;
        var rightWidth = medium ? 0 : RightColumn.Width.Value;
        // No dedicated splitter columns exist - the splitters are edge-anchored overlays that
        // live inside LeftColumn/RightColumn themselves, so CenterColumn's own width already
        // accounts for them. -24 is the content Grid's own left+right breathing-room margin
        // (12px each side, independent of splitter position), matching the
        // CenterColumn.ActualWidth - 24 fallback below so both paths agree on the same width.
        var estimatedCentreWidth = width - Root.Padding.Left - Root.Padding.Right - leftWidth - rightWidth - 24;
        // During a splitter drag, Grid has not necessarily arranged CenterColumn for
        // the current pointer position yet. Using that stale ActualWidth makes the
        // catalogue collapse one breakpoint too early and leaves the newly available
        // space empty. The column geometry is authoritative here because it already
        // includes the current splitter widths; ActualWidth is only a fallback during
        // the very first measure pass when the window width is not usable yet.
        var centreWidth = Math.Max(320, estimatedCentreWidth > 320
            ? estimatedCentreWidth
            : CenterColumn.ActualWidth - 24);

        // Keep the bounded Explore frame and its inner wrap geometry on the same effective
        // viewport width. ExploreViewportFrame owns symmetric * / Auto / * centering, so the
        // content frame must not carry a manually calculated left margin that can drift when
        // the vertical scrollbar or splitters are measured in a different pass.
        ApplyExploreViewportMetrics(centreWidth);

        // These bands mirror the Mac source's available-centre-width contract. The previous
        // The Mac catalogue is a vertically scrolling LazyVGrid: three columns
        // wide, two at medium, and one when compact. ItemsWrapGrid's horizontal
        // orientation fills each row across the available width and preserves
        // that vertical scroll contract.
        // Derive catalogue columns from the rendered centre width, not from the
        // total window width. Side panes can consume enough space to make the
        // old total-width breakpoint collapse the catalogue too early.
        var columnCount = centreWidth >= 900 ? 3 : centreWidth >= 600 ? 2 : 1;
        _viewModel.StationGridColumns = columnCount;
        var columnSpacing = 12d;
        var availableCardWidth = (centreWidth - ((columnCount - 1) * columnSpacing) - 8) / columnCount;
        _viewModel.ListCardWidth = Math.Clamp(availableCardWidth, 320, 620);

        // Match the Mac LazyVGrid adaptive contract: cards have a 168px minimum,
        // 14px spacing, and expand to fill the centred rail. Cap the wide layout at
        // eight columns so the curated surface remains a deliberate slot grid rather
        // than turning into a wall of tiny cards on ultrawide windows.
        _viewModel.FeaturedGridColumns = Math.Clamp(
            (int)Math.Floor((centreWidth + 14) / (168 + 14)),
            1,
            8);
        _viewModel.TopCardWidth = Math.Clamp(
            (centreWidth - ((_viewModel.FeaturedGridColumns - 1) * 14)) / _viewModel.FeaturedGridColumns,
            168,
            210);
        // Match the Mac genre geometry while keeping the longest category label
        // readable. Chips are deliberately uniform; when the rail is narrower
        // than the complete set, it scrolls instead of shrinking labels.
        // The Mac featured rail is a content-sized two-row presentation. Do not
        // derive its height from the window height: that makes the genre rail drift
        // toward the middle whenever the user resizes vertically.
        var featuredCardHeight = _viewModel.TopCardWidth + 78;
        // This runs on every resize/drag and would otherwise stomp an XAML-declared Auto default
        // the instant the window moves - so the ShowTopStations=false case is handled here, not
        // left to a one-time XAML value.
        FeaturedRailRow.Height = _viewModel.ShowTopStations
            ? new GridLength(Math.Clamp((featuredCardHeight * 2) + 16, 480, 580))
            : GridLength.Auto;
        _viewModel.ApplyLayoutWidths();
        PlayerBar.Padding = GetLayoutThickness(compact ? "GlassPlayerPaddingCompact" : "GlassPlayerPadding");
        PlayerBar.Margin = new Thickness(0, 0, 0, compact ? 6 : 8);
        // Match the Mac safeAreaInset player: a centered floating pill with a
        // deliberately bounded width. It must never become a second footer row.
        var playerAvailableWidth = Math.Max(340, centreWidth - 24);
        PlayerStatusPanel.Width = compact ? 48 : 270;
        PlayerStatusTextPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        PlayerFavoriteButton.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        PlayerBar.MinWidth = compact ? 0 : Math.Min(720, playerAvailableWidth);
        PlayerBar.Width = compact
            ? playerAvailableWidth
            : Math.Clamp(playerAvailableWidth, 420, 920);
        PlayerBar.MaxWidth = compact ? playerAvailableWidth : 920;
        PlayerMeterPanel.Visibility = compact || centreWidth < 900 ? Visibility.Collapsed : Visibility.Visible;
        PlayerVolumePanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ExploreScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0)
        {
            return;
        }

        // The parent-column estimate can be one layout pass behind the ScrollViewer. Re-anchor
        // every dependent Explore measurement to the viewport that is actually being rendered.
        ApplyExploreViewportMetrics(e.NewSize.Width);
    }

    private void ApplyExploreViewportMetrics(double outerWidth)
    {
        // Deliberately NOT ExploreScrollViewer.ViewportWidth. ViewportWidth shrinks whenever the
        // vertical scrollbar is showing, and the scrollbar's visibility depends on this method's
        // own output: it sets ExploreCategoryColumns/Rows below, which sets the category grid's
        // content height, which determines whether a scrollbar is needed at all. Reading
        // ViewportWidth here closed that loop - a SizeChanged pass could change the column count,
        // which changed content height, which toggled the scrollbar, which changed ViewportWidth,
        // which re-fired SizeChanged - a real oscillation, not a one-off glitch, that made Explore
        // fight the user's scroll input instead of just scrolling. The outer width this replaced
        // it with is stable: it comes from the window/pane size, not from this control's own
        // content, so it cannot feed back into itself. Always reserving a scrollbar-sized gutter
        // (whether or not a scrollbar is actually showing) keeps centering correct without
        // reintroducing the left/right drift a fixed-but-unreserved outer width caused before.
        const double ReservedScrollbarGutter = 16;
        var effectiveViewportWidth = Math.Max(320, outerWidth - ReservedScrollbarGutter);
        var boundedWidth = Math.Min(1600, effectiveViewportWidth);
        // Give the centering frame the exact viewport width. Without an explicit arranged
        // width, ScrollViewer can measure the frame at its desired size and leave the bounded
        // Explore surface anchored to the left when a side pane or vertical scrollbar changes.
        ExploreViewportFrame.Width = effectiveViewportWidth;
        ExploreContentFrame.Width = boundedWidth;
        ExploreContentFrame.Margin = new Thickness(0);

        // Explore categories are a real wrap grid, not a horizontally clipped chip rail.
        // ItemsWrapGrid's vertical orientation lets us cap the number of rows so each
        // column is laid out as a complete unit; that avoids a partial column appearing
        // when the ScrollViewer is measured through a resize pass.
        var categoryContentWidth = Math.Max(168, boundedWidth - 32);
        _viewModel.ExploreCategoryColumns = Math.Clamp(
            (int)Math.Floor((categoryContentWidth + 8) / (168 + 8)),
            1,
            8);
        _viewModel.ExploreCategoryRows = Math.Max(
            1,
            (int)Math.Ceiling(Math.Max(1, _viewModel.Genres.Count) / (double)_viewModel.ExploreCategoryColumns));

        // Give the ListView the exact measured tile block width. This avoids relying on
        // ItemsWrapGrid's desired-size origin, which is the source of the recurring left lean.
        _viewModel.ExploreCategoryGridWidth = Math.Max(
            168,
            (_viewModel.ExploreCategoryColumns * (168 + 8)) - 8);
        _viewModel.RebuildExploreGenreLayout();
    }

    private static Thickness GetLayoutThickness(string key) =>
        Microsoft.UI.Xaml.Application.Current.Resources[key] is Thickness thickness ? thickness : new Thickness(8);

    private void LeftSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (LeftColumn.Width.Value <= 0) return;
        _leftDragging = true;
        _leftDragStartX = e.GetCurrentPoint(Root).Position.X;
        _leftDragStartWidth = LeftColumn.Width.Value;
        // A Border does not auto-focus on press the way a Button does; without this the
        // splitter is draggable but keyboard arrow-resize silently targets whatever last
        // had focus instead.
        LeftSplitter.Focus(FocusState.Pointer);
        (sender as UIElement ?? LeftSplitter).CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void LeftSplitter_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_leftDragging) return;
        var delta = e.GetCurrentPoint(Root).Position.X - _leftDragStartX;
        LeftColumn.Width = new GridLength(Math.Clamp(_leftDragStartWidth + delta, 180, 340));
        UpdateContentMetrics(Root.ActualWidth, false, RightColumn.Width.Value <= 0);
        e.Handled = true;
    }

    private void LeftSplitter_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_leftDragging) return;
        _leftDragging = false;
        (sender as UIElement ?? LeftSplitter).ReleasePointerCapture(e.Pointer);
        CommitPaneWidths();
        e.Handled = true;
    }

    private void MiniGame_CloseRequested(object? sender, EventArgs e) => _viewModel.IsMiniGamePresented = false;

    private void LeftSplitter_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (!_leftDragging) return;
        _leftDragging = false;
        CommitPaneWidths();
        SetSplitterLineHover(LeftSplitterLine, false);
    }

    private void LeftSplitter_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        SetSplitterLineHover(LeftSplitterLine, true);
    }

    private void LeftSplitter_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_leftDragging) SetSplitterLineHover(LeftSplitterLine, false);
    }

    // Must match LeftSplitterLine/RightSplitterLine's Opacity in MainWindow.xaml - the rest
    // state is meant to blend almost fully into the background; only hover/drag should reveal
    // the accent colour.
    private const double SplitterRestOpacity = 0.08;

    private static void SetSplitterLineHover(Microsoft.UI.Xaml.Shapes.Rectangle line, bool hovered)
    {
        line.Opacity = hovered ? 1.0 : SplitterRestOpacity;
        line.Fill = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources[hovered ? "GlassAccentBrush" : "GlassMutedTextBrush"];
    }

    private void LeftSplitter_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var delta = e.Key switch
        {
            VirtualKey.Left => -16,
            VirtualKey.Right => 16,
            _ => 0
        };
        if (delta == 0 || LeftColumn.Width.Value <= 0) return;

        LeftColumn.Width = new GridLength(Math.Clamp(LeftColumn.Width.Value + delta, 180, 340));
        _paneWidthsUserAdjusted = true;
        _viewModel.SavePaneWidths(LeftColumn.Width.Value, RightColumn.Width.Value);
        UpdateContentMetrics(Root.ActualWidth, false, RightColumn.Width.Value <= 0);
        e.Handled = true;
    }

    private void RightSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (RightColumn.Width.Value <= 0) return;
        _rightDragging = true;
        _rightDragStartX = e.GetCurrentPoint(Root).Position.X;
        _rightDragStartWidth = RightColumn.Width.Value;
        RightSplitter.Focus(FocusState.Pointer);
        (sender as UIElement ?? RightSplitter).CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void RightSplitter_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_rightDragging) return;
        var delta = _rightDragStartX - e.GetCurrentPoint(Root).Position.X;
        RightColumn.Width = new GridLength(Math.Clamp(_rightDragStartWidth + delta, 220, 380));
        UpdateContentMetrics(Root.ActualWidth, false, false);
        e.Handled = true;
    }

    private void RightSplitter_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_rightDragging) return;
        _rightDragging = false;
        (sender as UIElement ?? RightSplitter).ReleasePointerCapture(e.Pointer);
        CommitPaneWidths();
        e.Handled = true;
    }

    private void RightSplitter_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (!_rightDragging) return;
        _rightDragging = false;
        CommitPaneWidths();
        SetSplitterLineHover(RightSplitterLine, false);
    }

    private void CommitPaneWidths()
    {
        _paneWidthsUserAdjusted = true;
        _viewModel.SavePaneWidths(LeftColumn.Width.Value, RightColumn.Width.Value);
    }

    private void RightSplitter_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        SetSplitterLineHover(RightSplitterLine, true);
    }

    private void RightSplitter_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_rightDragging) SetSplitterLineHover(RightSplitterLine, false);
    }

    private void RightSplitter_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var delta = e.Key switch
        {
            VirtualKey.Left => 16,
            VirtualKey.Right => -16,
            _ => 0
        };
        if (delta == 0 || RightColumn.Width.Value <= 0) return;

        RightColumn.Width = new GridLength(Math.Clamp(RightColumn.Width.Value + delta, 220, 380));
        _paneWidthsUserAdjusted = true;
        _viewModel.SavePaneWidths(LeftColumn.Width.Value, RightColumn.Width.Value);
        UpdateContentMetrics(Root.ActualWidth, false, false);
        e.Handled = true;
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => _settingsFactory().Activate();

    private void CopyStationName_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuFlyoutItem)?.Tag is not RadioStation station) return;
        var package = new DataPackage();
        package.SetText(station.Name);
        Clipboard.SetContent(package);
        _viewModel.StatusText = $"Copied {station.Name}.";
    }

    private void RemoveFavorite_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuFlyoutItem)?.Tag is RadioStation station)
            _viewModel.ToggleFavoriteCommand.Execute(station);
    }

    private void FavoriteStation_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is StationItemViewModel row)
            _ = _viewModel.PlayCommand.ExecuteAsync(row.Station);
        e.Handled = true;
    }

    private void FavoriteStations_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs e)
    {
        // WinUI can visually complete a reorder without mutating the backing
        // ObservableCollection consistently across virtualization boundaries.
        // Commit the final rendered order explicitly so the preset rail and its
        // persisted store cannot drift apart.
        var orderedIds = sender.Items
            .OfType<StationItemViewModel>()
            .Select(static row => row.Station.Id)
            .ToArray();
        _viewModel.ReorderFavorites(orderedIds);
    }

    private async void OpenStationWebsite_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuFlyoutItem)?.Tag is not RadioStation station ||
            !Uri.TryCreate(station.Homepage, UriKind.Absolute, out var homepage) ||
            (!string.Equals(homepage.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(homepage.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            _viewModel.StatusText = "This station has no valid website.";
            return;
        }

        await Launcher.LaunchUriAsync(homepage);
    }

    private void SearchAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        QuickSearchBox.Focus(FocusState.Keyboard);
        args.Handled = true;
    }

    private void StationPlay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RadioStation station })
            _ = _viewModel.PlayCommand.ExecuteAsync(station);
    }

    private void ExploreStation_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is Border { DataContext: StationItemViewModel row })
            _viewModel.OpenExploreStationDetails(row.Station);
    }

    private async void ExploreGenre_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.FirstOrDefault() is string genre)
            await _viewModel.ExploreGenreCommand.ExecuteAsync(genre);
    }

    // Shared by every list-style station card (All Stations list, "Top in selected country") that
    // shows its play icon only on hover, as an overlay on the artwork - hovering the WHOLE card
    // reveals it, not just the small artwork area, per direct user feedback aligning the two list
    // views' behaviour. The hover handler is wired on the outer Button in each template, but the
    // named overlay lives a few levels deep and at a different depth in each template's markup, so
    // this walks the actual visual tree by name rather than assuming a fixed Children index -
    // robust to the two templates having different shapes, unlike indexing into a specific child
    // position would be.
    private void StationCard_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not DependencyObject root) return;
        if (FindDescendantByName(root, "PlayHoverOverlay") is { } overlay)
            overlay.Opacity = 1;
        if (FindDescendantByName(root, "TopCountryRowSurface") is not null)
        {
            NormalizeTopCountryRows(root);
            SetFlankSurface(root, "TopCountryRowSurface", "GlassFlankHoverBrush");
        }
    }

    private void StationCard_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not DependencyObject root) return;
        if (FindDescendantByName(root, "PlayHoverOverlay") is { } overlay)
            overlay.Opacity = 0;
        if (FindDescendantByName(root, "TopCountryRowSurface") is not null)
            NormalizeTopCountryRows();
    }

    private void FlankNav_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        SetFlankSurface(sender, "NavHoverHomeSurface", "GlassFlankHoverBrush");
        SetFlankSurface(sender, "NavHoverExploreSurface", "GlassFlankHoverBrush");
    }

    private void FlankNav_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        SetFlankSurface(sender, "NavHoverHomeSurface", null);
        SetFlankSurface(sender, "NavHoverExploreSurface", null);
    }

    private void FlankRow_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        NormalizeFavoriteRows(sender as DependencyObject);
        SetFlankSurface(sender, "FavouriteRowSurface", "GlassFlankHoverBrush");
    }

    private void FlankRow_PointerExited(object sender, PointerRoutedEventArgs e)
        => NormalizeFavoriteRows();

    private void NormalizeTopCountryRows(DependencyObject? hovered = null)
    {
        if (TopStationsListView is null) return;

        foreach (var row in _viewModel.TopStations)
        {
            if (TopStationsListView.ContainerFromItem(row) is not ListViewItem item ||
                FindDescendantByName(item, "TopCountryRowSurface") is not Border surface)
                continue;

            if (hovered is not null && ReferenceEquals(surface, hovered)) continue;
            SetFlankSurface(surface, "TopCountryRowSurface", row.IsPlaying ? "GlassFlankActiveBrush" : null);
        }
    }

    private void NormalizeFavoriteRows(DependencyObject? hovered = null)
    {
        if (FavoriteStationsListView is null) return;

        foreach (var row in _viewModel.FavoriteRows)
        {
            if (FavoriteStationsListView.ContainerFromItem(row) is not ListViewItem item ||
                FindDescendantByName(item, "FavouriteRowSurface") is not Border surface)
                continue;

            if (hovered is not null && ReferenceEquals(surface, hovered)) continue;
            SetFlankSurface(surface, "FavouriteRowSurface", row.IsPlaying ? "GlassFlankActiveBrush" : null);
        }
    }

    private static void SetFlankSurface(object sender, string surfaceName, string? brushKey)
    {
        if (sender is not DependencyObject root || FindDescendantByName(root, surfaceName) is not Border surface)
            return;

        surface.Background = brushKey is null
            ? null
            : Microsoft.UI.Xaml.Application.Current.Resources[brushKey] as Brush;
    }

    private static FrameworkElement? FindDescendantByName(DependencyObject root, string name)
    {
        if (root is FrameworkElement { Name: var rootName } && rootName == name)
            return (FrameworkElement)root;

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement { } fe && fe.Name == name) return fe;
            if (FindDescendantByName(child, name) is { } found) return found;
        }
        return null;
    }

    private void CountryLoad_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CountryPreset country })
            _ = _viewModel.LoadCountryCommand.ExecuteAsync(country);
    }

    private void CountryLoad_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CountryPreset country)
            _ = _viewModel.LoadCountryCommand.ExecuteAsync(country);
        e.Handled = true;
    }

    private void TopStation_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is StationItemViewModel row)
            _ = _viewModel.PlayCommand.ExecuteAsync(row.Station);
        e.Handled = true;
    }

    private void CountriesListView_Loaded(object sender, RoutedEventArgs e)
        => UpdateCountrySelectionVisuals();

    private void Countries_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateCountrySelectionVisuals();

    private void UpdateCountrySelectionVisuals()
    {
        if (CountriesListView is null) return;

        foreach (var country in _viewModel.Countries)
        {
            if (CountriesListView.ContainerFromItem(country) is ListViewItem item &&
                FindDescendantByName(item, "CountryName") is TextBlock name)
            {
                name.FontWeight = item.IsSelected ? FontWeights.Bold : FontWeights.Normal;
            }
        }
    }

    private void CountryLoadAndNavigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CountryPreset country })
            _ = _viewModel.LoadCountryAndNavigateCommand.ExecuteAsync(country);
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            _viewModel.UpdateQuickSearchSuggestions(sender.Text);
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        // Selecting a suggestion is a playback action; WinUI may also raise QuerySubmitted
        // for that selection, so do not turn it into a second navigation/search operation.
        if (args.ChosenSuggestion is RadioStation) return;
        _viewModel.QuickSearchText = args.QueryText;
        _ = _viewModel.RunQuickSearchCommand.ExecuteAsync(null);
    }

    private void SearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is not RadioStation station) return;
        _viewModel.QuickSearchText = station.Name;
        _ = _viewModel.PlayCommand.ExecuteAsync(station);
    }

    private void SearchTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter:
                _ = _viewModel.SearchCommand.ExecuteAsync(null);
                e.Handled = true;
                break;
            case VirtualKey.Escape:
                _viewModel.ClearSearchCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void PlayPauseAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        _viewModel.TogglePlaybackCommand.Execute(null);
        args.Handled = true;
    }

    private async void About_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "About Glass Radio",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Global radio, calmly arranged.", FontSize = 18 },
                    new TextBlock { Text = "A personal free and open-source internet-radio application.", TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = "Station metadata is provided by radio-browser.info. Station artwork is supplied by broadcasters and external services.", TextWrapping = TextWrapping.Wrap }
                }
            },
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Root.XamlRoot
        };
        await dialog.ShowAsync();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.ShowFavoritesNav):
            case nameof(MainViewModel.ShowCountriesPanel):
            case nameof(MainViewModel.ShowTopStations):
                UpdateResponsiveLayout(Root.ActualWidth);
                break;
        }
    }

    private void ViewModel_LayoutSettingsLoaded(object? sender, EventArgs e)
    {
        // Re-derive pane visibility now that settings (including the Home-layout toggles) have
        // actually loaded - the very first layout pass can run before this event fires and would
        // otherwise be working from field-initializer defaults.
        UpdateResponsiveLayout(Root.ActualWidth);
        if (_paneWidthsUserAdjusted) return;
        // Only reapply the saved width to a pane that is actually visible right now - one
        // the breakpoint or a user toggle has collapsed to 0 should stay at 0.
        if (LeftColumn.Width.Value > 0) LeftColumn.Width = new GridLength(_viewModel.LeftPaneWidth);
        if (RightColumn.Width.Value > 0) RightColumn.Width = new GridLength(_viewModel.RightPaneWidth);
        UpdateContentMetrics(Root.ActualWidth, Root.ActualWidth < 900, Root.ActualWidth < 1250);
    }

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel.LayoutSettingsLoaded -= ViewModel_LayoutSettingsLoaded;
        await _viewModel.DisposeAsync();
    }
}
