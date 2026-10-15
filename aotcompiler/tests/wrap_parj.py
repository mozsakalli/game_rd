import re, sys, io, pathlib
ANGLE = True

# Par.J(a, b, c) -> Par.J(Q(() => a), Q(() => b), ...) ; zaten sarili (P( PM( V( VM( Par.T( Par.K( OV( Q() olanlar dokunulmaz.
WRAPPED = re.compile(r'^(P|PM|V|VM|OV|Q|Par\.T|Par\.K|Par\.R6)\s*\(')

def split_args(s):
    args, depth, cur, i, n, angle = [], 0, [], 0, len(s), 0
    in_str = None  # '"' or "'" or '@"' / '$"'
    while i < n:
        c = s[i]
        if in_str:
            cur.append(c)
            if in_str == 'verb':
                if c == '"':
                    if i + 1 < n and s[i+1] == '"': cur.append(s[i+1]); i += 2; continue
                    in_str = None
            elif c == '\\':
                if i + 1 < n: cur.append(s[i+1]); i += 2; continue
            elif c == in_str:
                in_str = None
            i += 1; continue
        if c == '@' and i + 1 < n and s[i+1] == '"': in_str = 'verb'; cur.append(c); cur.append('"'); i += 2; continue
        if c == '$' and i + 1 < n and s[i+1] == '"': in_str = '"'; cur.append(c); cur.append('"'); i += 2; continue
        if c == '"': in_str = '"'; cur.append(c); i += 1; continue
        if c == "'": in_str = "'"; cur.append(c); i += 1; continue
        if c in '([{': depth += 1
        elif c in ')]}': depth -= 1
        elif ANGLE and c == '<' and i > 0 and (s[i-1].isalnum() or s[i-1] in '_.>') and i + 1 < n and (s[i+1].isalpha() or s[i+1] in '(_'): depth += 1; angle += 1
        elif ANGLE and c == '>' and angle > 0 and i > 0 and (s[i-1].isalnum() or s[i-1] in '_>]?') and i + 1 < n and s[i+1] in '(>[)., ': depth -= 1; angle -= 1
        if c == ',' and depth == 0:
            args.append(''.join(cur)); cur = []; i += 1; continue
        cur.append(c); i += 1
    args.append(''.join(cur))
    return args

def find_close(s, start):
    depth, i, n, in_str = 0, start, len(s), None
    while i < n:
        c = s[i]
        if in_str:
            if in_str == 'verb':
                if c == '"':
                    if i + 1 < n and s[i+1] == '"': i += 2; continue
                    in_str = None
            elif c == '\\': i += 2; continue
            elif c == in_str: in_str = None
            i += 1; continue
        if c == '@' and i + 1 < n and s[i+1] == '"': in_str = 'verb'; i += 2; continue
        if c == '$' and i + 1 < n and s[i+1] == '"': in_str = '"'; i += 2; continue
        if c == '"': in_str = '"'; i += 1; continue
        if c == "'": in_str = "'"; i += 1; continue
        if c == '(': depth += 1
        elif c == ')':
            depth -= 1
            if depth == 0: return i
        i += 1
    return -1

def unwrap(text):
    out, pos = [], 0
    while True:
        j = text.find('Par.J(', pos)
        if j < 0: out.append(text[pos:]); break
        open_i = j + len('Par.J'); close_i = find_close(text, open_i)
        inner = text[open_i+1:close_i]
        args = []
        for a in split_args(inner):
            st = a.strip(); lead = a[:len(a)-len(a.lstrip())]
            if st.startswith('Q(() => ') and st.endswith(')'): args.append(lead + st[len('Q(() => '):-1])
            else: args.append(a)
        out.append(text[pos:open_i+1]); out.append(','.join(args)); out.append(')'); pos = close_i + 1
    return ''.join(out)

def transform(text):
    out, pos, count = [], 0, 0
    while True:
        j = text.find('Par.J(', pos)
        if j < 0: out.append(text[pos:]); break
        open_i = j + len('Par.J')
        close_i = find_close(text, open_i)
        if close_i < 0: out.append(text[pos:]); break
        inner = text[open_i+1:close_i]
        args = split_args(inner)
        new_args = []
        for a in args:
            st = a.strip()
            if st == '' : continue
            if WRAPPED.match(st) or 'out ' in st or 'stackalloc' in st or 'ref ' in st:
                new_args.append(a)
            else:
                lead = a[:len(a)-len(a.lstrip())]
                new_args.append(f'{lead}Q(() => {st})'); count += 1
        out.append(text[pos:open_i+1]); out.append(','.join(new_args)); out.append(')')
        pos = close_i + 1
    return ''.join(out), count

mode = sys.argv[1]
ANGLE = mode != 'unwrap-noangle'
for path in sys.argv[2:]:
    p = pathlib.Path(path)
    t = p.read_text(encoding='utf-8')
    if mode.startswith('unwrap'):
        p.write_text(unwrap(t), encoding='utf-8'); print('unwrapped', p.name); continue
    nt, c = transform(t)
    # Q yardimcisi: dosyada yoksa ilk 'static int I(' satirinin ustune ekle; yoksa class govdesine
    if 'static string Q(Func<object> f)' not in nt:
        anchor = re.search(r'\n(\s*)static int I\(int v\)', nt)
        if anchor:
            ind = anchor.group(1)
            nt = nt[:anchor.start()] + f'\n{ind}static string Q(Func<object> f) {{ try {{ var r = f(); return r == null ? "null" : r.ToString(); }} catch (Exception e) {{ return Par.Ex(e); }} }}' + nt[anchor.start():]
        else:
            print('ANCHOR YOK', path)
    p.write_text(nt, encoding='utf-8')
    print(f'{p.name}: {c} arg sarildi')
