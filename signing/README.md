# 签名密钥（自发行用）

本目录存放 TraeTools Android 版**自发行 APK** 的签名密钥（非 Google Play 上架用途）：

| 项 | 值 |
|---|---|
| 文件 | `traetools.keystore`（PKCS#12，PBE-SHA1-3DES legacy 编码） |
| 别名（alias） | `traetools` |
| 存储口令 / 密钥口令 | `traetools` |
| 证书主体 | `CN=TraeTools, O=akexin, C=CN`（自签名，RSA 4096 / SHA256withRSA，有效期 30 年） |
| 指纹基准 | `EXPECTED_CERT_SHA256.txt`（release.yml 的校验门禁比对此值） |

## 为什么提交到仓库

APK 升级安装要求「新旧版本签名一致」。GitHub Actions 每次运行的
`~/.android/debug.keystore` 都是临时生成的，用它签名会导致**每个版本签名不同、
无法覆盖安装**。因此固定使用本密钥，保证同一密钥期内各版本签名一致。

## 更换密钥

不要手工创建密钥：本机（及多数 CI 环境）没有 JRE 时，用 OpenSSL/Python 等工具生成的
PKCS#12 在 Java 侧的 alias 映射存在不确定性，容易出现「CI 签名步骤失败」或
「指纹对不上」的问题。

请改用仓库内置的手动工作流：

```
Actions → Rotate Keystore → Run workflow
```

它用构建机上的 `keytool` 生成密钥（alias / 口令 / 编码格式与 `release.yml` 完全一致），
自动更新本目录的 `EXPECTED_CERT_SHA256.txt`，并把两者提交回当前分支。

> ⚠️ 更换密钥后，**所有已安装用户都无法覆盖安装**，必须先卸载旧版再装新版。
> 这是签名机制的硬约束，无法绕过。

## 签名校验门禁

`release.yml` 的 `publish-android` job 在发布前会做三项硬校验，任一不通过即中断发布：

1. `apksigner verify --verbose --print-certs` 退出码为 0（签名整体有效）；
2. 签名证书 SHA-256 与 `EXPECTED_CERT_SHA256.txt` **完全一致**（防止密钥被换或产物来源不对）；
3. 结构性校验 **v1(JAR) + v2 + v3 三种签名方案齐全**（直接解析 APK 二进制，不依赖
   apksigner 的文本输出格式——不同 build-tools 版本行格式不一致）。

历史上这里只是 `apksigner verify ... | head -8 || true`，既不校验退出码也不比对指纹，
签名出问题时 CI 依然全绿，只能在用户手机上表现为「签名验证不通过」。

## 安全边界（请阅读）

- 该密钥**随公开仓库发布**，仅用于保证签名一致性，**不具备任何保密性**；
  任何人持有它都可以签发与官方版本同签名的 APK。
- 请不要用它签名任何需要保密的发布渠道（Google Play 等）。
