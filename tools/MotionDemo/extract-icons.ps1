# Extrait en 256 px l'icone de quelques executables installes, pour les clips de demonstration.
# Les icones vont dans %TEMP%\dockpad-motion-icons et ne sont jamais copiees dans le depot :
# ce sont des logos de produits. Une application absente donne simplement une tuile sans icone.
$ErrorActionPreference = 'Stop'
$out = Join-Path $env:TEMP 'dockpad-motion-icons'
New-Item -ItemType Directory -Force $out | Out-Null

# Windows PowerShell 5.1 : son compilateur est un C# 5, d'ou l'absence de `out var` et de `using var`.
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Imaging;

public static class ShellIcon
{
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory { void GetImage(SIZE size, int flags, out IntPtr phbm); }

    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }

    [DllImport("gdi32.dll")] static extern int GetObject(IntPtr h, int size, ref BITMAP bm);

    public static void Save(string exe, string png, int size)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        IShellItemImageFactory factory;
        SHCreateItemFromParsingName(exe, IntPtr.Zero, ref iid, out factory);
        // SIIGBF_ICONONLY (0x4) | SIIGBF_BIGGERSIZEOK (0x1)
        IntPtr hbm;
        factory.GetImage(new SIZE { cx = size, cy = size }, 0x5, out hbm);
        try
        {
            // Le HBITMAP est une section DIB 32 bits a alpha premultiplie : on recopie ses octets
            // plutot que de passer par Image.FromHbitmap, qui jette le canal alpha.
            var bm = new BITMAP();
            GetObject(hbm, Marshal.SizeOf(typeof(BITMAP)), ref bm);
            var img = new Bitmap(bm.bmWidth, bm.bmHeight, PixelFormat.Format32bppPArgb);
            var data = img.LockBits(new Rectangle(0, 0, bm.bmWidth, bm.bmHeight), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            var row = new byte[bm.bmWidthBytes];
            for (int y = 0; y < bm.bmHeight; y++)
            {
                Marshal.Copy(new IntPtr(bm.bmBits.ToInt64() + (long)y * bm.bmWidthBytes), row, 0, row.Length);
                Marshal.Copy(row, 0, new IntPtr(data.Scan0.ToInt64() + (long)(bm.bmHeight - 1 - y) * data.Stride), row.Length);
            }
            img.UnlockBits(data);
            img.Save(png, ImageFormat.Png);
            img.Dispose();
        }
        finally { DeleteObject(hbm); }
    }
}
'@

$candidates = [ordered]@{
    chrome     = @("$env:ProgramFiles\Google\Chrome\Application\chrome.exe", "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe", "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe")
    edge       = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe")
    powershell = @("$env:ProgramFiles\PowerShell\7\pwsh.exe", "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe")
    notepad    = @("$env:WINDIR\System32\notepad.exe")
    explorer   = @("$env:WINDIR\explorer.exe")
    taskmgr    = @("$env:WINDIR\System32\Taskmgr.exe")
    terminal   = @("$env:LOCALAPPDATA\Microsoft\WindowsApps\wt.exe")
}

foreach ($name in $candidates.Keys) {
    $exe = $candidates[$name] | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $exe) { Write-Host "absent : $name"; continue }
    try { [ShellIcon]::Save($exe, (Join-Path $out "$name.png"), 256); Write-Host "ok : $name" }
    catch { Write-Host "echec : $name ($($_.Exception.GetType().Name))" }
}
