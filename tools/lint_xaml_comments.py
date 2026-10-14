"""扫描所有 .axaml 的 XML 注释，找出非法写法。

XML 规范不允许注释内容里出现连续两个短横线 `--`，也不允许注释以 `-` 结尾。
Avalonia 的 XAML 预处理会直接抛 `AVLN1001: An XML comment cannot contain '--'`
并**中止构建**，但报错只给行号不给原因，很容易反复踩（用 `----` 画分隔线时必踩）。

用法：
    python tools/lint_xaml_comments.py [仓库根目录]
退出码非 0 表示存在非法注释。
"""
import io
import os
import re
import sys

ROOT = sys.argv[1] if len(sys.argv) > 1 else "."

COMMENT_RE = re.compile(r"<!--(.*?)-->", re.S)

SKIP_DIRS = {"obj", "bin", ".git", "node_modules"}


def scan(root: str) -> int:
    bad = 0
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
        for name in filenames:
            if not name.endswith(".axaml"):
                continue
            path = os.path.join(dirpath, name)
            try:
                text = io.open(path, encoding="utf-8").read()
            except Exception as exc:  # 编码异常也值得报出来
                print(f"{path}: 无法读取（{exc}）")
                bad += 1
                continue
            for m in COMMENT_RE.finditer(text):
                inner = m.group(1)
                if "--" in inner or inner.endswith("-"):
                    line = text[: m.start()].count("\n") + 1
                    snippet = inner.strip().replace("\n", " ")[:70]
                    print(f"{path}:{line}: 非法注释 -> {snippet}")
                    bad += 1
    return bad


if __name__ == "__main__":
    n = scan(ROOT)
    print(f"\n非法注释总数: {n}")
    sys.exit(1 if n else 0)
