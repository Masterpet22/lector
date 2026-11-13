using SkiaSharp.Views.Maui.Controls;

namespace NebulaReader.Views;

public partial class ReaderPage
{
    private partial void AttachPlatformInput(SKCanvasView canvasView)
    {
        // En Android los gestos de pellizco, arrastre y doble toque se gestionan vía MAUI GestureRecognizers en XAML.
    }

    private partial void DetachPlatformInput()
    {
    }
}
