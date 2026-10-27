using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DockPad.StreamDock.Hid;

/// <summary>
/// Interfaces HID présentes du 293S V3 : VID, PID et usage page vendeur. Chaque interface est ouverte sans droit
/// de lecture ni d'écriture, ce qui suffit à la décrire sans gêner l'application qui la tient.
/// </summary>
internal static class HidDeviceFinder
{
    private const int SerialNumberBufferLength = 256;

    public static IReadOnlyList<SurfaceInfo> Find()
    {
        HidNative.HidD_GetHidGuid(out var hidGuid);
        using var deviceInfoSet = HidNative.SetupDiGetClassDevs(ref hidGuid, enumerator: null, parentWindow: 0,
            HidNative.DigcfPresent | HidNative.DigcfDeviceInterface);
        if (deviceInfoSet.IsInvalid)
            return [];

        var surfaces = new List<SurfaceInfo>();
        var interfaceData = new HidNative.DeviceInterfaceData { Size = Marshal.SizeOf<HidNative.DeviceInterfaceData>() };
        for (uint index = 0; HidNative.SetupDiEnumDeviceInterfaces(deviceInfoSet, 0, ref hidGuid, index, ref interfaceData); index++)
        {
            var devicePath = DevicePath(deviceInfoSet, ref interfaceData);
            if (devicePath == null)
                continue;

            var surface = Describe(devicePath);
            if (surface != null)
                surfaces.Add(surface);
        }
        return surfaces;
    }

    private static string? DevicePath(HidNative.DeviceInfoSetHandle deviceInfoSet, ref HidNative.DeviceInterfaceData interfaceData)
    {
        HidNative.SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, 0, 0, out var requiredSize, 0);
        if (requiredSize <= 0)
            return null;

        var detail = Marshal.AllocHGlobal(requiredSize);
        try
        {
            // cbSize de SP_DEVICE_INTERFACE_DETAIL_DATA_W : 8 en 64 bits (alignement), 6 en 32 bits.
            Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
            if (HidNative.SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, detail, requiredSize, out _, 0) == false)
                return null;
            return Marshal.PtrToStringUni(detail + sizeof(int));
        }
        finally
        {
            Marshal.FreeHGlobal(detail);
        }
    }

    /// <summary>
    /// Null si l'interface n'est pas celle du 293S V3 ou ne peut pas être interrogée.
    /// </summary>
    private static SurfaceInfo? Describe(string devicePath)
    {
        using var device = HidNative.CreateFile(devicePath, desiredAccess: 0,
            HidNative.FileShareRead | HidNative.FileShareWrite, securityAttributes: 0, HidNative.OpenExisting,
            flagsAndAttributes: 0, templateFile: 0);
        if (device.IsInvalid)
            return null;

        var attributes = new HidNative.HiddAttributes { Size = Marshal.SizeOf<HidNative.HiddAttributes>() };
        if (HidNative.HidD_GetAttributes(device, ref attributes) == false)
            return null;
        if (attributes.VendorId != StreamDockModel.VendorId || StreamDockModel.ProductIds.Contains(attributes.ProductId) == false)
            return null;
        if (IsVendorInterface(device) == false)
            return null;

        return new SurfaceInfo(StreamDockDriver.DriverName, devicePath, StreamDockSurface.ModelName, SerialNumber(device));
    }

    /// <summary>
    /// Le dock expose aussi des interfaces clavier ou consommateur : seule l'interface vendeur porte le protocole.
    /// </summary>
    private static bool IsVendorInterface(SafeFileHandle device)
    {
        if (HidNative.HidD_GetPreparsedData(device, out var preparsedData) == false)
            return false;
        try
        {
            if (HidNative.HidP_GetCaps(preparsedData, out var capabilities) != HidNative.HidpStatusSuccess)
                return false;
            return capabilities.UsagePage == StreamDockModel.UsagePage && capabilities.Usage == StreamDockModel.Usage;
        }
        finally
        {
            HidNative.HidD_FreePreparsedData(preparsedData);
        }
    }

    private static unsafe string? SerialNumber(SafeFileHandle device)
    {
        // Dernier caractère réservé et mis à zéro : même tronquée, la chaîne lue reste terminée.
        var buffer = stackalloc byte[SerialNumberBufferLength];
        buffer[SerialNumberBufferLength - 2] = 0;
        buffer[SerialNumberBufferLength - 1] = 0;
        if (HidNative.HidD_GetSerialNumberString(device, buffer, SerialNumberBufferLength - sizeof(char)) == false)
            return null;

        var serialNumber = new string((char*)buffer);
        return serialNumber.Length == 0 ? null : serialNumber;
    }
}
