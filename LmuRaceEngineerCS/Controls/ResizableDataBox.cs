using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;

namespace LmuRaceEngineerCS.Controls;

/// <summary>
/// Wraps a data widget so it can be uniformly scaled — box and text together — while the
/// app is in Edit-Layout mode (<see cref="App.EditLayout"/>). Scaling is a
/// <see cref="ScaleTransform"/> applied as a LayoutTransform, so at the default scale of
/// 1.0 the transform is the identity and the page renders EXACTLY as designed; only when
/// the user drags the corner grip does the box (and its text) grow or shrink. The chosen
/// scale is keyed by <see cref="WidgetId"/> and persisted to layout.json, so it survives
/// restarts. Outside Edit mode the grip/highlight are hidden but the saved scale still
/// applies.
/// </summary>
public class ResizableDataBox : ContentControl
{
    public static readonly DependencyProperty WidgetIdProperty =
        DependencyProperty.Register(nameof(WidgetId), typeof(string),
            typeof(ResizableDataBox), new PropertyMetadata(""));

    /// <summary>Stable id used as the persistence key. Must be unique per data box.</summary>
    public string WidgetId
    {
        get => (string)GetValue(WidgetIdProperty);
        set => SetValue(WidgetIdProperty, value);
    }

    private const double MinScale = 0.5, MaxScale = 3.0;

    // Cached layout — loaded once, refreshed on each save. Reads happen for every box on
    // page load, so we avoid re-reading the file per box.
    private static LayoutState _state = LayoutState.Load();

    private readonly ScaleTransform _scaleTransform = new(1.0, 1.0);
    private double  _scale = 1.0;
    private Thumb?  _grip;
    private Border? _editBorder;

    public ResizableDataBox()
    {
        Template = BuildTemplate();
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment   = VerticalAlignment.Stretch;
        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // ── Template (scaled content + edit highlight + corner grip) ──────────────
    private static ControlTemplate BuildTemplate()
    {
        const string xaml =
            "<ControlTemplate " +
              "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
              "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' " +
              "TargetType='ContentControl'>" +
              "<Grid>" +
                "<ContentPresenter x:Name='PART_Content'/>" +
                "<Border x:Name='PART_EditBorder' BorderBrush='#5AA0FF' BorderThickness='1' " +
                        "Background='#142A6FB0' Visibility='Collapsed' IsHitTestVisible='False'/>" +
                "<Thumb x:Name='PART_Grip' Width='16' Height='16' " +
                       "HorizontalAlignment='Right' VerticalAlignment='Bottom' " +
                       "Cursor='SizeNWSE' Visibility='Collapsed'>" +
                  "<Thumb.Template>" +
                    "<ControlTemplate TargetType='Thumb'>" +
                      "<Border Background='#5AA0FF' CornerRadius='2' Margin='1'>" +
                        "<Path Data='M 9 1 L 9 9 L 1 9' Stroke='White' StrokeThickness='1.3' " +
                              "HorizontalAlignment='Center' VerticalAlignment='Center'/>" +
                      "</Border>" +
                    "</ControlTemplate>" +
                  "</Thumb.Template>" +
                "</Thumb>" +
              "</Grid>" +
            "</ControlTemplate>";
        return (ControlTemplate)XamlReader.Parse(xaml);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (_grip != null)
        {
            _grip.DragDelta     -= Grip_DragDelta;
            _grip.DragCompleted -= Grip_DragCompleted;
        }
        _grip       = GetTemplateChild("PART_Grip") as Thumb;
        _editBorder = GetTemplateChild("PART_EditBorder") as Border;
        if (GetTemplateChild("PART_Content") is ContentPresenter cp)
            cp.LayoutTransform = _scaleTransform;
        if (_grip != null)
        {
            _grip.DragDelta     += Grip_DragDelta;
            _grip.DragCompleted += Grip_DragCompleted;
        }
        ApplyEditVisual(App.EditLayout);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RestoreScale();
        App.EditLayoutChanged += ApplyEditVisual;
        ApplyEditVisual(App.EditLayout);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
        => App.EditLayoutChanged -= ApplyEditVisual;

    // ── Persistence ───────────────────────────────────────────────────────────
    private void RestoreScale()
    {
        if (!string.IsNullOrEmpty(WidgetId) &&
            _state.WidgetScales.TryGetValue(WidgetId, out double s))
        {
            SetScale(s);
        }
    }

    private void PersistScale()
    {
        if (string.IsNullOrEmpty(WidgetId)) return;
        // Read-modify-write so we don't clobber scales saved by other boxes/pages.
        var st = LayoutState.Load();
        st.WidgetScales[WidgetId] = Math.Round(_scale, 3);
        st.Save();
        _state = st;
    }

    // ── Grip drag ─────────────────────────────────────────────────────────────
    private void Grip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        // Drag out (right/down) to grow. Scale change is proportional to the box size so
        // it feels the same on large and small widgets.
        double basis  = Math.Max(80.0, ActualWidth);
        double factor = 1.0 + (e.HorizontalChange + e.VerticalChange) / (2.0 * basis);
        SetScale(_scale * factor);
    }

    private void Grip_DragCompleted(object sender, DragCompletedEventArgs e) => PersistScale();

    private void SetScale(double s)
    {
        _scale = Clamp(s, MinScale, MaxScale);
        _scaleTransform.ScaleX = _scale;
        _scaleTransform.ScaleY = _scale;
    }

    // ── Edit-mode visuals ─────────────────────────────────────────────────────
    private void ApplyEditVisual(bool on)
    {
        var v = on ? Visibility.Visible : Visibility.Collapsed;
        if (_grip       != null) _grip.Visibility       = v;
        if (_editBorder != null) _editBorder.Visibility = v;
    }

    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
}
