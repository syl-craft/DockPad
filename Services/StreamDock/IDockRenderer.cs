using System.Threading.Tasks;
using DockPad.Models;
using DockPad.Surfaces;

namespace DockPad.Services.StreamDock;

public interface IDockRenderer
{
    Task<SurfaceImage> RenderKeyAsync(DockKey key, DockKeyStyle style, bool pressed, int size);
    Task<SurfaceImage> RenderSideStripAsync(SideStripContent content, SideStripStyle style, SurfaceSize size);
}
