"""Regenerate the readable default binding reference from the shipped Quest JSON."""
import json
import re
from pathlib import Path

root = Path(__file__).resolve().parents[1]
assets = root / 'SpaceEngineersVR/Assets/Controls'
binding = json.loads((assets / 'binding_oculus_touch.json').read_text())
manifest = json.loads((assets / 'actions.json').read_text())
english = next((x for x in manifest.get('localization', []) if x.get('language_tag', '').lower() == 'en_us'), {})
labels = {k.lower(): v for k, v in english.items()}
lines = ['# Quest controller bindings', '',
         'Generated from the shipped binding JSON and command catalog.', '',
         '| Context | Control | Input | Action |', '|---|---|---|---|']
for context, bindings in binding['bindings'].items():
    for source in bindings.get('sources', []):
        path = source['path'].replace('/user/hand/', '').replace('/input/', ' ')
        for input_name, value in source.get('inputs', {}).items():
            action = value.get('output')
            if action:
                label = labels.get(action.lower(), action.rsplit('/', 1)[-1])
                lines.append(f'| {context.rsplit("/", 1)[-1]} | {path} | {input_name} | {label} |')
lines += ['', '## Wheel actions', '',
          '| Wheel | Page | Sector | Action |', '|---|---|---|---|']
source = (root / 'SpaceEngineersVR/Player/GameActions.cs').read_text()
shared = dict(re.findall(r'public static readonly ActionChoice (\w+) = new ActionChoice\("([^"\n]+)"', source))
for array, title in [('Quick', 'Actions'), ('Building', 'Building')]:
    body = re.search(r'ActionChoice\[\] ' + array + r' = \{(.*?)\n        \};', source, re.S).group(1)
    for index, line in enumerate(body.strip().splitlines()):
        match = re.search(r'(?:new ActionChoice|Native)\("([^"\n]+)"', line)
        label = match.group(1) if match else shared[line.strip().rstrip(',')]
        lines.append(f'| {title} | {index // 9 + 1} | {index % 9 + 1} | {label} |')
lines.append('')
(root / 'docs').mkdir(exist_ok=True)
(root / 'docs/DEFAULT-BINDINGS.md').write_text('\n'.join(lines))
print('Updated docs/DEFAULT-BINDINGS.md')
