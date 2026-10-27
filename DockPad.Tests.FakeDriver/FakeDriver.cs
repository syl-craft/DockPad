using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DockPad.Surfaces;

namespace DockPad.Tests.FakeDriver;

public sealed class FakeDriver : IControlSurfaceDriver
{
    public string Name => "FakeDriver";

    public event Action? SurfacesChanged
    {
        add { }
        remove { }
    }

    public IReadOnlyList<SurfaceInfo> FindSurfaces() => Array.Empty<SurfaceInfo>();

    public Task<IControlSurface> OpenAsync(SurfaceInfo surface, CancellationToken ct) =>
        throw new NotSupportedException("The fake driver cannot open a surface.");
}
