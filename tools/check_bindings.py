# -*- coding: utf-8 -*-
"""绑定严格校验。

本项目大量使用 x:DataType + 反射式混合绑定，且 XAML 里的绑定名写错**不会**在
编译期报错（DataTemplate 内部更是退化为运行时反射），只会在界面上静默失效。
本脚本把每个 View 的绑定与其 ViewModel 声明的成员做交叉比对：

· DataTemplate 之外的绑定 —— 必须能在该 View 对应的 ViewModel（或其基类文件）里找到；
  找不到即视为可疑（打错字、或忘了加属性）。
· DataTemplate 之内的绑定 —— 数据项类型未知，允许回退到「全项目任意类型都声明过」。

命令绑定（XxxCommand）由 [RelayCommand] 源生成，脚本会把注解的方法名换算成命令名。
"""
import os
import re
import sys
import glob

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "repo"))

ATTR_BIND_RE = re.compile(r"\{Binding\s+([^}]+)\}")

SKIP_TOKENS = {
    "Path", "Mode", "Converter", "ConverterParameter", "StringFormat",
    "RelativeSource", "Source", "FallbackValue", "TargetNullValue",
    "UpdateSourceTrigger", "ElementName", "Async", "Priority",
    "$parent", "$self", "AncestorType",
}

DECL_PATTERNS = [
    # 显式属性/字段/方法声明
    re.compile(
        r"\b(?:public|private|internal|protected|static|readonly|partial|sealed|override|"
        r"virtual|new|async|required|\s)+"
        r"(?:[A-Za-z_][A-Za-z0-9_<>,\[\]\?\.\s]*?)\s+"
        r"([A-Za-z_][A-Za-z0-9_]*)\s*(?:\{|=>|\(|;|=)"
    ),
]


def declared_names(src):
    names = set()

    # [ObservableProperty] private string _foo; -> Foo
    for m in re.finditer(r"\[ObservableProperty\][\s\S]{0,300}?\b_([A-Za-z_][A-Za-z0-9_]*)\s*[;=]", src):
        raw = m.group(1)
        names.add(raw[0].upper() + raw[1:])
        names.add(raw)

    for pat in DECL_PATTERNS:
        for m in pat.finditer(src):
            names.add(m.group(1))

    # [RelayCommand] 方法 -> XxxCommand
    # 注意修饰符链（private void / public async Task）允许出现空格，故用 (\w+\s+)+ 而不是单个 [\w]+
    for m in re.finditer(
        r"\[RelayCommand[^\]]*\]\s*(?:partial\s+)?(?:[\w<>\[\]\.\?,]+\s+)+"
        r"([A-Za-z_][A-Za-z0-9_]*)\s*\(",
        src,
    ):
        n = m.group(1)
        names.add(n + "Command")
        names.add(n)
        base = n[:-5] if n.endswith("Async") else n
        names.add(base + "Command")
        names.add(base)

    for m in re.finditer(r"\bevent\s+[\w<>\[\]\.\?]+\s+([A-Za-z_][A-Za-z0-9_]*)", src):
        names.add(m.group(1))

    # 属性表达式体里的计算属性（上面正则已覆盖大部分）
    for m in re.finditer(r"\b([A-Z][A-Za-z0-9_]*)\s*=>", src):
        names.add(m.group(1))

    return names


def collect_bindings(axaml_path):
    """返回 [(绑定表达式, 行号, 是否在 DataTemplate 内)]"""
    with open(axaml_path, encoding="utf-8-sig") as f:
        lines = f.readlines()

    out = []
    depth = 0  # DataTemplate 嵌套深度
    for i, line in enumerate(lines, 1):
        opened = line.count("<DataTemplate")
        closed = line.count("</DataTemplate>")
        in_tpl = depth > 0 or opened > 0

        for m in ATTR_BIND_RE.finditer(line):
            body = m.group(1)
            cand = None
            for p in [x.strip() for x in body.split(",")]:
                if not p:
                    continue
                if "=" in p:
                    k, v = p.split("=", 1)
                    if k.strip() == "Path":
                        cand = v.strip()
                    continue
                cand = p
                break
            if not cand:
                continue
            root = re.split(r"[\.\[\s]", cand.lstrip("!"))[0]
            if not root or root in SKIP_TOKENS:
                continue
            out.append((cand, i, in_tpl))

        depth += opened - closed
        if depth < 0:
            depth = 0
    return out


def main():
    # 全项目声明名（供 DataTemplate 内回退）
    global_names = set()
    models_names = set()
    vm_names = {}
    for cs in glob.glob(os.path.join(REPO, "**", "*.cs"), recursive=True):
        if os.sep + "obj" + os.sep in cs or os.sep + "bin" + os.sep in cs:
            continue
        try:
            src = open(cs, encoding="utf-8-sig").read()
        except Exception:
            continue
        names = declared_names(src)
        global_names |= names
        rel = os.path.relpath(cs, REPO)
        if rel.startswith("Models") or rel.startswith("Services"):
            models_names |= names
        if rel.startswith("ViewModels"):
            vm_names.setdefault(os.path.basename(cs)[:-3], names)

    # ViewModelBase 的属性对所有 VM 可见
    base_names = set()
    vb = os.path.join(REPO, "ViewModels", "ViewModelBase.cs")
    if os.path.exists(vb):
        base_names = declared_names(open(vb, encoding="utf-8-sig").read())

    problems = []
    total = 0
    dtype_re = re.compile(r'x:DataType="(?:vm:)?([A-Za-z_][A-Za-z0-9_]*)"')
    for view in sorted(glob.glob(os.path.join(REPO, "Views", "*.axaml"))):
        head = open(view, encoding="utf-8-sig").read()
        m = dtype_re.search(head)
        # 以视图自己声明的 x:DataType 为准（ViewLocator 是「把 View 换成 ViewModel」，
        # 不是追加，且对话框类视图根本没有对应 ViewModel）
        owned = set()
        if m:
            owned = vm_names.get(m.group(1), set())
        owned |= base_names

        for cand, line, in_tpl in collect_bindings(view):
            total += 1
            root = re.split(r"[\.\[\s]", cand.lstrip("!"))[0]
            if root in owned:
                continue
            if in_tpl and (root in global_names or root in models_names):
                continue
            problems.append((os.path.basename(view), line, cand, in_tpl))

    print("校验 %d 个绑定表达式" % total)
    if problems:
        print("\n可疑绑定（VM 与模板类型里都找不到）：")
        for v, ln, c, t in problems:
            print("  %-24s L%-5d %-42s %s" % (v, ln, c, "[模板内]" if t else ""))
    else:
        print("全部绑定均可解析 ✓")
    return 0


if __name__ == "__main__":
    sys.exit(main())
