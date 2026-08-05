using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Test94.Controls;

/// <summary>
/// 色相リング＋SV三角形のカラーホイール。
///
/// YukkuriMovieMaker.Plugin.Community.Effect.Video.Node.Editor.Control.ColorPicker
/// （ノードエディタ用カラーピッカーの ColorPicker.xaml / ColorPicker.xaml.cs）で
/// 実装されていたホイールの描画・操作ロジックを移植したコントロール。
///
/// 移植にあたっては元コードのXAML(UserControl)構成ではなく、本プロジェクトの
/// <see cref="HsvColorWheel"/> と同じ方式（XAMLを使わずコードのみでVisualTreeを
/// 構築する）に合わせて書き直している。H/S/V の型・レンジも HsvColorWheel と
/// 揃え（byte, 0-255）、YMM4標準 ColorPicker への TwoWay バインディングに
/// そのまま利用できるようにした。
///
/// 移植元と比べての主な特徴（差別化ポイント）:
///   ・SV三角形の外にドラッグしても、最も近い辺の上にクランプされる
///     （ClampPointToTriangle / ClosestPointOnLineSegment）。
///     HsvColorWheel の四角形実装のような単純な軸クランプではなく、
///     三角形の辺に沿った自然な追従になる。
///   ・色相インジケータ（針）がリングの内側から外側へ、さらにリングの外まで
///     わずかに突き出す（オーバーシュート）デザイン。
///   ・三角形内かどうかの判定に重心座標（PointInTriangle）を用いる。
/// </summary>
public class TriangleColorWheel : Grid
{
    // ── レイアウト定数 ──
    // 移植元は 250x250 キャンバス・中心(125,125)・三角形半径 r=105・
    // リング内側半径105/外側半径125・インジケータのオーバーシュート量30、
    // という比率だった。本プロジェクトの HsvColorWheel（150x150）に
    // サイズを揃えるため、同じ比率で 0.6 倍に縮小してある。
    private const int CanvasSize = 150;
    private const double CX = 75.0;
    private const double CY = 75.0;
    private const double TriR = 58.0; // SV三角形の外接円半径（移植元のr=105相当）
    private const double RingInner = 58.0; // 色相リング内側半径
    private const double RingOuter = 70.0; // 色相リング外側半径
    private const double IndicatorInnerR = 58.0; // 色相インジケータ開始半径
    private const double IndicatorOuterR = 74.0; // 色相インジケータ終了半径（リング外側に少しはみ出す）

    // ── DependencyProperty（HsvColorWheel と同じ規約: byte 0-255）──
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

    // ── コールバック ──
    /// <summary>ドラッグ操作で色が変わったとき呼ばれる。</summary>
    public Action<byte, byte, byte>? OnColorChanged { get; set; }

    /// <summary>ドラッグ中かどうか。</summary>
    public bool IsDragging => _isDragging;

    // 状態変更通知用イベント
    public event Action<bool>? OnTrackingRotationChanged;
    public event Action<bool>? OnFixedAngleEnabledChanged;
    public event Action<double>? OnFixedAngleChanged;

    /// <summary>角度固定が有効かどうか。</summary>
    public bool FixedAngleEnabled
    {
        get => _fixedAngleEnabled;
        set
        {
            _fixedAngleEnabled = value;
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

    /// <summary>固定角度（度）。</summary>
    public double FixedAngle
    {
        get => _fixedAngle;
        set
        {
            _fixedAngle = value;
            OnFixedAngleChanged?.Invoke(value);
            if (_fixedAngleEnabled && _initialized)
            {
                UpdateTriangleGeometry();
                RenderTriangleGradient();
                UpdateHueIndicator();
                UpdateSvIndicator();
            }
        }
    }

    /// <summary>追尾回転が有効かどうか。</summary>
    public bool TrackingRotationEnabled
    {
        get => _trackingRotationEnabled;
        set
        {
            _trackingRotationEnabled = value;
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

    // ── ビジュアル要素 ──
    private readonly Canvas _canvas;
    private readonly Rectangle _hueRingRect;
    private readonly Polygon _svTriangle;
    private readonly Line _hueIndicatorShadow;
    private readonly Line _hueIndicator;
    private readonly Ellipse _svIndicatorShadow;
    private readonly Ellipse _svIndicator;

    // ── 状態 ──
    private Point _a, _b, _c; // 回転後の三角形頂点
    private Point _baseA, _baseB, _baseC; // 未回転の三角形頂点
    private double _hue; // 0–360
    private double _sat = 1.0; // 0–1
    private double _val = 1.0; // 0–1
    private bool _isDragging;
    private bool _dragRing;
    private bool _dragTriangle;
    private bool _updating; // 再帰防止フラグ
    private bool _initialized;
    private bool _fixedAngleEnabled; // 角度固定が有効かどうか
    private double _fixedAngle = 0.0; // 固定角度（度）
    private bool _trackingRotationEnabled = true; // 追尾回転が有効かどうか

    // ── Hueリングビットマップのキャッシュ ──
    private static WriteableBitmap? _cachedHueRing;

    // ── コンストラクタ ──
    public TriangleColorWheel()
    {
        Width = CanvasSize;
        Height = CanvasSize;
        // 移植元同様、色相インジケータの針がリング外側に少しはみ出すため
        // HsvColorWheel と異なりクリップしない。
        ClipToBounds = false;

        _canvas = new Canvas
        {
            Width = CanvasSize,
            Height = CanvasSize,
            Background = Brushes.Transparent // ヒットテスト用
        };
        Children.Add(_canvas);

        // Hue リング（WriteableBitmap を ImageBrush で貼る Rectangle）
        _hueRingRect = new Rectangle
        {
            Width = CanvasSize,
            Height = CanvasSize,
            IsHitTestVisible = false
        };
        _canvas.Children.Add(_hueRingRect);

        // SV 三角形
        _svTriangle = new Polygon
        {
            Stroke = Brushes.Black,
            StrokeThickness = 1,
            IsHitTestVisible = false
        };
        _canvas.Children.Add(_svTriangle);

        // Hue インジケータ（影＋本体）
        _hueIndicatorShadow = new Line
        {
            Stroke = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
            StrokeThickness = 4,
            IsHitTestVisible = false
        };
        _canvas.Children.Add(_hueIndicatorShadow);

        _hueIndicator = new Line
        {
            Stroke = Brushes.White,
            StrokeThickness = 2,
            IsHitTestVisible = false
        };
        _canvas.Children.Add(_hueIndicator);

        // SV インジケータ（影＋本体）
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

        // マウスイベント
        _canvas.MouseLeftButtonDown += OnMouseDown;
        _canvas.MouseMove += OnMouseMove;
        _canvas.MouseLeftButtonUp += OnMouseUp;

        // コンテキストメニュー
        var ctxMenu = new ContextMenu();

        // 追尾回転のオンオフ
        var trackingItem = new MenuItem { Header = "追尾回転" };
        trackingItem.IsCheckable = true;
        trackingItem.IsChecked = _trackingRotationEnabled;
        trackingItem.Click += (s, e) =>
        {
            _trackingRotationEnabled = trackingItem.IsChecked;
            if (_initialized)
            {
                UpdateTriangleGeometry();
                RenderTriangleGradient();
                UpdateHueIndicator();
                UpdateSvIndicator();
            }
        };
        ctxMenu.Items.Add(trackingItem);

        // 角度固定
        var fixedAngleItem = new MenuItem { Header = "角度固定" };
        fixedAngleItem.IsCheckable = true;
        fixedAngleItem.IsChecked = _fixedAngleEnabled;
        fixedAngleItem.Click += (s, e) =>
        {
            _fixedAngleEnabled = fixedAngleItem.IsChecked;
            if (_fixedAngleEnabled)
            {
                ShowFixedAngleDialog();
            }
            if (_initialized)
            {
                UpdateTriangleGeometry();
                RenderTriangleGradient();
                UpdateHueIndicator();
                UpdateSvIndicator();
            }
        };
        ctxMenu.Items.Add(fixedAngleItem);

        this.ContextMenu = ctxMenu;
    }

    // ── 初期化 ──
    /// <summary>
    /// 初回表示時に呼び出す。Collapsed状態からVisibleになったタイミングで呼ぶこと。
    /// </summary>
    public void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        InitBaseTriangle();
        _hue = H / 255.0 * 360.0;
        _sat = S / 255.0;
        _val = V / 255.0;

        RenderHueRing();
        UpdateTriangleGeometry();
        RenderTriangleGradient();
        UpdateHueIndicator();
        UpdateSvIndicator();
    }

    // ── DependencyProperty 変更コールバック ──
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

    // ── 三角形の基本ジオメトリ ──
    private void InitBaseTriangle()
    {
        // B = 純色(右), A = 白(左上), C = 黒(左下)
        _baseB = new Point(CX + TriR, CY);
        _baseA = new Point(CX - TriR / 2.0, CY - TriR * Math.Sqrt(3) / 2.0);
        _baseC = new Point(CX - TriR / 2.0, CY + TriR * Math.Sqrt(3) / 2.0);
    }

    private void UpdateTriangleGeometry()
    {
        // 追尾回転が無効、または角度固定が有効な場合は固定角度を使用
        double angle = (!_trackingRotationEnabled || _fixedAngleEnabled) ? _fixedAngle : _hue;
        double rad = angle * Math.PI / 180.0;
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

    // ── Hue リング描画 ──
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
            Width = 280, Height = 140,
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

                // アンチエイリアス
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

    // ── SV 三角形グラデーション描画（移植元のバリセントリック方式）──
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

    // ── インジケータ更新 ──
    private void UpdateHueIndicator()
    {
        // 追尾回転が無効、または角度固定が有効な場合は固定角度を使用
        double angle = (!_trackingRotationEnabled || _fixedAngleEnabled) ? _fixedAngle : _hue;
        var rad = angle * Math.PI / 180.0;
        var dir = new Vector(Math.Cos(rad), Math.Sin(rad));
        var center = new Point(CX, CY);
        var start = center + dir * IndicatorInnerR;
        var end = center + dir * IndicatorOuterR;

        _hueIndicatorShadow.X1 = start.X;
        _hueIndicatorShadow.Y1 = start.Y;
        _hueIndicatorShadow.X2 = end.X;
        _hueIndicatorShadow.Y2 = end.Y;

        _hueIndicator.X1 = start.X;
        _hueIndicator.Y1 = start.Y;
        _hueIndicator.X2 = end.X;
        _hueIndicator.Y2 = end.Y;
    }

    private void UpdateSvIndicator()
    {
        // 重心座標からポジションを算出
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

    // ── マウスイベント ──
    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_initialized) return;
        var pos = e.GetPosition(_canvas);
        var dist = (pos - new Point(CX, CY)).Length;

        if (dist >= RingInner && dist <= RingOuter + 4)
        {
            // Hue リングをドラッグ
            _isDragging = true;
            _dragRing = true;
            _dragTriangle = false;
            _canvas.CaptureMouse();
            UpdateHueFromPoint(pos);
        }
        else if (PointInTriangle(pos, _a, _b, _c))
        {
            // 三角形内 → SV 操作
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
        if (!_isDragging) return;
        var pos = e.GetPosition(_canvas);
        if (_dragRing) UpdateHueFromPoint(pos);
        else if (_dragTriangle) UpdateSvFromPoint(pos);
        e.Handled = true;
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
        var angle = Math.Atan2(pos.Y - CY, pos.X - CX) * 180.0 / Math.PI;
        if (angle < 0) angle += 360.0;
        _hue = angle;

        var newH = (byte)Math.Clamp(_hue / 360.0 * 255.0, 0, 255);

        _updating = true;
        try { H = newH; }
        finally { _updating = false; }

        UpdateTriangleGeometry();
        RenderTriangleGradient();
        UpdateHueIndicator();
        UpdateSvIndicator();
        OnColorChanged?.Invoke(H, S, V);
    }

    // 移植元の「三角形の外にドラッグしても最も近い辺の上にクランプする」処理。
    // HsvColorWheel（四角形）の単純な軸クランプとは異なり、三角形の3辺のうち
    // 最も近い辺への最近接点を求めて、そこにインジケータを追従させる。
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

    // ── ユーティリティ ──
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

    /// <summary>HSV (hue 0–360, sat/val 0–1) → Color</summary>
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
