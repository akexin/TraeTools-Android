namespace TraeTools.Platform;

/// <summary>
/// 敏感字段加密抽象（跨平台 partial）：
///   Windows：DPAPI（当前用户作用域），密文为纯 Base64（历史格式，无前缀）。
///   Android：Keystore（RSA 包裹 AES-256-GCM 密钥），密文带 "and-v1:" 前缀。
/// Protect 返回 null 表示加密失败（调用方按明文保存兜底或放弃保存）。
/// </summary>
internal static partial class Secrets
{
    /// <summary>Android 密文前缀；Windows 上遇到该前缀的密文直接判定解密失败（换机迁移场景）。</summary>
    public const string AndroidPrefix = "and-v1:";

    public static string? Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return plain;
        return ProtectCore(plain);
    }

    public static string? Unprotect(string? enc)
    {
        if (string.IsNullOrEmpty(enc)) return enc;
        // 跨平台密文互不通用：Windows 实例读到 Android 密文时明确失败，触发重新登录
        if (enc.StartsWith(AndroidPrefix, StringComparison.Ordinal)) return null;
        return UnprotectCore(enc);
    }

    private static partial string? ProtectCore(string plain);
    private static partial string? UnprotectCore(string enc);
}
