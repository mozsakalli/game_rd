#!/usr/bin/env python3
"""generated.c fonksiyon-bazli normalize diff (docs/engine-notes.md CANON LOWERING P0).

Kullanim: python tools/gen_diff.py <taban.c> <yeni.c> [--canon-only|--exact-only] [--show N]
Her C fonksiyon govdesi (`^... name(...) {` ... `^}`) icin normalize edilir:
  - gecici ad numaralari (__s12, __t3, __o7, __a2, __p9, __i4, __bx, __dims5, L47 etiketleri) sirali yeniden adlandirilir
  - DIGITOYENGINE_STEP/SETLINE/debug satirlari atilir, bosluk sikistirilir
Sonra ad bazinda eslestirilip farkli olanlar listelenir. Exact fonksiyonlar (adinda ___Canon_ olmayan) BIREBIR olmali.
"""
import re, sys

FN_RX = re.compile(r'^(?:static inline |static |extern |DIGITOYENGINE_\w+ )*[A-Za-z_][\w \*]*?\b([A-Za-z_]\w*)\s*\(([^;{]*)\)\s*\{\s*$')
TMP_RX = re.compile(r'\b(__(?:s|t|o|a|p|i|dims|nb|ub|bx|c|r|v)\d*)\b|\b(L\d+)\b')
SKIP_RX = re.compile(r'DIGITOYENGINE_(STEP|SETLINE)\(|__dbg|DBG_LOCALS|^\s*#(ifdef|endif)')

def functions(path):
    fns = {}
    cur = None; body = []
    with open(path, encoding='utf-8', errors='replace') as f:
        for line in f:
            if cur is None:
                m = FN_RX.match(line.rstrip('\n'))
                if m:
                    cur = m.group(1); body = [line]
            else:
                body.append(line)
                if line.startswith('}'):
                    fns[cur] = normalize(body); cur = None
    return fns

def normalize(lines):
    out = []; names = {}
    def ren(m):
        tok = m.group(0)
        if tok not in names: names[tok] = '%%%d' % len(names)
        return names[tok]
    for l in lines:
        if SKIP_RX.search(l): continue
        l = TMP_RX.sub(ren, l)
        l = re.sub(r'\s+', ' ', l).strip()
        if l: out.append(l)
    return '\n'.join(out)

def main():
    a, b = sys.argv[1], sys.argv[2]
    mode = next((x for x in sys.argv[3:] if x.startswith('--canon') or x.startswith('--exact')), None)
    show = int(sys.argv[sys.argv.index('--show') + 1]) if '--show' in sys.argv else 3
    fa, fb = functions(a), functions(b)
    def pick(n):
        if mode == '--canon-only': return '___Canon_' in n
        if mode == '--exact-only': return '___Canon_' not in n
        return True
    only_a = [n for n in fa if n not in fb and pick(n)]
    only_b = [n for n in fb if n not in fa and pick(n)]
    diff = [n for n in fa if n in fb and pick(n) and fa[n] != fb[n]]
    same = sum(1 for n in fa if n in fb and pick(n) and fa[n] == fb[n])
    print(f'ayni: {same}  farkli: {len(diff)}  yalniz-taban: {len(only_a)}  yalniz-yeni: {len(only_b)}')
    for n in only_a[:show]: print('  yalniz taban:', n)
    for n in only_b[:show]: print('  yalniz yeni :', n)
    for n in diff[:show]:
        print(f'--- {n}')
        la, lb = fa[n].split('\n'), fb[n].split('\n')
        for i in range(max(len(la), len(lb))):
            x = la[i] if i < len(la) else '<yok>'; y = lb[i] if i < len(lb) else '<yok>'
            if x != y:
                print('  -', x[:200]); print('  +', y[:200]); break
    sys.exit(1 if diff or only_a or only_b else 0)

if __name__ == '__main__':
    main()
