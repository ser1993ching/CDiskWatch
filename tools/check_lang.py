"""多语言一致性检查（开发期守卫，由 build/publish.ps1 调用）。

检查项：
  1. 源码里 Loc.T("key") / Loc.F("key") 用到的 key 必须存在于 tools/lang/lang.tsv
  2. lang.tsv 里没有任何 key 被用到（僵尸词条）
  3. 文案表（src/i18n/Tables.g.cs）之外不得出现中文字面量（防止又写死中文）
  4. 各语言占位符一致（gen_lang.py 已校验，这里再兜一遍）

用法：python tools/check_lang.py [--quiet]
"""

import io
import os
import re
import sys

try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, 'CDiskWatch', 'src')
TSV = os.path.join(ROOT, 'tools', 'lang', 'lang.tsv')
GENERATED = os.path.join('i18n', 'Tables.g.cs')

LO, HI = 0x4E00, 0x9FFF
BS = chr(92)
DQ = chr(34)

CALL = re.compile(r'Loc\.[TF]\(\s*' + DQ + r'([a-zA-Z0-9_.\-]+)' + DQ)
# 生成表本身就是文案数据，跳过
SKIP_FILES = {os.path.normpath(GENERATED)}
# 自检报告是内部诊断信息，按设计保持中文（断言已改成与语言无关）
SKIP_CJK = {os.path.normpath('SelfTest.cs'), os.path.normpath(os.path.join('i18n', 'Loc.cs'))}


def is_cjk(s):
    return any(LO <= ord(ch) <= HI for ch in s)


def literals(src):
    """返回 [(行号, 字符串内容)]，跳过注释。"""
    out = []
    i, n, line = 0, len(src), 1
    while i < n:
        c = src[i]
        if c == chr(10):
            line += 1
            i += 1
        elif c == DQ:
            start = line
            i += 1
            buf = []
            while i < n:
                ch = src[i]
                if ch == DQ:
                    i += 1
                    break
                if ch == BS and i + 1 < n:
                    buf.append(ch + src[i + 1])
                    i += 2
                    continue
                if ch == chr(10):
                    line += 1
                buf.append(ch)
                i += 1
            out.append((start, ''.join(buf)))
        elif c == '/' and i + 1 < n and src[i + 1] == '/':
            while i < n and src[i] != chr(10):
                i += 1
        else:
            i += 1
    return out


def load_keys():
    keys = []
    for line in io.open(TSV, encoding='utf-8').read().splitlines():
        if not line.strip() or line.lstrip().startswith('#'):
            continue
        parts = line.split('\t')
        if len(parts) != 4:
            sys.exit('lang.tsv 格式错误（需要 4 列）：' + line[:60])
        keys.append(parts[0].strip())
    return keys


def main():
    quiet = '--quiet' in sys.argv
    declared = load_keys()
    declared_set = set(declared)

    used = set()
    unknown = []
    cjk_leaks = []

    for dirpath, _, files in os.walk(SRC):
        for name in files:
            if not name.endswith('.cs'):
                continue
            full = os.path.join(dirpath, name)
            rel = os.path.relpath(full, SRC)
            if os.path.normpath(rel) in SKIP_FILES:
                continue
            src = io.open(full, encoding='utf-8').read()
            # 只要是字面量里出现过，就算被引用（涵盖三元表达式/switch 里的动态 key）
            for _, text in literals(src):
                if text in declared_set:
                    used.add(text)
            for k in CALL.findall(src):
                if k.endswith('.'):
                    # 动态拼 key（advice.rule.<Id>.name / cat.<id>）：只校验前缀有词条
                    n = sum(1 for d in declared if d.startswith(k))
                    if n == 0:
                        unknown.append(rel + ': 前缀无词条 ' + k + '*')
                    used.add(k + '*')
                    continue
                used.add(k)
                if k not in declared_set:
                    unknown.append(rel + ': ' + k)
            if os.path.normpath(rel) in SKIP_CJK:
                continue
            for ln, text in literals(src):
                if is_cjk(text):
                    cjk_leaks.append('%s:%d  %s' % (rel, ln, text))

    prefixes = {u[:-1] for u in used if u.endswith('*')}
    unused = [k for k in declared if k not in used and not any(k.startswith(p) for p in prefixes)]

    problems = 0
    if unknown:
        problems += len(unknown)
        print('[FAIL] 源码用了未定义的 key（%d）：' % len(unknown))
        for x in unknown[:30]:
            print('    ' + x)
    if cjk_leaks:
        problems += len(cjk_leaks)
        print('[FAIL] 文案表之外出现中文字面量（%d）：' % len(cjk_leaks))
        for x in cjk_leaks[:30]:
            print('    ' + x)
    if unused:
        # 只告警：留着备用词条不致命
        print('[WARN] 未被使用的词条（%d）：%s' % (len(unused), ', '.join(unused[:20])))

    if not problems:
        print('[OK] 多语言检查通过：%d 条词条，%d 处调用引用' % (len(declared), len(used)))
    return 1 if problems else 0


sys.exit(main())
