using HarmonyLib;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Test94.Controls;
using YukkuriMovieMaker.Controls;

namespace Test94;

/// <summary>
/// ColorPicker.OnApplyTemplate() の Postfix パッチ。
/// ポップアップ内に「標準」「△ホイール（移植）」「□ホイール」のタブ切り替えUIを注入する。
///
/// ・□ホイールは本プロジェクトに元々あった <see cref="HsvColorWheel"/>（WheelMode.Square）。
/// ・△ホイールは今回、外部のノードエディタ用カラーピッカー
///   （YukkuriMovieMaker.Plugin.Community.Effect.Video.Node.Editor.Control.ColorPicker）から
///   移植した <see cref="TriangleColorWheel"/>。
/// 2つは別コントロールとして同じセルに重ねて配置し、タブでVisibilityを切り替える。
/// </summary>
[HarmonyPatch(typeof(ColorPicker), "OnApplyTemplate")]
internal class ColorPickerPatch
{
    private const string InjectedTag = "ColorWheelTabBar";

    // ColorPicker インスタンスごとの状態管理
    private static readonly ConditionalWeakTable<ColorPicker, WheelState> _states = new();

    // Slider_ValueChanged メソッド（Value更新用）
    private static readonly MethodInfo? _sliderValueChangedMethod =
        typeof(ColorPicker).GetMethod("Slider_ValueChanged",
            BindingFlags.Instance | BindingFlags.NonPublic);

    // mainHSlider フィールド（ValueChanged 呼び出し時の sender 用）
    private static readonly FieldInfo? _mainHSliderField =
        typeof(ColorPicker).GetField("mainHSlider",
            BindingFlags.Instance | BindingFlags.NonPublic);

    // popup フィールド
    private static readonly FieldInfo? _popupField =
        typeof(ColorPicker).GetField("popup",
            BindingFlags.Instance | BindingFlags.NonPublic);

    private class WheelState
    {
        public TriangleColorWheel? TriangleWheel;
        public Border? TriangleContainer;
        public HsvColorWheel? SquareWheel;
        public Border? SquareContainer;
        public int ActiveTab; // 0=標準, 1=△ホイール（移植）, 2=□ホイール
        public bool TrackingRotationEnabled = true; // 追尾回転の状態（共通）
        public bool FixedAngleEnabled; // 角度固定の状態（共通）
        public double FixedAngle; // 固定角度（共通）
    }

    // テーマカラー取得用
    private static Brush? _cachedThemeBrush;
    private static readonly object _themeBrushLock = new object();

    private static Brush GetThemeColorBrush()
    {
        lock (_themeBrushLock)
        {
            if (_cachedThemeBrush != null)
                return _cachedThemeBrush;

            try
            {
                // CustomThemePluginからテーマカラーを取得しようと試みる
                var themeBrush = TryGetCustomThemeBrush();
                if (themeBrush != null)
                {
                    _cachedThemeBrush = themeBrush;
                    return themeBrush;
                }
            }
            catch
            {
                // テーマプラグインが存在しない場合やエラーの場合はフォールバック
            }

            // フォールバック: システムデフォルト色
            _cachedThemeBrush = new SolidColorBrush(SystemColors.ControlLightLightColor);
            return _cachedThemeBrush;
        }
    }

    private static Brush? TryGetCustomThemeBrush()
    {
        try
        {
            // YMM4のリソースからテーマカラーを取得
            var app = Application.Current;
            if (app != null)
            {
                // YMM4のリソースディクショナリから背景色を取得
                var resources = app.Resources;
                if (resources != null)
                {
                    // 一般的なWPFテーマリソースキーを試す
                    object[] resourceKeys = new object[]
                    {
                        "ThemeBackgroundColor",
                        "BackgroundColor",
                        "ControlLightLightColor",
                        "WindowBackgroundColor",
                        "PanelBackgroundColor"
                    };

                    foreach (var key in resourceKeys)
                    {
                        if (resources.Contains(key) && resources[key] is Brush brush)
                        {
                            return brush;
                        }
                    }

                    // CustomThemePluginのリソースもチェック
                    foreach (var resourceDict in resources.MergedDictionaries)
                    {
                        foreach (var key in resourceKeys)
                        {
                            if (resourceDict.Contains(key) && resourceDict[key] is Brush brush)
                            {
                                return brush;
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // リソース取得に失敗した場合はnullを返す
        }

        return null;
    }

    private static void Postfix(ColorPicker __instance)
    {
        try
        {
            InjectWheelUi(__instance);
        }
        catch
        {
            // YMM4 をクラッシュさせない
        }
    }

    private static void InjectWheelUi(ColorPicker picker)
    {
        // ── ポップアップ取得 ──
        var popup = _popupField?.GetValue(picker) as Popup;
        if (popup?.Child is not Grid shadowGrid) return;

        // ── 内側グリッド（426×274）を探す ──
        Grid? innerGrid = null;
        foreach (UIElement child in shadowGrid.Children)
        {
            if (child is Grid g && g.ColumnDefinitions.Count >= 3)
            {
                innerGrid = g;
                break;
            }
        }
        if (innerGrid == null) return;

        // ── 注入済みチェック ──
        foreach (UIElement child in innerGrid.Children)
        {
            if (child is FrameworkElement fe
                && fe.Tag is string tag
                && tag == InjectedTag)
                return;
        }

        // ── ステップ 1: タブ行を挿入 ──
        innerGrid.RowDefinitions.Insert(0, new RowDefinition { Height = GridLength.Auto });

        if (!double.IsNaN(innerGrid.Height))
            innerGrid.Height += 22;

        // 既存の子要素を 1 行下にずらす
        foreach (UIElement child in innerGrid.Children)
        {
            Grid.SetRow(child, Grid.GetRow(child) + 1);
        }

        // Slider_ValueChanged を叩いて ColorPicker 側の Value 表示を更新させる共通処理
        void NotifyValueChanged()
        {
            try
            {
                if ((bool)picker.GetValue(ColorPicker.IsValueChangingProperty)) return;

                var mainHSlider = _mainHSliderField?.GetValue(picker);
                if (mainHSlider != null && _sliderValueChangedMethod != null)
                {
                    _sliderValueChangedMethod.Invoke(picker,
                        new object[] { mainHSlider, EventArgs.Empty });
                }
            }
            catch { /* 安全に無視 */ }
        }

        // ── ステップ 2-a: △ホイール（移植した TriangleColorWheel）を作成 ──
        var triangleWheel = new TriangleColorWheel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        triangleWheel.SetBinding(TriangleColorWheel.HProperty,
            new Binding(nameof(ColorPicker.H)) { Source = picker, Mode = BindingMode.TwoWay });
        triangleWheel.SetBinding(TriangleColorWheel.SProperty,
            new Binding(nameof(ColorPicker.S)) { Source = picker, Mode = BindingMode.TwoWay });
        triangleWheel.SetBinding(TriangleColorWheel.VProperty,
            new Binding(nameof(ColorPicker.V)) { Source = picker, Mode = BindingMode.TwoWay });
        triangleWheel.OnColorChanged = (_, _, _) => NotifyValueChanged();

        // 状態変更イベントのハンドラ
        triangleWheel.OnTrackingRotationChanged += (enabled) =>
        {
            var s = _states.GetOrCreateValue(picker);
            s.TrackingRotationEnabled = enabled;
            if (s.SquareWheel != null)
            {
                s.SquareWheel.TrackingRotationEnabled = enabled;
            }
        };
        triangleWheel.OnFixedAngleEnabledChanged += (enabled) =>
        {
            var s = _states.GetOrCreateValue(picker);
            s.FixedAngleEnabled = enabled;
            if (s.SquareWheel != null)
            {
                s.SquareWheel.FixedAngleEnabled = enabled;
            }
        };
        triangleWheel.OnFixedAngleChanged += (angle) =>
        {
            var s = _states.GetOrCreateValue(picker);
            s.FixedAngle = angle;
            if (s.SquareWheel != null)
            {
                s.SquareWheel.FixedAngle = angle;
            }
        };

        var triangleContainer = new Border
        {
            Background = GetThemeColorBrush(),
            Child = triangleWheel,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Visibility = Visibility.Collapsed
        };
        Grid.SetRow(triangleContainer, 1);
        Grid.SetColumn(triangleContainer, 0);
        Panel.SetZIndex(triangleContainer, 10);
        innerGrid.Children.Add(triangleContainer);

        // ── ステップ 2-b: □ホイール（既存の HsvColorWheel／Squareモード）を作成 ──
        var squareWheel = new HsvColorWheel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        squareWheel.SetBinding(HsvColorWheel.HProperty,
            new Binding(nameof(ColorPicker.H)) { Source = picker, Mode = BindingMode.TwoWay });
        squareWheel.SetBinding(HsvColorWheel.SProperty,
            new Binding(nameof(ColorPicker.S)) { Source = picker, Mode = BindingMode.TwoWay });
        squareWheel.SetBinding(HsvColorWheel.VProperty,
            new Binding(nameof(ColorPicker.V)) { Source = picker, Mode = BindingMode.TwoWay });
        squareWheel.OnColorChanged = (_, _, _) => NotifyValueChanged();
        squareWheel.SetMode(WheelMode.Square); // このタブでは常に四角形モードのみ使用する

        // 状態変更イベントのハンドラ
        squareWheel.OnTrackingRotationChanged += (enabled) =>
        {
            var s = _states.GetOrCreateValue(picker);
            s.TrackingRotationEnabled = enabled;
            if (s.TriangleWheel != null)
            {
                s.TriangleWheel.TrackingRotationEnabled = enabled;
            }
        };
        squareWheel.OnFixedAngleEnabledChanged += (enabled) =>
        {
            var s = _states.GetOrCreateValue(picker);
            s.FixedAngleEnabled = enabled;
            if (s.TriangleWheel != null)
            {
                s.TriangleWheel.FixedAngleEnabled = enabled;
            }
        };
        squareWheel.OnFixedAngleChanged += (angle) =>
        {
            var s = _states.GetOrCreateValue(picker);
            s.FixedAngle = angle;
            if (s.TriangleWheel != null)
            {
                s.TriangleWheel.FixedAngle = angle;
            }
        };

        var squareContainer = new Border
        {
            Background = GetThemeColorBrush(),
            Child = squareWheel,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Visibility = Visibility.Collapsed
        };
        Grid.SetRow(squareContainer, 1);
        Grid.SetColumn(squareContainer, 0);
        Panel.SetZIndex(squareContainer, 10);
        innerGrid.Children.Add(squareContainer);

        // ── ステップ 3: タブバーを作成 ──
        var state = _states.GetOrCreateValue(picker);
        state.TriangleWheel = triangleWheel;
        state.TriangleContainer = triangleContainer;
        state.SquareWheel = squareWheel;
        state.SquareContainer = squareContainer;
        state.ActiveTab = 0;

        var tab0 = CreateTabButton("■ 標準", true);
        var tab1 = CreateTabButton("△ ホイール", false);
        var tab2 = CreateTabButton("□ ホイール", false);
        var tabs = new[] { tab0, tab1, tab2 };

        // タブ切り替えロジック
        tab0.MouseLeftButtonDown += (s, e) =>
        {
            if (state.ActiveTab == 0) return;
            state.ActiveTab = 0;
            SetTabActive(tabs, 0);
            triangleContainer.Visibility = Visibility.Collapsed;
            squareContainer.Visibility = Visibility.Collapsed;
            e.Handled = true;
        };

        tab1.MouseLeftButtonDown += (s, e) =>
        {
            if (state.ActiveTab == 1) return;
            state.ActiveTab = 1;
            SetTabActive(tabs, 1);
            squareContainer.Visibility = Visibility.Collapsed;
            triangleContainer.Visibility = Visibility.Visible;
            triangleWheel.EnsureInitialized();
            e.Handled = true;
        };

        tab2.MouseLeftButtonDown += (s, e) =>
        {
            if (state.ActiveTab == 2) return;
            state.ActiveTab = 2;
            SetTabActive(tabs, 2);
            triangleContainer.Visibility = Visibility.Collapsed;
            squareContainer.Visibility = Visibility.Visible;
            squareWheel.EnsureInitialized();
            e.Handled = true;
        };

        var tabBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Height = 22,
            Tag = InjectedTag,
        };
        tabBar.Children.Add(tab0);
        tabBar.Children.Add(tab1);
        tabBar.Children.Add(tab2);

        Grid.SetRow(tabBar, 0);
        Grid.SetColumn(tabBar, 0);
        Grid.SetColumnSpan(tabBar, 3);

        innerGrid.Children.Add(tabBar);
    }

    // ── タブボタン生成 ──
    private static Border CreateTabButton(string text, bool isActive)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = SystemColors.ControlTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        return new Border
        {
            Background = new SolidColorBrush(
                isActive ? SystemColors.ControlLightLightColor
                         : SystemColors.ControlColor),
            BorderBrush = SystemColors.ActiveBorderBrush,
            BorderThickness = new Thickness(1, 1, 1, isActive ? 0 : 1),
            CornerRadius = new CornerRadius(3, 3, 0, 0),
            Padding = new Thickness(6, 1, 6, 1),
            Margin = new Thickness(0, 0, 1, 0),
            Cursor = Cursors.Hand,
            Child = tb
        };
    }

    // ── タブ切り替え見た目 ──
    private static void SetTabActive(Border[] tabs, int activeIndex)
    {
        for (int i = 0; i < tabs.Length; i++)
        {
            bool active = i == activeIndex;
            tabs[i].Background = new SolidColorBrush(
                active ? SystemColors.ControlLightLightColor
                       : SystemColors.ControlColor);
            tabs[i].BorderThickness = new Thickness(1, 1, 1, active ? 0 : 1);
        }
    }
}
