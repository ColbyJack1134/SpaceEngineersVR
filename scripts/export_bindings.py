"""Export raw Quest bindings without inferring contextual gameplay behavior."""
import json
from pathlib import Path

root = Path(__file__).resolve().parents[1]
assets = root / 'SpaceEngineersVR/Assets/Controls'
binding = json.loads((assets / 'binding_oculus_touch.json').read_text())
lines = ['# Default Quest / Touch bindings', '',
         'Local raw-input reference from `SpaceEngineersVR/Assets/Controls/binding_oculus_touch.json`. '
         'These are SteamVR declarations, not contextual gameplay instructions. '
         'See [CONTROLS.md](CONTROLS.md) for current behavior and VR options → Controls for active/remapped bindings.', '',
         '| Action set | Physical input | Input type | Action identifier |', '| --- | --- | --- | --- |']
for context, bindings in binding['bindings'].items():
    for source in bindings.get('sources', []):
        path = source['path'].replace('/user/hand/', '').replace('/input/', ' ')
        for input_name, value in source.get('inputs', {}).items():
            action = value.get('output')
            if action:
                lines.append(f'| {context.rsplit("/", 1)[-1]} | {path} | {input_name} | `{action.rsplit("/", 1)[-1]}` |')
lines += ['', '## Context and maintenance', '',
          'Pressure actions support near interaction and capture. Skeleton/pose/haptic declarations are not buttons and are omitted.', '',
          'Wheel pages are contextual; the full command catalog is not the wheel layout. '
          'For example, the menu left-grip WheelPreviousPage declaration is not the active wheel paging instruction: '
          'left/right triggers page backward/forward, while native menus use grip/trigger modifiers.', '',
          'Run `python3 scripts/export_bindings.py` after changing the binding JSON. '
          'Update CONTROLS.md separately when Controls, GameActions, ToolbarWheel, PlacementControls, HelmetHud or movement routing changes. '
          'Sprint/crouch design remains tracked in [ROADMAP.md](ROADMAP.md).', '']
(root / 'docs').mkdir(exist_ok=True)
(root / 'docs/DEFAULT-BINDINGS.md').write_text('\n'.join(lines))
print('Updated docs/DEFAULT-BINDINGS.md')
