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
        // ⚠️ 跨平台密文互不通用（Windows 读不懂 Keystore 密文、反之亦然）这条规则
        // **必须写在具体平台的实现里**，不能写在这层共享代码里：
        // Android 自己的密文就是 "and-v1:" 前缀，若在此处拦截，Android 将永远解不开自己加密的数据
        // （症状：DeviceId 退化成密文被当作设备号发出去 → 风控 9074「参与用户太多」）。
        // 现在的分工：这里只管分发，平台差异由 UnprotectCore 各自处理
        // （Windows 实现会拒绝 "and-v1:" 前缀，见 Secrets.Windows.cs）。
        return UnprotectCore(enc);
    }

    private static partial string? ProtectCore(string plain);
    private static partial string? UnprotectCore(string enc);
}
