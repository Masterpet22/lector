using Android.Views;
using AndroidX.Core.View;

namespace NebulaReader.Services;

public readonly record struct AndroidSafeInsets(double Left, double Top, double Right, double Bottom);

public static class AndroidSafeArea
{
    public static AndroidSafeInsets Current { get; private set; }

    public static event Action<AndroidSafeInsets>? Changed;

    public static void Attach(Android.Views.Window window)
    {
        var decorView = window.DecorView;
        ViewCompat.SetOnApplyWindowInsetsListener(decorView, new InsetsListener());
        ViewCompat.RequestApplyInsets(decorView);
    }

    private sealed class InsetsListener : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        public WindowInsetsCompat OnApplyWindowInsets(Android.Views.View? view, WindowInsetsCompat? insets)
        {
            ArgumentNullException.ThrowIfNull(view);
            ArgumentNullException.ThrowIfNull(insets);
            var safe = insets.GetInsets(WindowInsetsCompat.Type.SystemBars() |
                                        WindowInsetsCompat.Type.DisplayCutout());
            if (safe is null) return insets;
            var density = view.Resources?.DisplayMetrics?.Density ?? 1f;
            var next = new AndroidSafeInsets(safe.Left / density, safe.Top / density,
                                             safe.Right / density, safe.Bottom / density);
            if (next != Current)
            {
                Current = next;
                Changed?.Invoke(next);
            }

            // MAUI and its child views still receive the original insets.
            return insets;
        }
    }
}
