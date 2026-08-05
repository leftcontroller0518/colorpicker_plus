using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Test94.Controls;

/// <summary>ホイール内部のSVエリアの表示モード</summary>
public enum WheelMode
{
    /// <summary>色相リング＋SV三角形</summary>
    Triangle,
    /// <summary>色相リング＋SV四角形</summary>
    Square
}

/// <summary>
/// 円形カラーホイール（色相リング＋SV三角形/四角形）コントロール。
/// XAMLを使わず、すべてC#コードでVisualTreeを構築する。
/// </summary>
public class HsvColorWheel : Grid
{
    // ── 定数 ──
    private const int CanvasSize = 150;
    private const double CX = 75.0;
    private const double CY = 75.0;
    private const double OuterR = 72.0;
    private const double InnerR = 55.0;
    private const double TriR = 53.0;

    // 四角形モード用定数（Hueリング内に内接する正方形）
    private const int SqSide = 74;
    // 四角形の右側の角をホイール中心の右側に配置（三角形の純色頂点Bに準拠）
    private const double SqLeft = CX - SqSide;  // 1（右側の角がCXになる）
    private const double SqTop = CY - SqSide / 2.0;   // 38

    // ── DependencyProperty ──
    public static readonly DependencyProperty HProperty =
        DependencyProperty.Register(nameof(H), typeof(byte), typeof(HsvColorWheel),
            new FrameworkPropertyMetadata((byte)0,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnHChanged));

    public static readonly DependencyProperty SProperty =
        DependencyProperty.Register(nameof(S), typeof(byte), typeof(HsvColorWheel),
            new FrameworkPropertyMetadata((byte)255,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSvChanged));

    public static readonly DependencyProperty VProperty =
        DependencyProperty.Register(nameof(V), typeof(byte), typeof(HsvColorWheel),
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
                UpdateSquareGeometry();
                RenderInnerGradient();
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
                UpdateSquareGeometry();
                RenderInnerGradient();
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
                UpdateSquareGeometry();
                RenderInnerGradient();
                UpdateHueIndicator();
                UpdateSvIndicator();
            }
        }
    }

    // ── ビジュアル要素 ──
    private readonly Canvas _canvas;
    private readonly Rectangle _hueRingRect;
    private readonly Polygon _svTriangle;
    private readonly Rectangle _svSquare;
    private readonly Line _hueIndicatorShadow;
    private readonly Line _hueIndicator;
    private readonly Ellipse _svIndicatorShadow;
    private readonly Ellipse _svIndicator;

    // ── 状態 ──
    private WheelMode _mode = WheelMode.Triangle;
    private Point _a, _b, _c;             // 三角形の回転後の頂点
    private Point _baseA, _baseB, _baseC;  // 三角形の基本頂点（未回転）
    private double _hue;                   // 0–360
    private double _sat = 1.0;             // 0–1
    private double _val = 1.0;             // 0–1
    private double _hueOffset;             // 色相リングの回転オフセット（度）
    private bool _isDragging;
    private bool _dragRing;
    private bool _dragInner;
    private bool _updating;                // 再帰防止フラグ
    private bool _initialized;
    private bool _fixedAngleEnabled;       // 角度固定が有効かどうか
    private double _fixedAngle = 0.0;      // 固定角度（度）
    private bool _trackingRotationEnabled = true; // 追尾回転が有効かどうか

    // セッション中の色相オフセット保持（全インスタンス共通）
    private static double _savedHueOffset;

    // ── Hueリングビットマップのキャッシュ ──
    private static WriteableBitmap? _cachedHueRing;

    // ── コンストラクタ ──
    public HsvColorWheel()
    {
        Width = CanvasSize;
        Height = CanvasSize;
        ClipToBounds = true;

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
        _svTriangle = new Polygon { IsHitTestVisible = false };
        _canvas.Children.Add(_svTriangle);

        // SV 四角形（初期状態は非表示）
        // 三角形の純色頂点B（右側の角）に準拠して、四角形の右側の角を回転基準点にする
        _svSquare = new Rectangle
        {
            Width = SqSide,
            Height = SqSide,
            Stroke = Brushes.Black,
            StrokeThickness = 1,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            RenderTransformOrigin = new Point(1.0, 0.5)  // 右側の角を回転基準点
        };
        Canvas.SetLeft(_svSquare, SqLeft);
        Canvas.SetTop(_svSquare, SqTop);
        _canvas.Children.Add(_svSquare);

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

        // コンテキストメニュー（色相オフセットプリセット）
        var ctxMenu = new ContextMenu();
        AddOffsetPreset(ctxMenu, "赤=右（ibisPaint / MediBang）", 0);
        AddOffsetPreset(ctxMenu, "赤=上（PaintShop Pro）", -90);
        AddOffsetPreset(ctxMenu, "赤=左（Krita）", 180);
        AddOffsetPreset(ctxMenu, "CLIP STUDIO PAINT（5π/6）", -150);
        ctxMenu.Items.Add(new Separator());
        var customItem = new MenuItem { Header = "カスタム角度..." };
        customItem.Click += (s, e) => ShowCustomOffsetDialog();
        ctxMenu.Items.Add(customItem);
        ctxMenu.Items.Add(new Separator());

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
                UpdateSquareGeometry();
                RenderInnerGradient();
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
                UpdateSquareGeometry();
                RenderInnerGradient();
                UpdateHueIndicator();
                UpdateSvIndicator();
            }
        };
        ctxMenu.Items.Add(fixedAngleItem);

        this.ContextMenu = ctxMenu;
    }

    private void AddOffsetPreset(ContextMenu menu, string header, double offset)
    {
        var item = new MenuItem { Header = header };
        item.Click += (s, e) => SetHueOffset(offset);
        menu.Items.Add(item);
    }

    private void ShowCustomOffsetDialog()
    {
        var win = new Window
        {
            Title = "色相オフセット",
            Width = 280, Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow
        };
        var sp = new StackPanel { Margin = new Thickness(12) };
        sp.Children.Add(new TextBlock
        {
            Text = $"色相リングの回転角度（度）を入力:\n現在: {_hueOffset:F1}°",
            Margin = new Thickness(0, 0, 0, 4)
        });
        var tb = new TextBox { Text = _hueOffset.ToString("F1") };
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
                SetHueOffset(v);
                win.Close();
            }
        };
        sp.Children.Add(btn);
        win.Content = sp;
        win.ShowDialog();
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
            Text = $"三角形/四角形の固定角度（度）を入力:\n現在: {_fixedAngle:F1}°",
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
                    UpdateSquareGeometry();
                    RenderInnerGradient();
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

    /// <summary>色相リングの回転オフセットを設定する（度）。</summary>
    public void SetHueOffset(double offset)
    {
        _hueOffset = offset;
        _savedHueOffset = offset;
        _hueRingRect.RenderTransform = new RotateTransform(offset, CX, CY);
        if (_initialized)
        {
            UpdateTriangleGeometry();
            UpdateSquareGeometry();
            RenderInnerGradient();
            UpdateHueIndicator();
            UpdateSvIndicator();
        }
    }

    // ── モード切替 ──
    public void SetMode(WheelMode mode)
    {
        _mode = mode;
        if (_mode == WheelMode.Triangle)
        {
            _svTriangle.Visibility = Visibility.Visible;
            _svSquare.Visibility = Visibility.Collapsed;
        }
        else
        {
            _svTriangle.Visibility = Visibility.Collapsed;
            _svSquare.Visibility = Visibility.Visible;
        }

        if (_initialized)
        {
            RenderInnerGradient();
            UpdateSvIndicator();
        }
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

        // 保存済みオフセットを適用
        _hueOffset = _savedHueOffset;
        _hueRingRect.RenderTransform = new RotateTransform(_hueOffset, CX, CY);

        RenderHueRing();
        UpdateTriangleGeometry();
        UpdateSquareGeometry();
        RenderInnerGradient();
        UpdateHueIndicator();
        UpdateSvIndicator();
    }

    // ── DependencyProperty 変更コールバック ──
    private static void OnHChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not HsvColorWheel w) return;
        w._hue = (byte)e.NewValue / 255.0 * 360.0;
        if (!w._updating && !w._isDragging && w._initialized)
        {
            w._updating = true;
            try
            {
                w.UpdateTriangleGeometry();
                w.UpdateSquareGeometry();
                w.RenderInnerGradient();
                w.UpdateHueIndicator();
                w.UpdateSvIndicator();
            }
            finally { w._updating = false; }
        }
    }

    private static void OnSvChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not HsvColorWheel w) return;
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

    // ── 四角形の回転（三角形の純色頂点B（右側の角）に準拠して回転させる） ──
    private void UpdateSquareGeometry()
    {
        // _svSquare は RenderTransformOrigin=(1.0,0.5) （右側の角）を基準点にしている
        // 四角形の右側の角がホイール中心の右側にある状態から、Hueに合わせて回転させる
        // 追尾回転が無効、または角度固定が有効な場合は固定角度を使用
        double angle = (!_trackingRotationEnabled || _fixedAngleEnabled) ? _fixedAngle : _hue;
        _svSquare.RenderTransform = new RotateTransform(angle + _hueOffset);
    }

    // ── Hue リング描画 ──
    private void RenderHueRing()
    {
        if (_cachedHueRing == null)
            _cachedHueRing = GenerateHueRingBitmap();
        _hueRingRect.Fill = new ImageBrush(_cachedHueRing);
    }

    private static WriteableBitmap GenerateHueRingBitmap()
    {
        var bmp = new WriteableBitmap(CanvasSize, CanvasSize, 96, 96,
                                     PixelFormats.Bgra32, null);
        int stride = CanvasSize * 4;
        var px = new byte[CanvasSize * stride];

        for (int y = 0; y < CanvasSize; y++)
        {
            for (int x = 0; x < CanvasSize; x++)
            {
                double dx = x - CX, dy = y - CY;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                int i = (y * CanvasSize + x) * 4;

                if (dist >= InnerR - 0.5 && dist <= OuterR + 0.5)
                {
                    double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
                    if (angle < 0) angle += 360.0;
                    var c = HsvToColor(angle, 1.0, 1.0);

                    // アンチエイリアス
                    double alphaInner = Math.Clamp((dist - (InnerR - 0.5)) * 2.0, 0, 1);
                    double alphaOuter = Math.Clamp(((OuterR + 0.5) - dist) * 2.0, 0, 1);
                    byte a = (byte)(alphaInner * alphaOuter * 255);

                    px[i + 0] = c.B;
                    px[i + 1] = c.G;
                    px[i + 2] = c.R;
                    px[i + 3] = a;
                }
            }
        }

        bmp.WritePixels(new Int32Rect(0, 0, CanvasSize, CanvasSize), px, stride, 0);
        return bmp;
    }

    // ── 内部グラデーション描画（モードに応じて三角形 or 四角形） ──
    private void RenderInnerGradient()
    {
        if (_mode == WheelMode.Triangle)
            RenderTriangleGradient();
        else
            RenderSquareGradient();
    }

    // ── SV 三角形グラデーション描画 ──
    private void RenderTriangleGradient()
    {
        double minX = Math.Min(_a.X, Math.Min(_b.X, _c.X));
        double minY = Math.Min(_a.Y, Math.Min(_b.Y, _c.Y));
        double maxX = Math.Max(_a.X, Math.Max(_b.X, _c.X));
        double maxY = Math.Max(_a.Y, Math.Max(_b.Y, _c.Y));
        int w = (int)Math.Ceiling(maxX - minX);
        int h = (int)Math.Ceiling(maxY - minY);
        if (w <= 0 || h <= 0) return;

        var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        int stride = w * 4;
        var px = new byte[h * stride];
        var pureHue = HsvToColor(_hue, 1.0, 1.0);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var p = new Point(minX + x, minY + y);
                Barycentric(p, _a, _b, _c, out double wA, out double wB, out double wC);
                int i = (y * w + x) * 4;

                if (wA >= -0.01 && wB >= -0.01 && wC >= -0.01)
                {
                    double cWA = Math.Max(wA, 0);
                    double cWB = Math.Max(wB, 0);
                    double cWC = Math.Max(wC, 0);
                    double sum = cWA + cWB + cWC;
                    if (sum > 0) { cWA /= sum; cWB /= sum; }

                    byte r = (byte)Math.Clamp(cWA * 255 + cWB * pureHue.R, 0, 255);
                    byte g = (byte)Math.Clamp(cWA * 255 + cWB * pureHue.G, 0, 255);
                    byte b = (byte)Math.Clamp(cWA * 255 + cWB * pureHue.B, 0, 255);
                    px[i + 0] = b;
                    px[i + 1] = g;
                    px[i + 2] = r;
                    px[i + 3] = 255;
                }
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

    // ── SV 四角形グラデーション描画 ──
    private void RenderSquareGradient()
    {
        var bmp = new WriteableBitmap(SqSide, SqSide, 96, 96, PixelFormats.Bgra32, null);
        int stride = SqSide * 4;
        var px = new byte[SqSide * stride];

        for (int y = 0; y < SqSide; y++)
        {
            for (int x = 0; x < SqSide; x++)
            {
                double s = (double)x / (SqSide - 1);
                double v = 1.0 - (double)y / (SqSide - 1);
                var c = HsvToColor(_hue, s, v);
                int i = (y * SqSide + x) * 4;
                px[i + 0] = c.B;
                px[i + 1] = c.G;
                px[i + 2] = c.R;
                px[i + 3] = 255;
            }
        }

        bmp.WritePixels(new Int32Rect(0, 0, SqSide, SqSide), px, stride, 0);
        _svSquare.Fill = new ImageBrush(bmp);
    }

    // ── インジケータ更新 ──
    private void UpdateHueIndicator()
    {
        // 追尾回転が無効、または角度固定が有効な場合は固定角度を使用
        double angle = (!_trackingRotationEnabled || _fixedAngleEnabled) ? _fixedAngle : _hue;
        double rad = (angle + _hueOffset) * Math.PI / 180.0;
        var dir = new Vector(Math.Cos(rad), Math.Sin(rad));
        var center = new Point(CX, CY);
        var start = center + dir * InnerR;
        var end = center + dir * OuterR;

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
        if (_mode == WheelMode.Triangle)
            UpdateSvIndicatorTriangle();
        else
            UpdateSvIndicatorSquare();
    }

    private void UpdateSvIndicatorTriangle()
    {
        // 重心座標からポジションを算出
        double wA = _val * (1.0 - _sat);
        double wB = _val * _sat;
        double wC = 1.0 - _val;
        var pos = new Point(
            wA * _a.X + wB * _b.X + wC * _c.X,
            wA * _a.Y + wB * _b.Y + wC * _c.Y);

        PositionIndicator(pos);
    }

    private void UpdateSvIndicatorSquare()
    {
        // 未回転ローカル座標で位置を求めてから、四角形の回転角度ぶんだけ
        // 順回転させてキャンバス上の実際の位置に変換する。
        // 四角形の右側の角（SqLeft + SqSide, SqTop + SqSide/2）が回転基準点
        double ix = SqLeft + _sat * SqSide;
        double iy = SqTop + (1.0 - _val) * SqSide;
        double rad = (_hue + _hueOffset) * Math.PI / 180.0;
        // 回転基準点は四角形の右側の角（CX, CY）
        var pos = RotatePoint(new Point(ix, iy), new Point(CX, CY), rad);
        PositionIndicator(pos);
    }

    private void PositionIndicator(Point pos)
    {
        Canvas.SetLeft(_svIndicatorShadow, pos.X - 8);
        Canvas.SetTop(_svIndicatorShadow, pos.Y - 8);
        Canvas.SetLeft(_svIndicator, pos.X - 7);
        Canvas.SetTop(_svIndicator, pos.Y - 7);

        var color = HsvToColor(_hue, _sat, _val);
        _svIndicator.Fill = new SolidColorBrush(color);

        _svIndicatorShadow.Visibility = Visibility.Visible;
        _svIndicator.Visibility = Visibility.Visible;
    }

    // ── マウスイベント ──
    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_initialized) return;
        var pos = e.GetPosition(_canvas);
        double dist = (pos - new Point(CX, CY)).Length;

        _isDragging = true;
        _canvas.CaptureMouse();

        if (dist >= InnerR && dist <= OuterR + 4)
        {
            // Hue リングをドラッグ
            _dragRing = true;
            _dragInner = false;
            UpdateHueFromPoint(pos);
        }
        else if (dist < InnerR)
        {
            // リング内側 → SV 操作（三角形 or 四角形）
            _dragInner = true;
            _dragRing = false;
            UpdateSvFromPoint(pos);
        }
        else
        {
            _isDragging = false;
            _canvas.ReleaseMouseCapture();
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        var pos = e.GetPosition(_canvas);
        if (_dragRing) UpdateHueFromPoint(pos);
        else if (_dragInner) UpdateSvFromPoint(pos);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        _dragRing = false;
        _dragInner = false;
        _canvas.ReleaseMouseCapture();
    }

    private void UpdateHueFromPoint(Point pos)
    {
        double screenAngle = Math.Atan2(pos.Y - CY, pos.X - CX) * 180.0 / Math.PI;
        if (screenAngle < 0) screenAngle += 360.0;
        _hue = (screenAngle - _hueOffset + 720.0) % 360.0;

        byte newH = (byte)Math.Clamp(_hue / 360.0 * 255.0, 0, 255);
        if (H == newH)
        {
            // byte値未変化 → インジケータ位置だけ微調整
            UpdateHueIndicator();
            return;
        }

        _updating = true;
        try { H = newH; }
        finally { _updating = false; }

        UpdateTriangleGeometry();
        UpdateSquareGeometry();
        RenderInnerGradient();
        UpdateHueIndicator();
        UpdateSvIndicator();
        OnColorChanged?.Invoke(H, S, V);
    }

    private void UpdateSvFromPoint(Point pos)
    {
        if (_mode == WheelMode.Triangle)
            UpdateSvFromPointTriangle(pos);
        else
            UpdateSvFromPointSquare(pos);
    }

    private void UpdateSvFromPointTriangle(Point pos)
    {
        Barycentric(pos, _a, _b, _c, out double wA, out double wB, out double wC);
        wA = Math.Clamp(wA, 0, 1);
        wB = Math.Clamp(wB, 0, 1);
        wC = Math.Clamp(wC, 0, 1);
        double sum = wA + wB + wC;
        if (sum > 0) { wA /= sum; wB /= sum; wC /= sum; }

        _val = Math.Clamp(wA + wB, 0, 1);
        _sat = _val > 0 ? Math.Clamp(wB / _val, 0, 1) : 0;

        byte newS = (byte)Math.Clamp(_sat * 255.0, 0, 255);
        byte newV = (byte)Math.Clamp(_val * 255.0, 0, 255);
        if (S == newS && V == newV)
        {
            UpdateSvIndicator();
            return;
        }

        _updating = true;
        try { S = newS; V = newV; }
        finally { _updating = false; }

        UpdateSvIndicator();
        OnColorChanged?.Invoke(H, S, V);
    }

    private void UpdateSvFromPointSquare(Point pos)
    {
        // 四角形は現在のHueに合わせて回転しているので、マウス座標を逆回転させて
        // 四角形の未回転ローカル座標系に戻してからS/Vを算出する。
        // 回転基準点は四角形の右側の角（CX, CY）
        double rad = (_hue + _hueOffset) * Math.PI / 180.0;
        var local = RotatePoint(pos, new Point(CX, CY), -rad);

        _sat = Math.Clamp((local.X - SqLeft) / SqSide, 0, 1);
        _val = Math.Clamp(1.0 - (local.Y - SqTop) / SqSide, 0, 1);

        byte newS = (byte)Math.Clamp(_sat * 255.0, 0, 255);
        byte newV = (byte)Math.Clamp(_val * 255.0, 0, 255);
        if (S == newS && V == newV)
        {
            UpdateSvIndicator();
            return;
        }

        _updating = true;
        try { S = newS; V = newV; }
        finally { _updating = false; }

        UpdateSvIndicator();
        OnColorChanged?.Invoke(H, S, V);
    }

    // ── ユーティリティ ──
    private static void Barycentric(Point p, Point a, Point b, Point c,
        out double wA, out double wB, out double wC)
    {
        double det = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
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
        int hi = (int)Math.Floor(hue / 60.0) % 6;
        double f = hue / 60.0 - Math.Floor(hue / 60.0);
        byte v = (byte)Math.Clamp(value * 255.0, 0, 255);
        byte p = (byte)Math.Clamp(v * (1.0 - saturation), 0, 255);
        byte q = (byte)Math.Clamp(v * (1.0 - f * saturation), 0, 255);
        byte t = (byte)Math.Clamp(v * (1.0 - (1.0 - f) * saturation), 0, 255);
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
