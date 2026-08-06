using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Test94.Settings;

public class ColorPickerPlusSettingsView : UserControl
{
    public ColorPickerPlusSettingsView()
    {
        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new StackPanel
            {
                Children =
                {
                    CreateWheelSection(
                        "三角形",
                        nameof(ColorPickerPlusSettings.TriangleTrackingRotationEnabled),
                        nameof(ColorPickerPlusSettings.TriangleFixedAngleEnabled),
                        nameof(ColorPickerPlusSettings.TriangleFixedAngle)),
                    CreateWheelSection(
                        "四角形",
                        nameof(ColorPickerPlusSettings.SquareTrackingRotationEnabled),
                        nameof(ColorPickerPlusSettings.SquareFixedAngleEnabled),
                        nameof(ColorPickerPlusSettings.SquareFixedAngle))
                }
            }
        };
    }

    private static Expander CreateWheelSection(
        string header,
        string trackingProperty,
        string fixedEnabledProperty,
        string fixedAngleProperty)
    {
        return new Expander
        {
            Header = header,
            IsExpanded = true,
            Margin = new Thickness(0, 0, 0, 6),
            Content = new StackPanel
            {
                Children =
                {
                    CreateCheckRow("追尾回転", trackingProperty),
                    CreateCheckRow("任意の角度で固定", fixedEnabledProperty),
                    new TextBlock
                    {
                        Text = "※カラーピッカー上を右クリックドラッグで角度を操作できます",
                        FontSize = 10,
                        Margin = new Thickness(12, 0, 0, 6),
                        Opacity = 0.6
                    }
                }
            }
        };
    }

    private static Grid CreateCheckRow(string label, string property)
    {
        var grid = CreateRowGrid();
        grid.Children.Add(new Label { Content = label });

        var checkBox = new CheckBox
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        checkBox.SetBinding(CheckBox.IsCheckedProperty, new Binding(property)
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        grid.Children.Add(checkBox);

        return grid;
    }

    private static Grid CreateRowGrid()
    {
        return new Grid
        {
            Margin = new Thickness(0, 3, 0, 3),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };
    }
}
