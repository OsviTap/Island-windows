using Island.App.ViewModels;
using Island.Core.Configuration;
using Island.Core.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace Island.App;

/// <summary>Editor de apariencia con vista previa reversible sobre la isla.</summary>
public sealed class SettingsWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly AppSettingsDocument _original;
    private readonly ComboBox _material = new() { Header = "Estilo de fondo", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _accent = new() { Header = "Color de acento", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Slider _opacity = new() { Header = "Intensidad de la capa de color", Minimum = 0, Maximum = 100, StepFrequency = 5, IsSnapToTickEnabled = true };
    private readonly ToggleSwitch _artwork = new() { Header = "Mostrar portada de la canción" };
    private readonly ToggleSwitch _hover = new() { Header = "Mostrar vista previa al acercar el cursor" };
    private readonly ToggleSwitch _animations = new() { Header = "Animaciones de expansión" };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap };
    private bool _ready;
    private bool _saved;
    private static readonly string[] AccentValues = ["#38BDF8", "#A78BFA", "#F472B6", "#34D399", "#FBBF24"];

    public SettingsWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        _original = viewModel.CurrentSettings;
        Title = "Configuración de la isla";
        AppWindow.Resize(new SizeInt32(520, 720));

        _material.Items.Add("Cristal · escritorio difuminado");
        _material.Items.Add("Mica · tono del escritorio");
        _material.Items.Add("Sólido · fondo opaco");
        _material.SelectedIndex = _original.RequestedMaterialOverride switch
        {
            MaterialKind.Glass => 0,
            MaterialKind.Mica => 1,
            MaterialKind.Solid => 2,
            _ => _original.SelectedThemeId == BuiltInThemes.DefaultGlassThemeId ? 0 :
                 _original.SelectedThemeId == BuiltInThemes.DefaultMicaThemeId ? 1 : 2
        };
        foreach (var name in new[] { "Azul", "Violeta", "Rosa", "Verde", "Ámbar" }) _accent.Items.Add(name);
        int accentIndex = Array.FindIndex(AccentValues, value => string.Equals(value, _original.AccentColorHex, StringComparison.OrdinalIgnoreCase));
        _accent.SelectedIndex = accentIndex < 0 ? 0 : accentIndex;
        _opacity.Value = _original.SurfaceOpacityPercent ?? 10;
        _artwork.IsOn = _original.ShowArtwork;
        _hover.IsOn = _original.HoverPreviewEnabled;
        _animations.IsOn = _original.AnimationsEnabled;

        var title = new TextBlock { Text = "Hazla tuya", FontSize = 26, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        var intro = new TextBlock { Text = "Los cambios se muestran en la isla mientras eliges. Guarda cuando estés conforme.", TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
        var explanation = new TextBlock { Text = "Cristal muestra el escritorio difuminado. Mica usa el tono del fondo. La intensidad modifica la capa de color, no el desenfoque de Windows.", TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
        var save = new Button { Content = "Guardar cambios", HorizontalAlignment = HorizontalAlignment.Stretch };
        var cancel = new Button { Content = "Cancelar", HorizontalAlignment = HorizontalAlignment.Stretch };
        var actions = new Grid { ColumnSpacing = 10 };
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(cancel, 1);
        actions.Children.Add(save);
        actions.Children.Add(cancel);
        var content = new StackPanel { Spacing = 14, Padding = new Thickness(24) };
        foreach (var element in new UIElement[] { title, intro, _material, explanation, _opacity, _accent, _artwork, _hover, _animations, _status, _error, actions }) content.Children.Add(element);
        Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        _material.SelectionChanged += (_, _) => Preview();
        _accent.SelectionChanged += (_, _) => Preview();
        _opacity.ValueChanged += (_, _) => Preview();
        _artwork.Toggled += (_, _) => Preview();
        _hover.Toggled += (_, _) => Preview();
        _animations.Toggled += (_, _) => Preview();
        save.Click += (_, _) =>
        {
            if (_viewModel.SaveAppearanceSettings(BuildSettings())) { _saved = true; Close(); }
            else _error.Text = "No se pudieron guardar los ajustes. Comprueba los permisos e inténtalo de nuevo.";
        };
        cancel.Click += (_, _) => Close();
        Closed += (_, _) =>
        {
            if (!_saved)
            {
                var current = _viewModel.CurrentSettings;
                var restored = current with
                {
                    SelectedThemeId = _original.SelectedThemeId,
                    RequestedMaterialOverride = _original.RequestedMaterialOverride,
                    SurfaceOpacityPercent = _original.SurfaceOpacityPercent,
                    AccentColorHex = _original.AccentColorHex,
                    ShowArtwork = _original.ShowArtwork,
                    HoverPreviewEnabled = _original.HoverPreviewEnabled,
                    AnimationsEnabled = _original.AnimationsEnabled
                };
                _viewModel.SaveAppearanceSettings(restored);
                _viewModel.ApplySettingsPreview(restored);
            }
        };
        _ready = true;
        _opacity.IsEnabled = _material.SelectedIndex != 2;
        _status.Text = _viewModel.AppearanceStatus;
    }

    private AppSettingsDocument BuildSettings()
    {
        var material = _material.SelectedIndex switch { 0 => MaterialKind.Glass, 1 => MaterialKind.Mica, _ => MaterialKind.Solid };
        var themeId = material switch { MaterialKind.Glass => BuiltInThemes.DefaultGlassThemeId, MaterialKind.Mica => BuiltInThemes.DefaultMicaThemeId, _ => BuiltInThemes.DefaultSolidThemeId };
        return _viewModel.CurrentSettings with
        {
            SelectedThemeId = themeId,
            RequestedMaterialOverride = material,
            SurfaceOpacityPercent = (int)_opacity.Value,
            AccentColorHex = AccentValues[Math.Clamp(_accent.SelectedIndex, 0, AccentValues.Length - 1)],
            ShowArtwork = _artwork.IsOn,
            HoverPreviewEnabled = _hover.IsOn,
            AnimationsEnabled = _animations.IsOn
        };
    }

    private void Preview()
    {
        if (!_ready) return;
        _opacity.IsEnabled = _material.SelectedIndex != 2;
        _viewModel.ApplySettingsPreview(BuildSettings());
        _status.Text = _viewModel.AppearanceStatus;
        _error.Text = string.Empty;
    }
}
