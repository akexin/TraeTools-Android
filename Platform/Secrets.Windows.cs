#if !ANDROID
using System.Security.Cryptography;
using System.Text;

namespace TraeTools.Platform;

/// <summary>Windows 实现：DPAPI（当前用户作用域）加密敏感字段，密文为纯 Base64。</summary>
internal static partial class Secrets
{
    static partial string? ProtectCore(string plain)
    {
        try
        {
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(bytes);
        }
        catch { return null; }
    }

    static partial string? UnprotectCore(string enc)
    {
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(enc), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch { return null; }
    }
}
#endif
