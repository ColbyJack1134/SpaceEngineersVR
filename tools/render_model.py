"""Render an installed-model JSON export with textures and labelled anchors."""
import argparse, json, math
from pathlib import Path
import numpy as np
import moderngl
from PIL import Image, ImageDraw, ImageFont

ROOT=Path.cwd()
CONTENT=None
ctx=moderngl.create_standalone_context(backend='egl')
program=ctx.program(vertex_shader='''#version 330
in vec3 pos; in vec3 normal; in vec2 uv; uniform mat4 mvp;
out vec3 n; out vec2 tex;
void main(){gl_Position=mvp*vec4(pos,1); n=normal; tex=uv;}
''', fragment_shader='''#version 330
in vec3 n; in vec2 tex; uniform sampler2D art; uniform vec3 tint;
out vec4 color;
void main(){vec3 base=mix(vec3(.22),texture(art,tex).rgb,.8);
float light=.45+.55*abs(dot(normalize(n),normalize(vec3(.5,.8,.4))));
color=vec4(pow(base*tint*light,vec3(.75)),1);}
''')
def font_at(size):
    for name in ('DejaVuSans.ttf', 'arial.ttf'):
        try: return ImageFont.truetype(name,size)
        except OSError: pass
    return ImageFont.load_default()
font=font_at(22)
small=font_at(17)
textures={}

def texture(part):
    path=part.get('ColorMetalTexture')
    if path not in textures:
        try:
            img=Image.open(CONTENT/path.replace('\\','/')).convert('RGB')
        except Exception as e:
            print('Texture fallback',path,str(e)[:100]); img=Image.new('RGB',(2,2),(160,178,192))
        tex=ctx.texture(img.size,3,img.tobytes()); tex.build_mipmaps()
        textures[path]=tex
    return textures[path]

def unit(x):
    x=np.asarray(x,dtype=float); return x/np.linalg.norm(x)

def view(eye,target):
    f=unit(np.array(target)-eye); right=unit(np.cross(f,[0,1,0])); up=np.cross(right,f)
    v=np.eye(4); v[:3,:3]=np.array([right,up,-f]); v[:3,3]=-v[:3,:3]@eye
    return v

def projection(aspect,fov=70,span=None):
    if span:
        p=np.eye(4); p[0,0]=2/span; p[1,1]=2/(span/aspect); p[2,2]=-2/30; p[2,3]=-1
    else:
        f=1/math.tan(math.radians(fov)/2); near=.01; far=30
        p=np.zeros((4,4)); p[0,0]=f/aspect; p[1,1]=f; p[2,2]=-(far+near)/(far-near)
        p[2,3]=-2*far*near/(far-near); p[3,2]=-1
    return p

def render(name,eye,target,title,out,span=None,points=(),fov=70):
    d=json.loads(Path(name).read_text(encoding='utf-8-sig'))
    verts=np.array(d['Vertices'],dtype='f4')[:,:3]; uv=np.array(d['UV'],dtype='f4')
    size=(1400,1000)
    fb=ctx.simple_framebuffer(size); fb.use(); fb.clear(.035,.055,.075,1)
    ctx.enable(moderngl.DEPTH_TEST)
    mvp=projection(size[0]/size[1],fov,span)@view(np.array(eye),target)
    program['mvp'].write(mvp.T.astype('f4').tobytes())
    for part in d['Parts']:
        if part.get('Technique')=='GLASS': continue
        idx=np.array(part['Indices'],dtype='i4').reshape(-1,3)
        p=verts[idx]; n=np.cross(p[:,1]-p[:,0],p[:,2]-p[:,0]); n/=np.maximum(np.linalg.norm(n,axis=1)[:,None],1e-8)
        data=np.concatenate([p.reshape(-1,3),np.repeat(n,3,axis=0),uv[idx].reshape(-1,2)],axis=1).astype('f4')
        b=ctx.buffer(data.tobytes()); vao=ctx.vertex_array(program,[(b,'3f 3f 2f','pos','normal','uv')])
        texture(part).use(); program['tint'].value=(1,1,1)
        vao.render(); vao.release(); b.release()
    image=Image.frombytes('RGB',size,fb.read(components=3)).transpose(Image.Transpose.FLIP_TOP_BOTTOM)
    draw=ImageDraw.Draw(image)
    draw.rectangle((0,0,1400,82),fill=(12,20,29)); draw.text((24,15),title,font=font,fill='white')
    draw.text((24,49),'Installed game mesh | inspection lighting | glass omitted | no gameplay changes',font=small,fill=(168,193,210))
    for point,label,color,offset in points:
        clip=mvp@np.array([*point,1]); ndc=clip[:3]/clip[3]
        x=(ndc[0]+1)*size[0]/2; y=(1-ndc[1])*size[1]/2
        if clip[3]<=0: continue
        draw.ellipse((x-7,y-7,x+7,y+7),outline=color,width=3)
        tx=x+offset[0]; ty=y+offset[1]; draw.line((x,y,tx,ty),fill=color,width=2)
        draw.text((tx+4,ty-20),label,font=small,fill=color,stroke_width=2,stroke_fill=(0,0,0))
    Path(out).parent.mkdir(parents=True,exist_ok=True)
    image.save(out); fb.release(); print(out)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('model',type=Path)
    parser.add_argument('--content',type=Path,required=True)
    parser.add_argument('--eye',type=float,nargs=3,required=True)
    parser.add_argument('--target',type=float,nargs=3,required=True)
    parser.add_argument('--span',type=float,help='Orthographic width in meters; otherwise use perspective')
    parser.add_argument('--fov',type=float,default=70)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--title',default='Installed model inspection')
    parser.add_argument('--anchors',type=Path,help='JSON list: {point:[x,y,z], label:...}')
    args=parser.parse_args(); CONTENT=args.content
    points=[]
    if args.anchors:
        points=[(a['point'],a['label'],(85,225,255),tuple(a.get('offset',[20,-30]))) for a in json.loads(args.anchors.read_text())]
    render(args.model,args.eye,args.target,args.title,args.output,args.span,points,args.fov)
