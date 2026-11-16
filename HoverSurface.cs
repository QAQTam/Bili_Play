using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace BiliPlay;

// The same rectangle hosts the content and its interaction overlay. Native item
// backgrounds are disabled so their internal insets cannot create a second frame.
public static class HoverSurface
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(HoverSurface), new PropertyMetadata(false, Changed));
    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);
    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    { if (args.NewValue is true && sender is Grid surface) _ = new SurfaceState(surface); }
    private sealed class SurfaceState
    {
        private readonly Grid _surface;
        private readonly Border _overlay = new() { CornerRadius = new(8), IsHitTestVisible = false, Opacity = 0 };
        private SelectorItem? _owner;
        private ListViewBase? _list;
        private long _selectedToken, _modeToken;
        private bool _over, _pressed;
        private CheckBox? _check;
        private static readonly System.Reflection.PropertyInfo[] Backgrounds = typeof(ListViewItemPresenter).GetProperties().Where(property => property.CanWrite && typeof(Brush).IsAssignableFrom(property.PropertyType)
            && property.Name is "PointerOverBackground" or "PressedBackground" or "SelectedBackground" or "SelectedPointerOverBackground" or "SelectedPressedBackground").ToArray();
        private static readonly System.Reflection.PropertyInfo[] Checks = typeof(ListViewItemPresenter).GetProperties().Where(property => property.CanWrite && typeof(Brush).IsAssignableFrom(property.PropertyType) && property.Name.StartsWith("Check", StringComparison.Ordinal)).ToArray();
        public SurfaceState(Grid surface)
        {
            _surface = surface;
            _surface.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            _surface.Loaded += Load; _surface.Unloaded += (_, _) => Detach();
            _surface.PointerEntered += (_, _) => { _over = true; Update(); };
            _surface.PointerExited += (_, _) => { _over = _pressed = false; Update(); };
            _surface.PointerPressed += (_, _) => { _pressed = true; Update(); };
            _surface.PointerReleased += (_, _) => { _pressed = false; Update(); };
            _surface.ActualThemeChanged += (_, _) => Update();
        }
        private void Load(object sender, RoutedEventArgs args)
        {
            Detach();
            if (!_surface.Children.Contains(_overlay)) _surface.Children.Add(_overlay);
            DependencyObject? parent = _surface;
            while ((parent = VisualTreeHelper.GetParent(parent)) is not null)
            {
                if (parent is SelectorItem item && _owner is null) _owner = item;
                if (parent is ListViewBase list) { _list = list; break; }
            }
            _check = FindCheck(_surface);
            if (_owner is { } owner)
            {
                _selectedToken = owner.RegisterPropertyChangedCallback(SelectorItem.IsSelectedProperty, (_, _) => Update());
                if (FindPresenter(owner) is { } presenter)
                {
                    var clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                    foreach (var property in Backgrounds) property.SetValue(presenter, clear);
                    if (_check is not null) foreach (var property in Checks) property.SetValue(presenter, clear);
                }
            }
            if (_list is { } source) _modeToken = source.RegisterPropertyChangedCallback(ListViewBase.SelectionModeProperty, (_, _) => Update());
            Update();
        }
        private void Detach()
        {
            if (_owner is not null) _owner.UnregisterPropertyChangedCallback(SelectorItem.IsSelectedProperty, _selectedToken);
            if (_list is not null) _list.UnregisterPropertyChangedCallback(ListViewBase.SelectionModeProperty, _modeToken);
            _owner = null; _list = null; _over = _pressed = false;
        }
        private void Update()
        {
            var light = _surface.ActualTheme == ElementTheme.Light;
            _overlay.Background = new SolidColorBrush(light ? Windows.UI.Color.FromArgb(_pressed ? (byte)28 : (byte)18, 0, 0, 0) : Windows.UI.Color.FromArgb(_pressed ? (byte)28 : (byte)18, 255, 255, 255));
            _overlay.Opacity = _over || _pressed || _owner?.IsSelected == true ? 1 : 0;
            if (_check is not null) { _check.Visibility = _list?.SelectionMode == ListViewSelectionMode.Multiple ? Visibility.Visible : Visibility.Collapsed; _check.IsChecked = _owner?.IsSelected == true; }
        }
        private static CheckBox? FindCheck(DependencyObject root)
        {
            if (root is CheckBox check && check.Name == "TrackSelectionCheck") return check;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) if (FindCheck(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
            return null;
        }
        private static ListViewItemPresenter? FindPresenter(DependencyObject root)
        {
            if (root is ListViewItemPresenter presenter) return presenter;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) if (FindPresenter(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
            return null;
        }
    }

}
