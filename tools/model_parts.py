"""List welded, connected mesh components with bounds, in native model meters."""
import argparse
import json
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('model', type=Path)
parser.add_argument('--material', help='Only this material')
args = parser.parse_args()
data = json.loads(args.model.read_text(encoding='utf-8-sig'))
vertices = data['Vertices']
for part in data['Parts']:
    if args.material and part['Material'] != args.material:
        continue
    faces = [part['Indices'][i:i+3] for i in range(0, len(part['Indices']), 3)]
    parents = list(range(len(faces)))
    owner = {}
    def root(i):
        while parents[i] != i:
            parents[i] = parents[parents[i]]
            i = parents[i]
        return i
    for i, face in enumerate(faces):
        for vertex in face:
            key = tuple(round(c, 4) for c in vertices[vertex])
            if key in owner:
                parents[root(i)] = root(owner[key])
            else:
                owner[key] = i
    groups = {}
    for i, face in enumerate(faces):
        groups.setdefault(root(i), []).extend(face)
    for ids in sorted(groups.values(), key=len, reverse=True):
        lo = [min(vertices[i][j] for i in ids) for j in range(3)]
        hi = [max(vertices[i][j] for i in ids) for j in range(3)]
        print(json.dumps(dict(material=part['Material'], triangles=len(ids)//3,
                              center=[(a+b)/2 for a,b in zip(lo,hi)], min=lo, max=hi)))
