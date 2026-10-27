using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DockPad.StreamDock.Hid;

/// <summary>
/// Appels Win32 de l'accès HID : setupapi pour énumérer, hid pour décrire, kernel32 pour ouvrir, cfgmgr32 pour
/// suivre les branchements.
/// </summary>
internal static unsafe partial class HidNative
{
    public const uint GenericRead = 0x80000000, GenericWrite = 0x40000000;
    public const uint FileShareRead = 0x1, FileShareWrite = 0x2;
    public const uint OpenExisting = 3;
    public const uint FileFlagOverlapped = 0x40000000;

    public const int ErrorAccessDenied = 5, ErrorSharingViolation = 32;

    public const uint DigcfPresent = 0x2, DigcfDeviceInterface = 0x10;

    public const int HidpStatusSuccess = 0x00110000;

    public const uint CrSuccess = 0;
    public const int CmNotifyFilterTypeDeviceInterface = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct DeviceInterfaceData
    {
        public int Size;
        public Guid InterfaceClassGuid;
        public int Flags;
        public nint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HiddAttributes
    {
        public int Size;
        public ushort VendorId;
        public ushort ProductId;
        public ushort VersionNumber;
    }

    /// <summary>
    /// HIDP_CAPS réduit aux champs lus ; Size couvre les champs réservés et les compteurs qui suivent.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 64)]
    public struct HidpCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
    }

    /// <summary>
    /// CM_NOTIFY_FILTER pour une classe d'interface ; Size couvre l'union, dont le plus grand membre est un id d'instance.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 416)]
    public struct CmNotifyFilter
    {
        public int Size;
        public int Flags;
        public int FilterType;
        public int Reserved;
        public Guid ClassGuid;
    }

    [LibraryImport("hid.dll")]
    public static partial void HidD_GetHidGuid(out Guid hidGuid);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool HidD_GetAttributes(SafeFileHandle device, ref HiddAttributes attributes);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool HidD_GetSerialNumberString(SafeFileHandle device, byte* buffer, int bufferLength);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool HidD_GetPreparsedData(SafeFileHandle device, out nint preparsedData);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool HidD_FreePreparsedData(nint preparsedData);

    [LibraryImport("hid.dll")]
    public static partial int HidP_GetCaps(nint preparsedData, out HidpCaps capabilities);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial DeviceInfoSetHandle SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, nint parentWindow, uint flags);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiEnumDeviceInterfaces(DeviceInfoSetHandle deviceInfoSet, nint deviceInfoData,
        ref Guid interfaceClassGuid, uint memberIndex, ref DeviceInterfaceData deviceInterfaceData);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiGetDeviceInterfaceDetail(DeviceInfoSetHandle deviceInfoSet,
        ref DeviceInterfaceData deviceInterfaceData, nint deviceInterfaceDetailData, int deviceInterfaceDetailDataSize,
        out int requiredSize, nint deviceInfoData);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiDestroyDeviceInfoList(nint deviceInfoSet);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode,
        nint securityAttributes, uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CancelIoEx(SafeFileHandle file, nint overlapped);

    [LibraryImport("cfgmgr32.dll")]
    public static partial uint CM_Register_Notification(ref CmNotifyFilter filter, nint context,
        delegate* unmanaged<nint, nint, int, nint, int, int> callback, out nint notification);

    [LibraryImport("cfgmgr32.dll")]
    public static partial uint CM_Unregister_Notification(nint notification);

    /// <summary>
    /// Liste d'interfaces de SetupDiGetClassDevs, détruite par SetupDiDestroyDeviceInfoList.
    /// </summary>
    public sealed class DeviceInfoSetHandle() : SafeHandleZeroOrMinusOneIsInvalid(ownsHandle: true)
    {
        protected override bool ReleaseHandle() => SetupDiDestroyDeviceInfoList(handle);
    }
}
