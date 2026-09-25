from pathlib import Path
from collections import Counter
import argparse
import json
import re
import xml.etree.ElementTree as ET

MOD = Path(__file__).resolve().parents[2]
SOURCE = MOD / '1.6/Source/SimManagementLib'
LANGUAGES = MOD / '1.6/Languages'
KEY_CALL = re.compile(r'SimTranslation\.T(?:OrFallback)?\(\s*"(RSMF\.[^"\r\n]+)"\s*[,)]')
FALLBACK = re.compile(r'SimTranslation\.TOrFallback\(\s*"RSMF\.[^"]+",\s*"(?:\\.|[^"\\])*"\)')
PLACEHOLDER = re.compile(r'(?<!\{)\{([A-Za-z_][A-Za-z0-9_]*)(?:[^{}]*)\}(?!\})')
HAN_LITERAL = re.compile(r'"(?:\\.|[^"\\\r\n])*[\u3400-\u9fff](?:\\.|[^"\\\r\n])*"')

#读取各语言键表，职责是校验 XML、重复键和空条目并记录所属文件。
def read_language(language):
    values, owners, errors = {}, {}, []
    for path in sorted((LANGUAGES / language / 'Keyed').glob('*.xml')):
        try:
            root = ET.fromstring(path.read_text(encoding='utf-8-sig'))
        except (ET.ParseError, UnicodeError) as error:
            errors.append(f'{path.name}: {error}')
            continue
        if root.tag != 'LanguageData':
            errors.append(f'{path.name}: 根节点必须为 LanguageData')
        for entry in root:
            if entry.tag in values:
                errors.append(f'{entry.tag}: 重复键，位于 {owners[entry.tag]} 和 {path.name}')
            values[entry.tag] = entry.text or ''
            owners[entry.tag] = path.name
            if not values[entry.tag].strip():
                errors.append(f'{entry.tag}: 空译文')
    return values, errors

#分类源码中的中文候选，职责是区分已有后备翻译、界面文字、日志和模型上下文。
def classify(file, line):
    if 'TOrFallback(' in line:
        return '已有翻译键的后备文本'
    if file.startswith('Debug/') or 'Log.' in line or 'SimDebugLogger.' in line:
        return '开发者诊断或日志'
    if 'CustomerReview' in file or 'ComboAiNameUtility' in file or 'Prompt' in file:
        return '模型提示或评价内容'
    if any(word in line for word in ['IndexOf(', 'Contains(', '.Replace(', 'ContainsAny']):
        return '文本解析或数据匹配'
    if file.startswith('SimDialog/') or file == 'SimManagementLibMod.cs':
        return '界面文件候选，需检查数据用途'
    return '内部状态或间接显示候选，需人工判断'

#扫描源码和 Def 中的明确键引用，职责是只把完整键作为缺失检查对象。
def scan_source():
    references, candidates = {}, []
    files, fallback_count = 0, 0
    for path in sorted(SOURCE.rglob('*.cs')):
        if any(part in {'obj', 'bin'} for part in path.relative_to(SOURCE).parts):
            continue
        files += 1
        file = path.relative_to(SOURCE).as_posix()
        source = path.read_text(encoding='utf-8-sig')
        fallback_count += len(FALLBACK.findall(source))
        for match in KEY_CALL.finditer(source):
            references.setdefault(match[1], []).append(file + ':' + str(source.count('\n', 0, match.start()) + 1))
        for number, line in enumerate(source.splitlines(), 1):
            if line.lstrip().startswith(('//', '*')) or not HAN_LITERAL.search(line):
                continue
            candidates.append({'file': file, 'line': number, 'category': classify(file, line), 'text': line.strip()})
    for path in sorted((MOD / '1.6/Defs').rglob('*.xml')):
        for element in ET.parse(path).getroot().iter():
            if element.tag.lower().endswith('key') and (element.text or '').startswith('RSMF.'):
                references.setdefault(element.text.strip(), []).append(path.relative_to(MOD).as_posix())
    return references, candidates, files, fallback_count

#执行静态语言审计，职责是报告缺失键、参数不一致和仍需人工判断的中文候选。
def main():
    parser = argparse.ArgumentParser(description='检查框架语言键与源码引用；不启动游戏。')
    parser.add_argument('--report', type=Path, help='可选的 UTF-8 JSON 报告路径')
    args = parser.parse_args()
    languages, errors = {}, []
    for language in ['ChineseSimplified', 'English']:
        languages[language], found = read_language(language)
        errors.extend(language + ': ' + error for error in found)
    references, candidates, file_count, fallback_count = scan_source()
    for language, values in languages.items():
        errors.extend(language + ': 缺少引用键 ' + key for key in sorted(references.keys() - values.keys()))
    zh, en = languages['ChineseSimplified'], languages['English']
    errors.extend('语言键集合不一致: ' + key for key in sorted(zh.keys() ^ en.keys()))
    for key in sorted(zh.keys() & en.keys()):
        if set(PLACEHOLDER.findall(zh[key])) != set(PLACEHOLDER.findall(en[key])):
            errors.append('命名参数不一致: ' + key)
    report = {
        'source_files': file_count,
        'language_key_counts': {name: len(values) for name, values in languages.items()},
        'referenced_static_keys': len(references),
        'fallback_calls': fallback_count,
        'candidate_line_count': len(candidates),
        'candidate_categories': dict(Counter(row['category'] for row in candidates)),
        'errors': errors,
        'candidates': candidates,
        'limitations': '中文字符串按行启发式扫描，候选不等于未翻译 UI；英文标识、动态拼接键、模型内容及间接数据流需要人工审查。'
    }
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({key: value for key, value in report.items() if key != 'candidates'}, ensure_ascii=False, indent=2))
    raise SystemExit(1 if errors else 0)

if __name__ == '__main__':
    main()
