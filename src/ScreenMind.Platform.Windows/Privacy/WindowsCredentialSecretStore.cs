using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using ScreenMind.Core.Privacy;

namespace ScreenMind.Platform.Windows.Privacy;

public sealed partial class WindowsCredentialSecretStore : ISecretStore
{
    private const string TargetPrefix = "ScreenMind:";
    private const int ErrorNotFound = 1168;

    public Task SaveAsync(string name, string secret, CancellationToken cancellationToken)
    {
        ValidateName(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        cancellationToken.ThrowIfCancellationRequested();

        byte[] secretBytes = Encoding.Unicode.GetBytes(secret);
        IntPtr blob = Marshal.AllocCoTaskMem(secretBytes.Length);
        try
        {
            Marshal.Copy(secretBytes, 0, blob, secretBytes.Length);
            NativeCredential credential = new()
            {
                Type = CredentialType.Generic,
                TargetName = BuildTargetName(name),
                CredentialBlobSize = (uint)secretBytes.Length,
                CredentialBlob = blob,
                Persist = CredentialPersist.LocalMachine,
                UserName = Environment.UserName,
            };

            IntPtr credentialPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf<NativeCredential>());
            try
            {
                Marshal.StructureToPtr(credential, credentialPtr, fDeleteOld: false);
                if (!NativeMethods.CredWrite(credentialPtr, 0))
                {
                    throw CreateWin32Exception("Credential Manager save failed.");
                }
            }
            finally
            {
                Marshal.DestroyStructure<NativeCredential>(credentialPtr);
                Marshal.FreeCoTaskMem(credentialPtr);
            }
        }
        finally
        {
            Array.Clear(secretBytes);
            Marshal.FreeCoTaskMem(blob);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string name, CancellationToken cancellationToken)
    {
        ValidateName(name);
        cancellationToken.ThrowIfCancellationRequested();

        bool exists = TryReadCredential(name, out IntPtr credentialPtr);
        if (credentialPtr != IntPtr.Zero)
        {
            NativeMethods.CredFree(credentialPtr);
        }

        return Task.FromResult(exists);
    }

    public Task<string?> GetAsync(string name, CancellationToken cancellationToken)
    {
        ValidateName(name);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryReadCredential(name, out IntPtr credentialPtr))
        {
            return Task.FromResult<string?>(null);
        }

        try
        {
            NativeCredential credential = Marshal.PtrToStructure<NativeCredential>(credentialPtr);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return Task.FromResult<string?>(string.Empty);
            }

            byte[] secretBytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, secretBytes, 0, secretBytes.Length);
            try
            {
                return Task.FromResult<string?>(Encoding.Unicode.GetString(secretBytes));
            }
            finally
            {
                Array.Clear(secretBytes);
            }
        }
        finally
        {
            NativeMethods.CredFree(credentialPtr);
        }
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken)
    {
        ValidateName(name);
        cancellationToken.ThrowIfCancellationRequested();

        if (!NativeMethods.CredDelete(BuildTargetName(name), CredentialType.Generic, 0))
        {
            int error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound)
            {
                throw new Win32Exception(error, "Credential Manager delete failed.");
            }
        }

        return Task.CompletedTask;
    }

    private static bool TryReadCredential(string name, out IntPtr credentialPtr)
    {
        if (NativeMethods.CredRead(BuildTargetName(name), CredentialType.Generic, 0, out credentialPtr))
        {
            return true;
        }

        int error = Marshal.GetLastWin32Error();
        if (error == ErrorNotFound)
        {
            credentialPtr = IntPtr.Zero;
            return false;
        }

        throw new Win32Exception(error, "Credential Manager read failed.");
    }

    private static Win32Exception CreateWin32Exception(string message)
        => new(Marshal.GetLastWin32Error(), message);

    private static string BuildTargetName(string name) => TargetPrefix + name;

    private static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("Secret name cannot contain null characters.", nameof(name));
        }
    }

    private enum CredentialType : uint
    {
        Generic = 1,
    }

    private enum CredentialPersist : uint
    {
        LocalMachine = 2,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;

        public CredentialType Type;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string TargetName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Comment;

        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;

        public uint CredentialBlobSize;

        public IntPtr CredentialBlob;

        public CredentialPersist Persist;

        public uint AttributeCount;

        public IntPtr Attributes;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? TargetAlias;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string UserName;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("Advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CredWrite(IntPtr credential, uint flags);

        [LibraryImport("Advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CredRead(
            string targetName,
            CredentialType type,
            uint reservedFlag,
            out IntPtr credentialPtr);

        [LibraryImport("Advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CredDelete(
            string targetName,
            CredentialType type,
            uint flags);

        [LibraryImport("Advapi32.dll")]
        public static partial void CredFree(IntPtr buffer);
    }
}
