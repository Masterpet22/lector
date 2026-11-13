using AndroidChoreographer = Android.Views.Choreographer;

namespace NebulaReader.Views;

public partial class ReaderPage
{
    private ChoreographerFrameCallback? _vsyncCallback;
    private bool _vsyncRunning;

    private partial void AttachPlatformInput(View surfaceView)
    {
        // En Android los gestos táctiles se gestionan vía MAUI GestureRecognizers sobre la vista OpenGL ES.
    }

    private partial void DetachPlatformInput()
    {
        StopVSyncLoop();
    }

    private partial void StartVSyncLoop()
    {
        if (_vsyncRunning) return;

        _vsyncRunning = true;
        _vsyncCallback ??= new ChoreographerFrameCallback(OnAndroidVSyncFrame);
        AndroidChoreographer.Instance?.PostFrameCallback(_vsyncCallback);
    }

    private partial void StopVSyncLoop()
    {
        if (!_vsyncRunning) return;

        _vsyncRunning = false;
        if (_vsyncCallback != null)
        {
            AndroidChoreographer.Instance?.RemoveFrameCallback(_vsyncCallback);
        }
    }

    private void OnAndroidVSyncFrame(long frameTimeNanos)
    {
        if (!_vsyncRunning) return;

        AdvanceVSyncAnimation();

        if (_vsyncRunning && _vsyncCallback != null)
        {
            AndroidChoreographer.Instance?.PostFrameCallback(_vsyncCallback);
        }
    }

    private sealed class ChoreographerFrameCallback(Action<long> callback)
        : Java.Lang.Object, AndroidChoreographer.IFrameCallback
    {
        public void DoFrame(long frameTimeNanos) => callback(frameTimeNanos);
    }
}
