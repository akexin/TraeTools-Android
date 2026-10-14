# -*- coding: utf-8 -*-
"""C# 源码字符级结构自检。

为多目标 Avalonia 项目做编译前的廉价防线：不依赖 SDK，直接扫源码，
找出「花括号/圆括号不平衡」「字符串或注释未闭合」「文件被截断」「编码损坏」
这类低级但致命的错误。

关键点是必须正确跳过：
  - 行注释 / 块注释
  - 逐字字符串 @"..."（内部 "" 表示一个引号）
  - 原始字符串字面量（C# 11，连续三个及以上双引号包围，引号数可更多）
  - 插值字符串 $、$@、以及插值原始字符串（内部可能有 {}，其中还可能嵌字符串）
  - 字符字面量 '...' 与转义

实现方式：单遍状态机。插值字符串里的 {} 内容按代码处理（递归计数），
从而不会把插值表达式里的括号漏掉或误算。
"""
import glob
import os
import sys

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "repo"))

# 需要参与配对统计的括号
PAIRS = {"{": "}", "(": ")", "[": "]"}


def scan(src):
    """返回 (counts, errors)。
    counts: {'{':open数, '}':close数, ...}，仅统计真正的代码区
    errors: 结构性问题列表
    """
    counts = {c: 0 for c in "{}()[]"}
    errors = []

    i = 0
    n = len(src)
    line = 1

    # 插值洞栈：每个元素是该洞的 "起始括号深度快照"，用来知道何时洞结束
    # 简化处理：插值洞不需要精确配对，因为我们把洞内容当作普通代码扫描，
    # 洞结束时由外层状态机通过 '}' 识别（同时要避免与代码块混淆）。
    # 这里用 brace_stack 记录插值洞的嵌套：进入 $"{ 时 push，遇到配对的 } pop。
    interp_depth = []   # 每个插值洞的 { 深度（用 counts['{'] 的快照近似）

    while i < n:
        ch = src[i]
        if ch == "\n":
            line += 1

        # ---- 注释 ----
        if ch == "/" and i + 1 < n and src[i + 1] == "/":
            j = src.find("\n", i)
            i = n if j < 0 else j
            continue
        if ch == "/" and i + 1 < n and src[i + 1] == "*":
            j = src.find("*/", i + 2)
            if j < 0:
                errors.append("块注释未闭合（从第 %d 行开始）" % line)
                return counts, errors
            line += src.count("\n", i, j)
            i = j + 2
            continue

        # ---- 原始字符串 """...""" ----
        if ch == '"' and src.startswith('"""', i):
            k = 3
            while i + k < n and src[i + k] == '"':
                k += 1
            quote = '"' * k
            j = src.find(quote, i + k)
            if j < 0:
                errors.append("原始字符串未闭合（第 %d 行）" % line)
                return counts, errors
            line += src.count("\n", i, j)
            i = j + k
            continue

        # ---- 逐字字符串 @"..." ----
        if ch == "@" and i + 1 < n and src[i + 1] == '"':
            i += 2
            closed = False
            while i < n:
                if src[i] == '"':
                    if i + 1 < n and src[i + 1] == '"':
                        i += 2
                        continue
                    i += 1
                    closed = True
                    break
                if src[i] == "\n":
                    line += 1
                i += 1
            if not closed:
                errors.append("逐字字符串未闭合（第 %d 行）" % line)
                return counts, errors
            continue

        # ---- 插值字符串 $"..." / $@"..." / $"""...""" ----
        if ch == "$":
            j = i + 1
            verbatim = False
            if j < n and src[j] == "@":
                verbatim = True
                j += 1
            if j < n and src[j] == '"':
                if src.startswith('"""', j):
                    k = 3
                    while j + k < n and src[j + k] == '"':
                        k += 1
                    quote = '"' * k
                    end = src.find(quote, j + k)
                    if end < 0:
                        errors.append("插值原始字符串未闭合（第 %d 行）" % line)
                        return counts, errors
                    # 洞内容当作代码扫描：交给主循环（从 j+k 开始）
                    # 为简化，直接递归扫描洞内容
                    inner = src[j + k:end]
                    sub_counts, sub_err = scan(inner)
                    for key in counts:
                        counts[key] += sub_counts[key]
                    errors.extend(sub_err)
                    line += src.count("\n", i, end)
                    i = end + k
                    continue
                # 普通/逐字插值字符串：逐个字符走，遇到 { 进入洞
                i = j + 1
                depth = 0
                closed = False
                while i < n:
                    c = src[i]
                    if c == "\n":
                        line += 1
                        if not verbatim:
                            break
                    if c == "\\" and not verbatim:
                        i += 2
                        continue
                    if c == '"':
                        if verbatim and i + 1 < n and src[i + 1] == '"':
                            i += 2
                            continue
                        if depth == 0:
                            i += 1
                            closed = True
                            break
                    if c == "{" and i + 1 < n and src[i + 1] == "{":
                        i += 2
                        continue
                    if c == "}" and i + 1 < n and src[i + 1] == "}":
                        i += 2
                        continue
                    if c == "{":
                        # 插值洞：找到配对的 }
                        d = 1
                        k2 = i + 1
                        while k2 < n and d > 0:
                            cc = src[k2]
                            if cc == "{":
                                d += 1
                            elif cc == "}":
                                d -= 1
                            elif cc == '"':
                                # 洞里的字符串
                                k3 = k2 + 1
                                while k3 < n:
                                    if src[k3] == "\\":
                                        k3 += 2
                                        continue
                                    if src[k3] == '"':
                                        break
                                    if src[k3] == "\n":
                                        line += 1
                                    k3 += 1
                                k2 = k3
                            elif cc == "\n":
                                line += 1
                            k2 += 1
                        inner = src[i + 1:k2 - 1]
                        sub_counts, sub_err = scan(inner)
                        for key in counts:
                            counts[key] += sub_counts[key]
                        errors.extend(sub_err)
                        i = k2
                        continue
                    i += 1
                if not closed:
                    errors.append("插值字符串未闭合（第 %d 行）" % line)
                    return counts, errors
                continue

        # ---- 字符字面量 ----
        if ch == "'":
            # 可能是 char，也可能是泛型/间隔符误用；C# 里 ' 仅用于 char
            j = i + 1
            if j < n and src[j] == "\\":
                j += 2
            else:
                j += 1
            if j < n and src[j] == "'":
                i = j + 1
                continue
            # 不是合法的 char 字面量，按普通字符处理
            i += 1
            continue

        # ---- 普通字符串 ----
        if ch == '"':
            i += 1
            closed = False
            while i < n:
                if src[i] == "\\":
                    i += 2
                    continue
                if src[i] == '"':
                    i += 1
                    closed = True
                    break
                if src[i] == "\n":
                    break
                i += 1
            if not closed:
                errors.append("字符串未闭合（第 %d 行）" % line)
                return counts, errors
            continue

        # ---- 括号统计 ----
        if ch in counts:
            counts[ch] += 1
        i += 1

    return counts, errors


def check(path):
    problems = []
    raw = open(path, "rb").read()
    try:
        src = raw.decode("utf-8-sig")
    except UnicodeDecodeError as e:
        return ["非 UTF-8 编码: %s" % e]

    if "\ufffd" in src:
        problems.append("含替换字符 U+FFFD（编码损坏）")

    counts, errors = scan(src)
    problems.extend(errors)

    for op, cl in PAIRS.items():
        if counts[op] != counts[cl]:
            problems.append("%s%s 不平衡: %d 个 %s / %d 个 %s"
                            % (op, cl, counts[op], op, counts[cl], cl))

    tail = src.rstrip()
    last_line = tail.rsplit("\n", 1)[-1].strip()
    # 条件编译收尾（#endif / #pragma warning restore 等）是合法写法，不算异常
    if tail and not last_line.startswith("#") and not tail.endswith(("}", ";", "*/")):
        problems.append("结尾异常: %r" % tail[-40:])

    return problems


def main():
    files = sorted(glob.glob(os.path.join(REPO, "**", "*.cs"), recursive=True))
    files = [f for f in files
             if os.sep + "obj" + os.sep not in f and os.sep + "bin" + os.sep not in f]

    # 只报告本次改动相关的文件，减少噪声
    import subprocess
    try:
        out = subprocess.run(["git", "status", "--porcelain"], cwd=REPO,
                             capture_output=True, text=True, encoding="utf-8").stdout
        changed = set()
        for ln in out.splitlines():
            p = ln[3:].strip().strip('"')
            if p.endswith(".cs"):
                changed.add(os.path.normpath(os.path.join(REPO, p)))
    except Exception:
        changed = set()

    total = 0
    for f in files:
        p = check(f)
        if p:
            total += 1
            tag = " [本次改动]" if os.path.normpath(f) in changed else " [既有文件]"
            print("  %s%s" % (os.path.relpath(f, REPO), tag))
            for msg in p:
                print("      - %s" % msg)

    print("共检查 %d 个 .cs 文件，%d 个存在结构问题。" % (len(files), total))
    return 0


if __name__ == "__main__":
    sys.exit(main())
