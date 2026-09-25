from collections import Counter
from pathlib import Path
import argparse
import json
import re
import xml.etree.ElementTree as ET

MOD = Path(__file__).resolve().parents[2]
SOURCE = MOD / '1.6/Source/RimSimRestaurantExtension'
LANGUAGES = MOD / '1.6/Languages'
HAN = re.compile(r'[\u3400-\u9fff]')
LITERAL = re.compile(r'"(?:\\.|[^"\\\r\n])*"')
PLACEHOLDER = re.compile(r'(?<!\{)\{([A-Za-z_][A-Za-z0-9_]*)\}(?!\})')


#读取语言文件，职责是验证编码、空值及同类翻译键的唯一性。
def read_language(language, errors):
    values = {}
    for path in sorted((LANGUAGES / language).rglob('*.xml')):
        relative = path.relative_to(LANGUAGES / language)
        group = '/'.join(relative.parts[:-1])
        root = ET.fromstring(path.read_text(encoding='utf-8-sig'))
        if root.tag != 'LanguageData':
            errors.append(f'{relative}: 根节点不是 LanguageData')
        for entry in root:
            key = (group, entry.tag)
            if key in values or not (entry.text or '').strip():
                errors.append(f'{language}/{relative}: 重复键或空译文 {entry.tag}')
            values[key] = entry.text or ''
    return values


#提取完整翻译调用，职责是跳过字符串内部括号并保留嵌套表达式的命名参数。
def translation_calls(source):
    for match in re.finditer(r'SimTranslation\.T\("(RSR\.[^"]+)"', source):
        start = source.index('(', match.start())
        depth, quoted, escaped = 0, False, False
        for index in range(start, len(source)):
            char = source[index]
            if quoted:
                if escaped:
                    escaped = False
                elif char == '\\':
                    escaped = True
                elif char == '"':
                    quoted = False
            elif char == '"':
                quoted = True
            elif char == '(':
                depth += 1
            elif char == ')':
                depth -= 1
                if depth == 0:
                    yield match[1], source[start:index + 1]
                    break


#归类保留的中文代码文本，职责是区别开发工具、诊断信息与玩家界面遗漏。
def category(file, line):
    if file.startswith('Debug/'):
        return '开发者生成工具'
    if any(token in line for token in ['Log.', 'RestaurantFlowLog.', 'Exception(', 'yield return defName']):
        return '日志、异常或配置诊断'
    if '/Rendering/' in file or file == 'Tool/RestaurantFlowLog.cs':
        return '渲染内部名称或日志'
    if file.startswith('Conveyor/Stocking/') and 'Fail(' in line:
        return '仅写日志的补餐失败原因'
    return '待人工核对'


#校验运行时键与 Def 字段并报告中文候选，职责是为后续译者提供可重复执行的静态检查。
def main():
    parser = argparse.ArgumentParser(description='检查餐厅翻译键、参数和 DefInjected；不启动游戏。')
    parser.add_argument('--report', type=Path)
    args = parser.parse_args()
    errors, candidates, references = [], [], set()
    zh = read_language('ChineseSimplified', errors)
    en = read_language('English', errors)
    if zh.keys() != en.keys():
        errors.append('中英文键集合不同：' + str(sorted(zh.keys() ^ en.keys())))
    for key in zh.keys() & en.keys():
        if Counter(PLACEHOLDER.findall(zh[key])) != Counter(PLACEHOLDER.findall(en[key])):
            errors.append(f'参数不一致：{key}')
    files = 0
    for path in sorted(SOURCE.rglob('*.cs')):
        if {'obj', 'bin'} & set(path.parts):
            continue
        files += 1
        file = path.relative_to(SOURCE).as_posix()
        source = path.read_text(encoding='utf-8-sig')
        for key, call in translation_calls(source):
            references.add(key)
            for language, values in [('ChineseSimplified', zh), ('English', en)]:
                if ('Keyed', key) not in values:
                    errors.append(f'{file}: {language} 缺少 {key}')
            if ('Keyed', key) in zh:
                expected = set(PLACEHOLDER.findall(zh['Keyed', key]))
                actual = set(re.findall(r'\.Named\("([^"]+)"\)', call))
                if expected != actual:
                    errors.append(f'{file}: {key} 调用参数不匹配 {expected} / {actual}')
        for number, line in enumerate(source.splitlines(), 1):
            if line.lstrip().startswith('//') or not any(HAN.search(s) for s in LITERAL.findall(line)):
                continue
            kind = category(file, line)
            candidates.append(dict(file=file, line=number, category=kind, text=line.strip()))
            if kind == '待人工核对':
                errors.append(f'{file}:{number}: 未分类的中文文本')
    targets = {}
    for path in (MOD / '1.6/Defs').rglob('*.xml'):
        for definition in ET.parse(path).getroot():
            name = definition.findtext('defName')
            if not name:
                continue
            group = 'DefInjected/' + definition.tag.rsplit('.', 1)[-1]
            for field in ['label', 'description', 'reportString', 'verb', 'gerund']:
                value = definition.findtext(field)
                if value:
                    targets[group, name + '.' + field] = value
    injected = {key for key in zh if key[0].startswith('DefInjected/')}
    for key in targets.keys() - injected:
        errors.append(f'缺少 Def 翻译：{key}')
    for key in injected - targets.keys():
        errors.append(f'Def 翻译目标不存在：{key}')
    report = dict(sourceFiles=files, keyed=sum(k[0] == 'Keyed' for k in zh),
                  referencedKeys=len(references), defFields=len(injected),
                  remainingCategories=dict(Counter(c['category'] for c in candidates)),
                  errors=errors, candidates=candidates)
    if args.report:
        args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({k: v for k, v in report.items() if k != 'candidates'}, ensure_ascii=False, indent=2))
    raise SystemExit(bool(errors))


if __name__ == '__main__':
    main()
