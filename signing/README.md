# 签名密钥（自发行用）

本目录存放 TraeTools Android 版**自发行 APK** 的签名密钥（非 Google Play 上架用途）：

| 项 | 值 |
|---|---|
| 文件 | `traetools.keystore`（PKCS#12，PBE-SHA1-3DES） |
| 别名（alias） | `traetools` |
| 存储口令 / 密钥口令 | `traetools` |
| 证书主体 | `CN=TraeTools, O=akexin, C=CN`（自签名，有效期 30 年） |

## 为什么提交到仓库

APK 升级安装要求「新旧版本签名一致」。GitHub Actions 每次运行的
`~/.android/debug.keystore` 都是临时生成的，用它签名会导致**每个版本签名不同、
无法覆盖安装**。因此固定使用本密钥，保证从 v1.4.0 起的各版本签名一致。

## 安全边界（请阅读）

- 该密钥**随公开仓库发布**，仅用于保证签名一致性，**不具备任何保密性**；
  任何人持有它都可以签发与官方版本同签名的 APK。
- 请不要用它签名任何需要保密的发布渠道（Google Play 等）。
- 如需换成自己的私有密钥：重新生成 keystore（`keytool -genkeypair ...`），
  替换本文件，并同步更新 `.github/workflows/release.yml` 中的
  `AndroidSigningKeyStore` / `AndroidSigningKeyAlias` / `AndroidSigningKeyPass` /
  `AndroidSigningStorePass` 四个参数。
  ⚠️ 更换密钥后，旧版本无法直接覆盖安装（需先卸载旧版）。
