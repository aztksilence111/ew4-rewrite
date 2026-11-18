"""Read original popup layout and ARM64 references; does not execute Android UI."""
import hashlib
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

sys.path.insert(0, str(Path.home() / '.codex/tools/ew4-inspect-deps'))
from capstone import Cs, CS_ARCH_ARM64, CS_MODE_ARM
from elftools.elf.elffile import ELFFile

ROOT = Path(__file__).resolve().parents[2]


def main():
    library = ROOT / 'apk_work/original/lib/arm64-v8a/lib64ew4_Sagiri.so'
    data = library.read_bytes()
    layout = ET.parse(ROOT / 'apk_work/original/assets/layout.xml').getroot()
    popup = next(n for n in layout.iter() if n.get('id') == 'form_getgeneraltips')
    manifest = json.loads((ROOT / 'rewrite/data/client/manifest.json').read_text(encoding='utf-8-sig'))
    checks = 0

    def check(condition):
        nonlocal checks
        assert condition
        checks += 1

    check(popup.attrib == manifest['layouts']['form_getgeneraltips']['attributes'])
    check((popup.get('w'), popup.get('h'), popup.get('sound'), popup.get('btn_ok')) ==
          ('152', '184', 'sfx_lvup2.wav', 'true'))
    check(any(n.get('id') == 'tcmder' for n in popup))
    check(any(n.get('name') == 'pattern_gotgeneral.png' for n in popup))
    strings = {}
    for address in [0x199248, 0x1933d0, 0x1933b0, 0x192f68]:
        strings[hex(address)] = data[address:address+80].split(b'\0')[0].decode('ascii')
    check(list(strings.values()) == ['form_getgeneraltips', 'effect_getgeneral.xml', 'effect_getgeneral_hd.xml', 'tcmder'])
    with library.open('rb') as stream:
        section = ELFFile(stream).get_section_by_name('.text')
        base, code = section['sh_addr'], section.data()
    decoder = Cs(CS_ARCH_ARM64, CS_MODE_ARM)
    instructions = {address: (mnemonic, operands) for address, _, mnemonic, operands in
                    decoder.disasm_lite(code[0xa1e70-base:0xa2074-base], 0xa1e70)}
    check(instructions[0xa1e88] == ('add', 'x1, x1, #0x248'))
    check(instructions[0xa1f0c] == ('add', 'x2, x2, #0x3d0'))
    check(instructions[0xa1f4c] == ('add', 'x2, x2, #0x3b0'))
    check(instructions[0xa2054] == ('add', 'x1, x1, #0xf68'))
    print(json.dumps({'checks': checks, 'errors': 0, 'librarySha256': hashlib.sha256(data).hexdigest(),
                      'strings': strings, 'scope': 'static original XML and ARM64 references'}, ensure_ascii=False))


if __name__ == '__main__':
    main()
