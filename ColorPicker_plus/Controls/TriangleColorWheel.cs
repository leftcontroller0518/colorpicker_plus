using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Test94.Settings;
namespace Test94.Controls;
public class TriangleColorWheel : Grid
{
    private const int CanvasSize = 150;
    private const double CX = 75.0;
    private const double CY = 75.0;
    private const double TriR = 53.0;
    private const double RingInner = 55.0;
    private const double RingOuter = 72.0;
    private const double IndicatorInnerR = 58.0;
    private const double IndicatorOuterR = 74.0;
    private const double RightDragThreshold = 4.0;
    public static readonly DependencyProperty HProperty =
        DependencyProperty.Register(nameof(H), typeof(byte), typeof(TriangleColorWheel),
            new FrameworkPropertyMetadata((byte)0,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnHChanged));
    public static readonly DependencyProperty SProperty =
        DependencyProperty.Register(nameof(S), typeof(byte), typeof(TriangleColorWheel),
            new FrameworkPropertyMetadata((byte)255,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSvChanged));
    public static readonly DependencyProperty VProperty =
        DependencyProperty.Register(nameof(V), typeof(byte), typeof(TriangleColorWheel),
            new FrameworkPropertyMetadata((byte)255,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSvChanged));
    public byte H
    {
        get => (byte)GetValue(HProperty);
        set => SetValue(HProperty, value);
    }
    public byte S
    {
        get => (byte)GetValue(SProperty);
        set => SetValue(SProperty, value);
    }
    public byte V
    {
        get => (byte)GetValue(VProperty);
        set => SetValue(VProperty, value);
    }
    public Action<byte, byte, byte>? OnColorChanged { get; set; }
    public bool IsDragging => _isDragging;
    public event Action<bool>? OnTrackingRotationChanged;
    public event Action<bool>? OnFixedAngleEnabledChanged;
    public event Action<double>? OnFixedAngleChanged;
    public bool FixedAngleEnabled
    {
        get => _fixedAngleEnabled;
        set
        {
            if (_fixedAngleEnabled == value) return;
            _fixedAngleEnabled = value;
            if (_fixedAngleEnabled) _trackingRotationEnabled = false;
            OnFixedAngleEnabledChanged?.Invoke(value);
            if (_initialized)
            {
                UpdateTriangleGeometry();
                RenderTriangleGradient();
                UpdateHueIndicator();
                UpdateSvIndicator();
            }
        }
    }
    public double FixedAngle
    {
        get => _fixedAngle;
        set
        {
            _fixedAngle = value;
            OnFixedAngleChanged?.Invoke(value);
            if ((_fixedAngleEnabled || !_trackingRotationEnabled) && _initialized)
            {
                UpdateTriangleGeometry();
                RenderTriangleGradient();
                UpdateHueIndicator();
                UpdateSvIndicator();
            }
        }
    }
    public bool TrackingRotationEnabled
    {
        get => _trackingRotationEnabled;
        set
        {
            if (_trackingRotationEnabled == value) return;
            _trackingRotationEnabled = value;
            if (_trackingRotationEnabled) _fixedAngleEnabled = false;
            OnTrackingRotationChanged?.Invoke(value);
            if (_initialized)
            {
                UpdateTriangleGeometry();
                RenderTriangleGradient();
                UpdateHueIndicator();
                UpdateSvIndicator();
            }
        }
    }
    private readonly Canvas _canvas;
    private readonly Rectangle _hueRingRect;
    private readonly Polygon _svTriangle;
    private readonly Ellipse _hueIndicatorShadow;
    private readonly Ellipse _hueIndicator;
    private readonly Ellipse _svIndicatorShadow;
    private readonly Ellipse _svIndicator;
    private Point _a, _b, _c;
    private Point _baseA, _baseB, _baseC;
    private double _hue;
    private double _sat = 1.0;
    private double _val = 1.0;
    private bool _isDragging;
    private bool _isRightDragging;
    private bool _dragRing;
    private bool _dragTriangle;
    private Point _rightDownPos;
    private bool _rightDragMoved;
    private bool _updating;
    private bool _initialized;
    private bool _fixedAngleEnabled;
    private double _fixedAngle = 0.0;
    private bool _trackingRotationEnabled = true;
    private double _hueOffset;
    private static double _savedHueOffset;
    private static WriteableBitmap? _cachedHueRing;
    public TriangleColorWheel()
    {
        Width = CanvasSize;
        Height = CanvasSize;
        ClipToBounds = false;
        _canvas = new Canvas
        {
            Width = CanvasSize,
            Height = CanvasSize,
            Background = Brushes.Transparent
        };
        Children.Add(_canvas);
        _hueRingRect = new Rectangle
        {
            Width = CanvasSize,
            Height = CanvasSize,
            IsHitTestVisible = false
        };
        _canvas.Children.Add(_hueRingRect);
        _svTriangle = new Polygon
        {
            Stroke = Brushes.Black,
            StrokeThickness = 1,
            IsHitTestVisible = false
        };
        _canvas.Children.Add(_svTriangle);
        _hueIndicatorShadow = new Ellipse
        {
            Stroke = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
            StrokeThickness = 3,
            Fill = Brushes.Transparent,
            Width = 16,
            Height = 16,
            IsHitTestVisible = false
        };
        _canvas.Children.Add(_hueIndicatorShadow);
        _hueIndicator = new Ellipse
        {
            Stroke = Brushes.White,
            StrokeThickness = 2,
            Width = 14,
            Height = 14,
            IsHitTestVisible = false
        };
        _canvas.Children.Add(_hueIndicator);
        _svIndicatorShadow = new Ellipse
        {
            Width = 16,
            Height = 16,
            Stroke = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)),
            StrokeThickness = 3,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        _canvas.Children.Add(_svIndicatorShadow);
        _svIndicator = new Ellipse
        {
            Width = 14,
            Height = 14,
            Stroke = Brushes.White,
            StrokeThickness = 2,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        _canvas.Children.Add(_svIndicator);
        _canvas.MouseLeftButtonDown += OnMouseDown;
        _canvas.MouseMove += OnMouseMove;
        _canvas.MouseLeftButtonUp += OnMouseUp;
        _canvas.MouseRightButtonDown += OnRightMouseDown;
        _canvas.MouseRightButtonUp += OnRightMouseUp;
        var ctxMenu = new ContextMenu();
        AddOffsetPreset(ctxMenu, "赤=右（ibisPaint / MediBang）", 0);
        AddOffsetPreset(ctxMenu, "赤=上（PaintShop Pro）", -90);
        AddOffsetPreset(ctxMenu, "赤=左（Krita）", 180);
        AddOffsetPreset(ctxMenu, "CLIP STUDIO PAINT（5π/6）", -150);
        ctxMenu.Items.Add(new Separator());
        var trackingItem = new MenuItem { Header = "追尾回転" };
        trackingItem.IsCheckable = true;
        trackingItem.Click += (s, e) => TrackingRotationEnabled = trackingItem.IsChecked;
        ctxMenu.Opened += (s, e) => trackingItem.IsChecked = TrackingRotationEnabled;
        ctxMenu.Items.Add(trackingItem);
        var fixedAngleItem = new MenuItem { Header = "角度固定" };
        fixedAngleItem.IsCheckable = true;
        fixedAngleItem.Click += (s, e) => FixedAngleEnabled = fixedAngleItem.IsChecked;
        ctxMenu.Opened += (s, e) => fixedAngleItem.IsChecked = FixedAngleEnabled;
        ctxMenu.Items.Add(fixedAngleItem);
        this.ContextMenu = ctxMenu;
        this.ContextMenuOpening += OnContextMenuOpening;
    }
    private void AddOffsetPreset(ContextMenu menu, string header, double offset)
    {
        var item = new MenuItem { Header = header };
        item.Click += (s, e) => SetHueOffset(offset);
        menu.Items.Add(item);
    }
    public void SetHueOffset(double offset)
    {
        _hueOffset = offset;
        _savedHueOffset = offset;
        _hueRingRect.RenderTransform = new RotateTransform(offset, CX, CY);
        if (_initialized)
        {
            UpdateTriangleGeometry();
            RenderTriangleGradient();
            UpdateHueIndicator();
            UpdateSvIndicator();
        }
    }
    public void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;
        InitBaseTriangle();
        _hue = H / 255.0 * 360.0;
        _sat = S / 255.0;
        _val = V / 255.0;
        _hueOffset = _savedHueOffset;
        _hueRingRect.RenderTransform = new RotateTransform(_hueOffset, CX, CY);
        RenderHueRing();
        UpdateTriangleGeometry();
        RenderTriangleGradient();
        UpdateHueIndicator();
        UpdateSvIndicator();
    }
    public void ApplySettings(ColorPickerPlusSettings settings)
    {
        TrackingRotationEnabled = settings.TriangleTrackingRotationEnabled;
        FixedAngleEnabled = settings.TriangleFixedAngleEnabled;
        FixedAngle = settings.TriangleFixedAngle;
    }
    private static void OnHChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TriangleColorWheel w) return;
        w._hue = (byte)e.NewValue / 255.0 * 360.0;
        if (!w._updating && !w._isDragging && w._initialized)
        {
            w._updating = true;
            try
            {
                w.UpdateTriangleGeometry();
                w.RenderTriangleGradient();
                w.UpdateHueIndicator();
                w.UpdateSvIndicator();
            }
            finally { w._updating = false; }
        }
    }
    private static void OnSvChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TriangleColorWheel w) return;
        w._sat = w.S / 255.0;
        w._val = w.V / 255.0;
        if (!w._updating && !w._isDragging && w._initialized)
        {
            w._updating = true;
            try { w.UpdateSvIndicator(); }
            finally { w._updating = false; }
        }
    }
    private void InitBaseTriangle()
    {
        _baseB = new Point(CX + TriR, CY);
        _baseA = new Point(CX - TriR / 2.0, CY - TriR * Math.Sqrt(3) / 2.0);
        _baseC = new Point(CX - TriR / 2.0, CY + TriR * Math.Sqrt(3) / 2.0);
    }
    private void UpdateTriangleGeometry()
    {
        double angle = (!_trackingRotationEnabled || _fixedAngleEnabled) ? _fixedAngle : _hue;
        double rad = (angle + _hueOffset) * Math.PI / 180.0;
        var center = new Point(CX, CY);
        _a = RotatePoint(_baseA, center, rad);
        _b = RotatePoint(_baseB, center, rad);
        _c = RotatePoint(_baseC, center, rad);
        _svTriangle.Points = new PointCollection { _a, _b, _c };
    }
    private static Point RotatePoint(Point pt, Point center, double angle)
    {
        double cos = Math.Cos(angle), sin = Math.Sin(angle);
        var v = pt - center;
        return new Point(center.X + v.X * cos - v.Y * sin,
                         center.Y + v.X * sin + v.Y * cos);
    }
    private void RenderHueRing()
    {
        _cachedHueRing ??= GenerateHueRingBitmap();
        _hueRingRect.Fill = new ImageBrush(_cachedHueRing);
    }
    private void ShowFixedAngleDialog()
    {
        var win = new Window
        {
            Title = "固定角度",
            Width = 280,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow
        };
        var sp = new StackPanel { Margin = new Thickness(12) };
        sp.Children.Add(new TextBlock
        {
            Text = $"三角形の固定角度（度）を入力:\n現在: {_fixedAngle:F1}°",
            Margin = new Thickness(0, 0, 0, 4)
        });
        var tb = new TextBox { Text = _fixedAngle.ToString("F1") };
        sp.Children.Add(tb);
        var btn = new Button
        {
            Content = "適用",
            Width = 80,
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        btn.Click += (s, e) =>
        {
            if (double.TryParse(tb.Text, out double v))
            {
                _fixedAngle = v;
                if (_initialized)
                {
                    UpdateTriangleGeometry();
                    RenderTriangleGradient();
                    UpdateHueIndicator();
                    UpdateSvIndicator();
                }
                win.Close();
            }
        };
        sp.Children.Add(btn);
        win.Content = sp;
        win.ShowDialog();
    }
    private static WriteableBitmap GenerateHueRingBitmap()
    {
        var bmp = new WriteableBitmap(CanvasSize, CanvasSize, 96, 96,
            PixelFormats.Bgra32, null);
        int stride = CanvasSize * 4;
        var px = new byte[CanvasSize * stride];
        for (var y = 0; y < CanvasSize; y++)
            for (var x = 0; x < CanvasSize; x++)
            {
                double dx = x - CX, dy = y - CY;
                var dist = Math.Sqrt(dx * dx + dy * dy);
                var i = (y * CanvasSize + x) * 4;
                if (dist >= RingInner - 0.5 && dist <= RingOuter + 0.5)
                {
                    var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
                    if (angle < 0) angle += 360.0;
                    var c = HsvToColor(angle, 1.0, 1.0);
                    var alphaInner = Math.Clamp((dist - (RingInner - 0.5)) * 2.0, 0, 1);
                    var alphaOuter = Math.Clamp((RingOuter + 0.5 - dist) * 2.0, 0, 1);
                    var a = (byte)(alphaInner * alphaOuter * 255);
                    px[i + 0] = c.B;
                    px[i + 1] = c.G;
                    px[i + 2] = c.R;
                    px[i + 3] = a;
                }
            }
        bmp.WritePixels(new Int32Rect(0, 0, CanvasSize, CanvasSize), px, stride, 0);
        return bmp;
    }
    private void RenderTriangleGradient()
    {
        var minX = Math.Min(_a.X, Math.Min(_b.X, _c.X));
        var minY = Math.Min(_a.Y, Math.Min(_b.Y, _c.Y));
        var maxX = Math.Max(_a.X, Math.Max(_b.X, _c.X));
        var maxY = Math.Max(_a.Y, Math.Max(_b.Y, _c.Y));
        var w = (int)Math.Ceiling(maxX - minX);
        var h = (int)Math.Ceiling(maxY - minY);
        if (w <= 0 || h <= 0) return;
        var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        var stride = w * 4;
        var px = new byte[h * stride];
        var pureHue = HsvToColor(_hue, 1.0, 1.0);
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var p = new Point(minX + x, minY + y);
                Barycentric(p, _a, _b, _c, out var wA, out var wB, out var wC);
                var i = (y * w + x) * 4;
                if (wA >= 0 && wB >= 0 && wC >= 0)
                {
                    var r = (byte)Math.Clamp(wA * 255 + wB * pureHue.R, 0, 255);
                    var g = (byte)Math.Clamp(wA * 255 + wB * pureHue.G, 0, 255);
                    var b = (byte)Math.Clamp(wA * 255 + wB * pureHue.B, 0, 255);
                    px[i + 0] = b;
                    px[i + 1] = g;
                    px[i + 2] = r;
                    px[i + 3] = 255;
                }
            }
        bmp.WritePixels(new Int32Rect(0, 0, w, h), px, stride, 0);
        _svTriangle.Fill = new ImageBrush(bmp)
        {
            Viewbox = new Rect(0, 0, w, h),
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(minX, minY, w, h),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill
        };
    }
    private void UpdateHueIndicator()
    {
        var rad = (_hue + _hueOffset) * Math.PI / 180.0;
        var dir = new Vector(Math.Cos(rad), Math.Sin(rad));
        var center = new Point(CX, CY);
        var pos = center + dir * ((RingInner + RingOuter) / 2.0);
        Canvas.SetLeft(_hueIndicatorShadow, pos.X - 8);
        Canvas.SetTop(_hueIndicatorShadow, pos.Y - 8);
        Canvas.SetLeft(_hueIndicator, pos.X - 7);
        Canvas.SetTop(_hueIndicator, pos.Y - 7);
        _hueIndicator.Fill = new SolidColorBrush(HsvToColor(_hue, 1.0, 1.0));
    }
    private void UpdateSvIndicator()
    {
        var wA = _val * (1.0 - _sat);
        var wB = _val * _sat;
        var wC = 1.0 - _val;
        var pos = new Point(
            wA * _a.X + wB * _b.X + wC * _c.X,
            wA * _a.Y + wB * _b.Y + wC * _c.Y);
        Canvas.SetLeft(_svIndicatorShadow, pos.X - 8);
        Canvas.SetTop(_svIndicatorShadow, pos.Y - 8);
        Canvas.SetLeft(_svIndicator, pos.X - 7);
        Canvas.SetTop(_svIndicator, pos.Y - 7);
        _svIndicator.Fill = new SolidColorBrush(HsvToColor(_hue, _sat, _val));
        _svIndicatorShadow.Visibility = Visibility.Visible;
        _svIndicator.Visibility = Visibility.Visible;
    }
    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_initialized) return;
        var pos = e.GetPosition(_canvas);
        var dist = (pos - new Point(CX, CY)).Length;
        if (dist >= RingInner && dist <= RingOuter + 4)
        {
            _isDragging = true;
            _dragRing = true;
            _dragTriangle = false;
            _canvas.CaptureMouse();
            UpdateHueFromPoint(pos);
        }
        else if (PointInTriangle(pos, _a, _b, _c))
        {
            _isDragging = true;
            _dragTriangle = true;
            _dragRing = false;
            _canvas.CaptureMouse();
            UpdateSvFromPoint(pos);
        }
        e.Handled = true;
    }
    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(_canvas);
        if (_isDragging)
        {
            if (_dragRing) UpdateHueFromPoint(pos);
            else if (_dragTriangle) UpdateSvFromPoint(pos);
        }
        else if (_isRightDragging)
        {
            if (!_rightDragMoved && (pos - _rightDownPos).Length > RightDragThreshold)
                _rightDragMoved = true;
            if (_rightDragMoved)
                UpdateFixedAngleFromPoint(pos);
        }
        e.Handled = true;
    }
    private void OnRightMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_initialized) return;
        _isRightDragging = true;
        _rightDragMoved = false;
        _rightDownPos = e.GetPosition(_canvas);
        _canvas.CaptureMouse();
        e.Handled = true;
    }
    private void OnRightMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isRightDragging = false;
        _canvas.ReleaseMouseCapture();
        if (_rightDragMoved)
        {
            e.Handled = true;
        }
    }
    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (_rightDragMoved)
        {
            e.Handled = true;
        }
    }
    private void UpdateFixedAngleFromPoint(Point pos)
    {
        var angle = Math.Atan2(pos.Y - CY, pos.X - CX) * 180.0 / Math.PI;
        angle -= _hueOffset;
        FixedAngle = (angle + 720.0) % 360.0;
        FixedAngleEnabled = true;
    }
    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        _dragRing = false;
        _dragTriangle = false;
        _canvas.ReleaseMouseCapture();
        e.Handled = true;
    }
    private void UpdateHueFromPoint(Point pos)
    {
        var screenAngle = Math.Atan2(pos.Y - CY, pos.X - CX) * 180.0 / Math.PI;
        if (screenAngle < 0) screenAngle += 360.0;
        _hue = (screenAngle - _hueOffset + 720.0) % 360.0;
        var newH = (byte)Math.Clamp(_hue / 360.0 * 255.0, 0, 255);
        if (H == newH)
        {
            UpdateHueIndicator();
            return;
        }
        _updating = true;
        try { H = newH; }
        finally { _updating = false; }
        UpdateTriangleGeometry();
        RenderTriangleGradient();
        UpdateHueIndicator();
        UpdateSvIndicator();
        OnColorChanged?.Invoke(H, S, V);
    }
    private void UpdateSvFromPoint(Point pos)
    {
        pos = ClampPointToTriangle(pos, _a, _b, _c);
        Barycentric(pos, _a, _b, _c, out var wA, out var wB, out _);
        var sum = wA + wB;
        _val = Math.Clamp(sum, 0, 1);
        _sat = _val > 0 ? Math.Clamp(wB / sum, 0, 1) : 0;
        var newS = (byte)Math.Clamp(_sat * 255.0, 0, 255);
        var newV = (byte)Math.Clamp(_val * 255.0, 0, 255);
        _updating = true;
        try { S = newS; V = newV; }
        finally { _updating = false; }
        UpdateSvIndicator();
        OnColorChanged?.Invoke(H, S, V);
    }
    private static Point ClampPointToTriangle(Point p, Point a, Point b, Point c)
    {
        if (PointInTriangle(p, a, b, c))
            return p;
        var cpAb = ClosestPointOnLineSegment(a, b, p);
        var cpBc = ClosestPointOnLineSegment(b, c, p);
        var cpCa = ClosestPointOnLineSegment(c, a, p);
        var dAb = (p - cpAb).Length;
        var dBc = (p - cpBc).Length;
        var dCa = (p - cpCa).Length;
        var closest = cpAb;
        var dMin = dAb;
        if (dBc < dMin)
        {
            dMin = dBc;
            closest = cpBc;
        }
        if (dCa < dMin)
            closest = cpCa;
        return closest;
    }
    private static Point ClosestPointOnLineSegment(Point a, Point b, Point p)
    {
        var ab = b - a;
        var t = Vector.Multiply(p - a, ab) / ab.LengthSquared;
        t = Math.Clamp(t, 0, 1);
        return a + ab * t;
    }
    private static bool PointInTriangle(Point p, Point a, Point b, Point c)
    {
        Barycentric(p, a, b, c, out var wA, out var wB, out var wC);
        return wA >= 0 && wB >= 0 && wC >= 0;
    }
    private static void Barycentric(Point p, Point a, Point b, Point c,
        out double wA, out double wB, out double wC)
    {
        var det = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
        if (Math.Abs(det) < 1e-9)
        {
            wA = wB = wC = 0;
            return;
        }
        wA = ((b.Y - c.Y) * (p.X - c.X) + (c.X - b.X) * (p.Y - c.Y)) / det;
        wB = ((c.Y - a.Y) * (p.X - c.X) + (a.X - c.X) * (p.Y - c.Y)) / det;
        wC = 1.0 - wA - wB;
    }
    internal static Color HsvToColor(double hue, double saturation, double value)
    {
        hue %= 360.0;
        if (hue < 0) hue += 360.0;
        var hi = (int)Math.Floor(hue / 60.0) % 6;
        var f = hue / 60.0 - Math.Floor(hue / 60.0);
        var v = (byte)Math.Clamp(value * 255.0, 0, 255);
        var p = (byte)Math.Clamp(v * (1.0 - saturation), 0, 255);
        var q = (byte)Math.Clamp(v * (1.0 - f * saturation), 0, 255);
        var t = (byte)Math.Clamp(v * (1.0 - (1.0 - f) * saturation), 0, 255);
        return hi switch
        {
            0 => Color.FromArgb(255, v, t, p),
            1 => Color.FromArgb(255, q, v, p),
            2 => Color.FromArgb(255, p, v, t),
            3 => Color.FromArgb(255, p, q, v),
            4 => Color.FromArgb(255, t, p, v),
            5 => Color.FromArgb(255, v, p, q),
            _ => Colors.White
        };
    }
}
