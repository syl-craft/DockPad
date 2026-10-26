using System.Threading.Tasks;
using DockPad.Surfaces;

namespace DockPad.Services.StreamDock;

public interface IDockRenderer
{
    Task<SurfaceImage> RenderKeyAsync(DockKey key, bool pressed, int size);
    Task<SurfaceImage> RenderDisplayAsync(UsageDisplay? display, int size);
}
