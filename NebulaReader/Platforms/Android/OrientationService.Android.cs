using Android.Content.PM;

namespace NebulaReader.Services;

public sealed partial class OrientationService
{
    private partial void ApplyRotationLockPlatform(bool locked)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var activity = Platform.CurrentActivity;
            if (activity == null) return;

            activity.RequestedOrientation = locked
                ? ScreenOrientation.Locked
                : ScreenOrientation.Unspecified;
        });
    }
}

