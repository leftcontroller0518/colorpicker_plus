using HarmonyLib;
using System;
using System.Reflection;
using System.Windows;
using YukkuriMovieMaker.Plugin;

namespace Test94;

public class ColorPickerWheelPlugin : IPlugin
{
    private static bool _initialized;

    static ColorPickerWheelPlugin()
    {
        if (_initialized) return;
        Initialize();
        _initialized = true;
    }

    public string Name => "カラーピッカーUIホイール拡張";

    private static void Initialize()
    {
        try
        {
            var harmony = new Harmony("com.test94.colorpickerwheelui");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"ColorPicker UI拡張プラグイン初期化エラー: {ex.Message}\n\n{ex.StackTrace}",
                "ColorPickerWheelPlugin Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
