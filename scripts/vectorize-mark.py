"""Trace the accepted transparent mark into a compact, scalable even-odd path (Pillow).

Pixel-boundary tracing uses only the alpha silhouette. RDP limits contour error
to one source pixel, avoiding a new design or low-resolution raster enlargement.
"""
from pathlib import Path
from PIL import Image
import math

root = Path(__file__).resolve().parents[1]
image = Image.open(root / "assets/brand/mark.png").convert("RGBA")
alpha = image.getchannel("A")
w,h = alpha.size
pixels = alpha.load()
edges = {}
for y in range(h):
    for x in range(w):
        if pixels[x,y] < 128: continue
        if y==0 or pixels[x,y-1]<128: edges[(x,y)]=(x+1,y)
        if x==w-1 or pixels[x+1,y]<128: edges[(x+1,y)]=(x+1,y+1)
        if y==h-1 or pixels[x,y+1]<128: edges[(x+1,y+1)]=(x,y+1)
        if x==0 or pixels[x-1,y]<128: edges[(x,y+1)]=(x,y)

def simplify(points,epsilon):
    if len(points)<3:return points
    a,b=points[0],points[-1]
    dx,dy=b[0]-a[0],b[1]-a[1]
    norm=math.hypot(dx,dy)
    distances=[abs(dy*(p[0]-a[0])-dx*(p[1]-a[1]))/norm if norm else math.dist(a,p) for p in points]
    index=max(range(len(points)),key=distances.__getitem__)
    if distances[index]<=epsilon:return [a,b]
    return simplify(points[:index+1],epsilon)[:-1]+simplify(points[index:],epsilon)

paths=[]
contours=[]
while edges:
    start=next(iter(edges));p=start;points=[]
    while True:
        points.append(p);p=edges.pop(p)
        if p==start:break
    if len(points)<20:continue
    midpoint=len(points)//2
    points=simplify(points[:midpoint+1],1)+simplify(points[midpoint:]+[points[0]],1)[1:]
    coords=[(x*1000/w,y*1000/h) for x,y in points]
    contours.append(coords)
    paths.append('M '+' L '.join(f'{x:.3f},{y:.3f}' for x,y in coords)+' Z')
data=' '.join(paths)
(root/'assets/brand/mark.path').write_text(data+'\n',encoding='utf-8')
(root/'assets/brand/mark.svg').write_text(f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1000 1000"><path fill="currentColor" fill-rule="evenodd" d="{data}"/></svg>\n',encoding='utf-8')
def area(contour):
    return abs(sum(a[0]*b[1]-b[0]*a[1] for a,b in zip(contour,contour[1:]+contour[:1])))
body=paths[max(range(len(contours)),key=lambda index:area(contours[index]))]
(root/'assets/brand/mark-body.path').write_text(body+'\n',encoding='utf-8')
print({'contours':len(paths),'pathBytes':len(data),'viewBox':[0,0,1000,1000]})
