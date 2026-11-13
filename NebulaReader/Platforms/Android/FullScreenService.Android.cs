using AndroidX.Core.View;

namespace NebulaReader.Services;

public sealed partial class FullScreenService
{
    private partial void ApplyFullScreenPlatform(bool enabled)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var activity = Platform.CurrentActivity;
            var window = activity?.Window;
            if (window == null)
            {
                return;
            }

            var decorView = window.DecorView;
            WindowCompat.SetDecorFitsSystemWindows(window, !enabled);
            var controller = WindowCompat.GetInsetsController(window, decorView);
            if (controller == null)
            {
                return;
            }

            if (enabled)
            {
                controller.SystemBarsBehavior = WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
                controller.Hide(WindowInsetsCompat.Type.SystemBars());
            }
            else
            {
                controller.Show(WindowInsetsCompat.Type.SystemBars());
            }
        });
    }
}

