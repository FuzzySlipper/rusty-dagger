"""Flame frames: keep frame 0's converted body, take only the flame region from frame k's conversion."""
import json,re,sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageFilter
L=str(Path(__file__).resolve().parents[2]/'gen-raw/items')+'/'
spec,study,cut=sys.argv[1:4]
for it in json.load(open(spec))['items']:
    inp=json.load(open(f"{study}/inputs/{it['id']}/input.json"))
    a,r,f=re.search(r'texture-(\d+)-record-(\d+)-frame-(\d+)',it['image']).groups()
    src0=np.array(Image.open(inp['source'].replace(f'frame-{f}-source','frame-000-source')).convert('RGBA'))
    srck=np.array(Image.open(inp['source']).convert('RGBA'))
    diff=(src0!=srck).any(2)
    s=inp['integer_scale']; px,py=inp['placement']; h,w=diff.shape
    m=np.zeros((1024,1024),np.uint8); m[py:py+h*s,px:px+w*s]=np.kron(diff,np.ones((s,s),np.uint8))*255
    mm=Image.fromarray(m).filter(ImageFilter.MaxFilter(2*int(s*1.5)//2*2+1)).filter(ImageFilter.GaussianBlur(s*0.4))
    mk=np.array(mm,np.float32)[...,None]/255
    base=np.array(Image.open(f'{L}texture-{a}-record-{r}-frame-000.png').convert('RGBA'),np.float32)/255
    new=np.array(Image.open(f"{cut}/{it['id']}.png").convert('RGBA'),np.float32)/255
    pb=base[...,:3]*base[...,3:]; pn=new[...,:3]*new[...,3:]
    pa=pb*(1-mk)+pn*mk; al=base[...,3:]*(1-mk)+new[...,3:]*mk
    rgb=np.where(al>1e-3,pa/np.maximum(al,1e-3),0)
    out=np.concatenate([rgb,al],2); Image.fromarray((np.clip(out,0,1)*255+.5).astype(np.uint8),'RGBA').save(f'{L}texture-{a}-record-{r}-frame-{f}.png')
    print(it['id'],'->',f)
