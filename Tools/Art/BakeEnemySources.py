"""Offline-only Blender 4.4 bake of the four user-authored aberrant chess sculptures.
Run: blender --background --factory-startup --python Tools/Art/BakeEnemySources.py
Never runs in the game. Originals are read-only; their separate heavy pedestal is replaced.
"""
import bpy, bmesh, os, json, hashlib, math
from mathutils import Vector, Matrix
REPO=os.path.abspath(os.path.join(os.path.dirname(__file__),'../..'))
SOURCE=r'E:/fantasykingdom/Enemy駒/完成/obj'
OUTPUT=os.path.join(REPO,'Assets/_FantasyKingdom/DarkEnemyVisuals/Sources')
FILES=[('Koi','鯉　異形 2完成.obj'),('Ram','羊の異形　下級完成 .obj'),('Crab','蟹の異形　下級 完成.obj'),('Hound','犬の異形　下級.obj')]
os.makedirs(OUTPUT,exist_ok=True)
for tag,filename in FILES:
 bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
 original=os.path.join(SOURCE,filename)
 bpy.ops.wm.obj_import(filepath=original)
 objects=[o for o in bpy.context.selected_objects if o.type=='MESH']
 bpy.context.view_layer.objects.active=objects[0]
 if len(objects)>1:bpy.ops.object.join()
 ob=bpy.context.object
 for vertex in ob.data.vertices:vertex.co=ob.matrix_world@vertex.co
 ob.matrix_world=Matrix.Identity(4)
 original_triangles=sum(len(p.vertices)-2 for p in ob.data.polygons)
 # Pedestals are disconnected components spanning ground to ~43% of their width.
 # Keep every creature component, including separate eyes/teeth/fins.
 bm=bmesh.new();bm.from_mesh(ob.data);bm.verts.ensure_lookup_table();seen=set();remove=[]
 for vertex in bm.verts:
  if vertex in seen:continue
  todo=[vertex];seen.add(vertex);component=[]
  while todo:
   current=todo.pop();component.append(current)
   for edge in current.link_edges:
    other=edge.other_vert(current)
    if other not in seen:seen.add(other);todo.append(other)
  low=Vector(tuple(min(v.co[j] for v in component) for j in range(3)))
  high=Vector(tuple(max(v.co[j] for v in component) for j in range(3)))
  if low.z<0.01 and high.x-low.x>.8 and high.y-low.y>.8:remove.extend(component)
 if not remove:raise RuntimeError('No detached pedestal found: '+filename)
 bmesh.ops.delete(bm,geom=remove,context='VERTS');bm.to_mesh(ob.data);bm.free()
 body_triangles=sum(len(p.vertices)-2 for p in ob.data.polygons)
 # Raise the long koi sculpture into a swimming / rearing pose before normalization.
 rotation=Matrix.Rotation(math.radians(20),4,'Y') if tag=='Koi' else Matrix.Identity(4)
 for v in ob.data.vertices:v.co=rotation@v.co
 bpy.context.view_layer.objects.active=ob
 modifier=ob.modifiers.new('Offline silhouette-preserving reduction','DECIMATE');modifier.ratio=min(1.0,1800/body_triangles);modifier.use_collapse_triangulate=True
 bpy.ops.object.modifier_apply(modifier=modifier.name)
 low=Vector(tuple(min(v.co[j] for v in ob.data.vertices) for j in range(3)))
 high=Vector(tuple(max(v.co[j] for v in ob.data.vertices) for j in range(3)))
 span=high-low;scale=min(.72/max(span.x,span.y),.88/span.z)
 center=Vector(((low.x+high.x)/2,(low.y+high.y)/2,low.z))
 for v in ob.data.vertices:v.co=(v.co-center)*scale
 for p in ob.data.polygons:p.use_smooth=True
 ob.data.update();ob.data.calc_loop_triangles()
 positions=[];normals=[]
 # Blender X,Y,Z -> Unity X,Z,Y is a handedness change. Reverse winding once.
 for triangle in ob.data.loop_triangles:
  for index in (triangle.vertices[0],triangle.vertices[2],triangle.vertices[1]):
   vertex=ob.data.vertices[index];v=vertex.co;n=vertex.normal
   positions.extend(round(x,6) for x in (v.x,v.z-.26,v.y));normals.extend(round(x,6) for x in (n.x,n.z,n.y))
 result={'formatVersion':1,'sourceFile':filename,'sourceSha256':hashlib.sha256(open(original,'rb').read()).hexdigest(),'originalTriangles':original_triangles,'bodyTriangles':body_triangles,'bakedTriangles':len(positions)//9,'positions':positions,'normals':normals}
 with open(os.path.join(OUTPUT,tag+'.json'),'w',encoding='utf8') as stream:json.dump(result,stream,ensure_ascii=False,separators=(',',':'))
 print('BAKED',tag,original_triangles,'->',result['bakedTriangles'])

