using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace KafkaLens.Shared.Security;

/// <summary>macOS login Keychain (generic passwords, service "KafkaLens", account = key).</summary>
[SupportedOSPlatform("macos")]
public sealed class MacKeychainSecretStore : ISecretStore
{
    private const string SecurityFramework = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const int errSecSuccess = 0;
    private const int errSecItemNotFound = -25300;

    public string BackendName => "macOS Keychain";
    public bool IsPersistent => true;

    private static readonly byte[] Service = Encoding.UTF8.GetBytes(SecretKeys.ServiceName);

    public string? Get(string key)
    {
        var account = Encoding.UTF8.GetBytes(key);
        var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)Service.Length, Service, (uint)account.Length, account,
            out var length, out var data, out var item);
        if (status == errSecItemNotFound) return null;
        Check(status, "read");
        try
        {
            if (length == 0 || data == IntPtr.Zero) return string.Empty;
            var bytes = new byte[length];
            Marshal.Copy(data, bytes, 0, (int)length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            if (data != IntPtr.Zero) SecKeychainItemFreeContent(IntPtr.Zero, data);
            if (item != IntPtr.Zero) CFRelease(item);
        }
    }

    public void Set(string key, string secret)
    {
        var account = Encoding.UTF8.GetBytes(key);
        var bytes = Encoding.UTF8.GetBytes(secret);
        try
        {
            var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)Service.Length, Service, (uint)account.Length, account,
                out _, IntPtr.Zero, out var item);
            if (status == errSecSuccess)
            {
                try
                {
                    Check(SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)bytes.Length, bytes), "update");
                }
                finally
                {
                    CFRelease(item);
                }
                return;
            }
            if (status != errSecItemNotFound) Check(status, "read");

            Check(SecKeychainAddGenericPassword(IntPtr.Zero, (uint)Service.Length, Service, (uint)account.Length, account,
                (uint)bytes.Length, bytes, IntPtr.Zero), "write");
        }
        finally
        {
            Array.Clear(bytes);
        }
    }

    public void Delete(string key)
    {
        var account = Encoding.UTF8.GetBytes(key);
        var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)Service.Length, Service, (uint)account.Length, account,
            out _, IntPtr.Zero, out var item);
        if (status == errSecItemNotFound) return;
        Check(status, "read");
        try
        {
            Check(SecKeychainItemDelete(item), "delete");
        }
        finally
        {
            CFRelease(item);
        }
    }

    private static void Check(int status, string action)
    {
        if (status != errSecSuccess)
            throw new InvalidOperationException($"Could not {action} the macOS Keychain item (OSStatus {status}).");
    }

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainFindGenericPassword(IntPtr keychainOrArray,
        uint serviceNameLength, byte[] serviceName, uint accountNameLength, byte[] accountName,
        out uint passwordLength, out IntPtr passwordData, out IntPtr itemRef);

    // Overload used when only the item reference is needed (passwordData = NULL).
    [DllImport(SecurityFramework)]
    private static extern int SecKeychainFindGenericPassword(IntPtr keychainOrArray,
        uint serviceNameLength, byte[] serviceName, uint accountNameLength, byte[] accountName,
        out uint passwordLength, IntPtr passwordData, out IntPtr itemRef);

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainAddGenericPassword(IntPtr keychain,
        uint serviceNameLength, byte[] serviceName, uint accountNameLength, byte[] accountName,
        uint passwordLength, byte[] passwordData, IntPtr itemRef);

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainItemModifyAttributesAndData(IntPtr itemRef, IntPtr attrList, uint length, byte[] data);

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainItemDelete(IntPtr itemRef);

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr cf);
}
