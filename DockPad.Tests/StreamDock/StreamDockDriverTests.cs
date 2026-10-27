using DockPad.StreamDock;
using DockPad.StreamDock.Hid;
using DockPad.Surfaces;

namespace DockPad.Tests;

/// <summary>
/// Pilote du Stream Dock sur la machine de test, dock branché ou non : la recherche n'ouvre les interfaces HID
/// qu'en interrogation et rend sans lever ; l'abonnement aux branchements est pris au premier abonné et rendu au dernier.
/// </summary>
public class StreamDockDriverTests
{
    [Fact]
    public void Recherche_RendSansLever()
    {
        var surfaces = HidDeviceFinder.Find();

        Assert.All(surfaces, surface =>
        {
            Assert.Equal("StreamDock", surface.DriverName);
            Assert.Equal("293S V3", surface.Model);
        });
    }

    [Fact]
    public void Pilote_SAppelleStreamDock()
    {
        IControlSurfaceDriver driver = new StreamDockDriver();

        Assert.Equal("StreamDock", driver.Name);
    }

    [Fact]
    public void AbonnementAuxBranchements_PrisPuisRendu()
    {
        var driver = new StreamDockDriver();
        Action changed = () => { };

        driver.SurfacesChanged += changed;
        Assert.True(driver.IsWatchingArrivals, "Le premier abonné prend l'abonnement Windows.");
        driver.SurfacesChanged -= changed;

        Assert.False(driver.IsWatchingArrivals);
    }
}
