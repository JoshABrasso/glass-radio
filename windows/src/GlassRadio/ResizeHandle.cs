using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace GlassRadio;

// UIElement.ChangeCursor is not present on this Windows App SDK version (confirmed by the
// compiler, not assumed), and ProtectedCursor is protected - a subclass is the documented way
// to reach it from outside a control's own code. This exists solely to give the pane splitters
// the standard SizeWestEast resize cursor on hover. Derives from Grid, not Border: Border is
// sealed in this SDK (confirmed by the compiler, CS0509), while Panel-derived types like Grid
// are not; Grid still supports Background for hit-testing and a single Rectangle child exactly
// as Border did, so nothing else about the splitter markup needs to change.
public class ResizeHandle : Grid
{
    public ResizeHandle()
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
    }
}
