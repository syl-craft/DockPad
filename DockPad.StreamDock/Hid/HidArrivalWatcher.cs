using System.Runtime.InteropServices;
using System.Threading;

namespace DockPad.StreamDock.Hid;

/// <summary>
/// Branchement ou débranchement d'une interface HID, sans fenêtre cachée. Le rappel Windows ne fait que poster
/// l'avis sur le pool de threads : les abonnés peuvent donc disposer le watcher sans attendre ce rappel.
/// </summary>
internal sealed unsafe class HidArrivalWatcher : IDisposable
{
    private const int ActionInterfaceArrival = 0, ActionInterfaceRemoval = 1;

    private readonly Action _changed;
    private GCHandle _self;
    private nint _notification;
    private volatile bool _isDisposed;

    /// <summary>
    /// Lève Win32Exception si Windows refuse l'abonnement.
    /// </summary>
    public HidArrivalWatcher(Action changed)
    {
        _changed = changed;
        HidNative.HidD_GetHidGuid(out var hidGuid);
        var filter = new HidNative.CmNotifyFilter
        {
            Size = sizeof(HidNative.CmNotifyFilter),
            FilterType = HidNative.CmNotifyFilterTypeDeviceInterface,
            ClassGuid = hidGuid,
        };

        _self = GCHandle.Alloc(this);
        var result = HidNative.CM_Register_Notification(ref filter, GCHandle.ToIntPtr(_self), &OnNotification, out _notification);
        if (result != HidNative.CrSuccess)
        {
            _self.Free();
            throw new System.ComponentModel.Win32Exception($"CM_Register_Notification failed (CONFIGRET {result}).");
        }
    }

    [UnmanagedCallersOnly]
    private static int OnNotification(nint notification, nint context, int action, nint eventData, int eventDataSize)
    {
        try
        {
            if (action is not (ActionInterfaceArrival or ActionInterfaceRemoval))
                return 0;
            if (GCHandle.FromIntPtr(context).Target is HidArrivalWatcher watcher)
                ThreadPool.UnsafeQueueUserWorkItem(static target => target.NotifyChanged(), watcher, preferLocal: false);
        }
        catch (Exception ex)
        {
            DriverTrace.Warning("HID arrival callback failed", ex);
        }
        return 0;
    }

    private void NotifyChanged()
    {
        if (_isDisposed)
            return;
        try
        {
            _changed();
        }
        catch (Exception ex)
        {
            DriverTrace.Warning("HID arrival subscriber failed", ex);
        }
    }

    /// <summary>
    /// Si Windows refuse le désabonnement, le GCHandle est gardé : un rappel peut encore recevoir son contexte.
    /// </summary>
    public void Dispose()
    {
        if (_notification == 0)
            return;

        _isDisposed = true;
        var result = HidNative.CM_Unregister_Notification(_notification);
        _notification = 0;
        if (result == HidNative.CrSuccess)
            _self.Free();
        else
            DriverTrace.Warning($"CM_Unregister_Notification failed (CONFIGRET {result}).");
    }
}
