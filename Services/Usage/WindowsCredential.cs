using System.Runtime.InteropServices;
using System.Text;

namespace DockPad.Services.Usage;

/// <summary>
/// Lecture d'un identifiant générique du Gestionnaire d'identification Windows.
/// </summary>
/// <remarks>
/// Lecture seule : DockPad n'écrit ni ne renouvelle jamais l'identifiant d'un autre outil. Le
/// contenu rendu est un secret — l'appelant le garde local à sa méthode.
/// </remarks>
public static class WindowsCredential
{
    private const int CredTypeGeneric = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    /// <summary>Le contenu de l'identifiant, décodé en UTF-8, ou <c>null</c> s'il n'existe pas.</summary>
    public static string? ReadGeneric(string target)
    {
        if (!CredRead(target, CredTypeGeneric, 0, out var pointer)) return null;

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero) return null;

            var blob = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
            return Encoding.UTF8.GetString(blob);
        }
        finally
        {
            CredFree(pointer);
        }
    }
}
