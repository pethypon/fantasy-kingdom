#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

public static partial class TerrainEnvironmentProductionInstaller
{
    const float Ground = -.48f;
    static VisualMesh MakeVisual(TerrainEnvironmentKind kind)
    {
        var mesh = new VisualMesh();
        switch (kind)
        {
            case TerrainEnvironmentKind.Forest:
                mesh.Patch(Vector3.zero, .47f, Ground, .016f, 0, 12, .13f);
                Tree(mesh, new Vector3(-.27f, Ground, .25f), .76f, .175f);
                Tree(mesh, new Vector3(.27f, Ground, .26f), .61f, .16f);
                Tree(mesh, new Vector3(-.30f, Ground, -.27f), .52f, .14f);
                Bush(mesh, new Vector3(.31f, Ground, -.29f), .16f, .13f, 2);
                mesh.Rock(new Vector3(.13f, Ground, -.37f), new Vector3(.10f, .07f, .065f), 7, .6f);
                break;
            case TerrainEnvironmentKind.Bush:
                mesh.Patch(Vector3.zero, .47f, Ground, .012f, 1, 12, .12f);
                Bush(mesh, new Vector3(-.29f, Ground, -.26f), .18f, .19f, 2);
                Bush(mesh, new Vector3(.27f, Ground, -.31f), .18f, .15f, 3);
                Bush(mesh, new Vector3(.27f, Ground, .28f), .18f, .21f, 2);
                Bush(mesh, new Vector3(-.27f, Ground, .29f), .17f, .16f, 3);
                mesh.Leaf(new Vector3(.04f, Ground, -.40f), .11f, .13f, 3, -20);
                mesh.Leaf(new Vector3(-.38f, Ground, .02f), .11f, .11f, 2, 30);
                break;
            case TerrainEnvironmentKind.Rock:
                mesh.Patch(Vector3.zero, .46f, Ground, .014f, 6, 12, .17f);
                // Walkable cover: a clear center with boulders around the tile rim.
                mesh.Rock(new Vector3(-.29f, Ground, .24f), new Vector3(.19f, .36f, .19f), 7, .4f);
                mesh.Rock(new Vector3(.28f, Ground, .26f), new Vector3(.17f, .24f, .18f), 6, 1.4f);
                mesh.Rock(new Vector3(.32f, Ground, -.26f), new Vector3(.13f, .17f, .15f), 8, 2.1f);
                mesh.Rock(new Vector3(-.32f, Ground, -.30f), new Vector3(.11f, .09f, .13f), 7, 3.6f);
                break;
            case TerrainEnvironmentKind.PoisonSwamp:
                mesh.Patch(Vector3.zero, .48f, Ground, .019f, 9, 14, .16f);
                mesh.Patch(new Vector3(.01f, 0, .01f), .395f, Ground + .021f, .006f, 10, 14, .13f);
                mesh.Ring(new Vector3(-.17f, Ground + .034f, -.10f), .075f, .047f, .005f, 11, 10);
                mesh.Ring(new Vector3(.21f, Ground + .033f, .13f), .055f, .033f, .005f, 11, 10);
                mesh.Rock(new Vector3(-.36f, Ground, .21f), new Vector3(.09f, .095f, .075f), 6, 1.1f);
                mesh.Rock(new Vector3(.32f, Ground, -.29f), new Vector3(.12f, .07f, .09f), 6, 2.4f);
                mesh.Leaf(new Vector3(-.27f, Ground, -.32f), .10f, .17f, 10, -15);
                break;
            case TerrainEnvironmentKind.Mud:
                mesh.Patch(Vector3.zero, .48f, Ground, .019f, 12, 14, .11f);
                mesh.Patch(new Vector3(-.08f, 0, .01f), .30f, Ground + .021f, .005f, 13, 12, .17f);
                mesh.Patch(new Vector3(.22f, 0, -.20f), .11f, Ground + .027f, .004f, 12, 9, .08f);
                mesh.Rock(new Vector3(.28f, Ground, .28f), new Vector3(.11f, .075f, .09f), 7, .4f);
                mesh.Rock(new Vector3(-.29f, Ground, -.29f), new Vector3(.07f, .05f, .09f), 7, 1.8f);
                break;
            case TerrainEnvironmentKind.Road:
                mesh.Patch(Vector3.zero, .47f, Ground, .012f, 13, 12, .13f);
                for (int row = -1; row <= 1; row++)
                    for (int column = -1; column <= 1; column++)
                    {
                        float x = column * .27f + (row == 0 ? .025f : -.015f);
                        mesh.Rock(new Vector3(x, Ground + .008f, row * .27f), new Vector3(.13f, .032f, .12f), 14, column * .7f + row);
                    }
                break;
            case TerrainEnvironmentKind.HealingSpring:
                mesh.Patch(Vector3.zero, .47f, Ground, .025f, 6, 14, .06f);
                mesh.Ring(new Vector3(0, Ground + .025f, 0), .44f, .34f, .06f, 8, 14);
                mesh.Patch(Vector3.zero, .338f, Ground + .038f, .005f, 15, 14, 0);
                mesh.Ring(new Vector3(.015f, Ground + .047f, -.015f), .24f, .231f, .004f, 16, 14);
                mesh.Ring(new Vector3(.025f, Ground + .050f, -.018f), .13f, .122f, .004f, 17, 14);
                mesh.Crystal(new Vector3(-.29f, Ground + .08f, .27f), .058f, .23f, 17);
                mesh.Crystal(new Vector3(-.36f, Ground + .07f, .14f), .035f, .14f, 16);
                break;
            case TerrainEnvironmentKind.Geyser:
                mesh.Patch(Vector3.zero, .47f, Ground, .018f, 6, 12, .12f);
                mesh.Ring(new Vector3(0, Ground + .018f, 0), .28f, .13f, .12f, 7, 10);
                mesh.Patch(Vector3.zero, .13f, Ground + .045f, .004f, 18, 10, 0);
                mesh.Ring(new Vector3(0, Ground + .051f, 0), .082f, .07f, .004f, 15, 10);
                mesh.Rock(new Vector3(-.33f, Ground, .27f), new Vector3(.12f, .11f, .10f), 8, 1.2f);
                mesh.Rock(new Vector3(.31f, Ground, -.28f), new Vector3(.10f, .07f, .12f), 8, 3.1f);
                break;
            case TerrainEnvironmentKind.Cave:
                mesh.Patch(Vector3.zero, .47f, Ground, .014f, 6, 12, .07f);
                mesh.Ring(new Vector3(0, Ground + .014f, 0), .36f, .24f, .055f, 7, 12);
                mesh.Patch(Vector3.zero, .238f, Ground + .016f, .004f, 18, 12, 0);
                // A broken stone arch leaves the center usable for the travelling piece.
                mesh.Rock(new Vector3(-.28f, Ground + .05f, .22f), new Vector3(.14f, .32f, .14f), 7, 2.2f);
                mesh.Rock(new Vector3(.28f, Ground + .05f, .22f), new Vector3(.14f, .29f, .14f), 7, .7f);
                mesh.Rock(new Vector3(0, Ground + .30f, .25f), new Vector3(.25f, .16f, .13f), 8, 3.4f);
                mesh.Crystal(new Vector3(-.32f, Ground + .09f, -.14f), .035f, .12f, 22);
                mesh.Crystal(new Vector3(.32f, Ground + .09f, -.14f), .035f, .12f, 22);
                break;
            case TerrainEnvironmentKind.Smoke:
                mesh.Patch(Vector3.zero, .46f, Ground, .012f, 18, 12, .13f);
                mesh.Cloud(new Vector3(-.27f, Ground + .10f, -.24f), new Vector3(.19f, .10f, .16f), 19);
                mesh.Cloud(new Vector3(.27f, Ground + .14f, -.25f), new Vector3(.17f, .13f, .17f), 20);
                mesh.Cloud(new Vector3(.26f, Ground + .23f, .24f), new Vector3(.19f, .17f, .18f), 19);
                mesh.Cloud(new Vector3(-.27f, Ground + .20f, .25f), new Vector3(.18f, .15f, .19f), 20);
                mesh.Cloud(new Vector3(.02f, Ground + .28f, .37f), new Vector3(.13f, .10f, .10f), 21);
                break;
        }
        AddNatureDetails(mesh, kind);
        return mesh;
    }
    static void Tree(VisualMesh mesh, Vector3 position, float height, float radius)
    {
        mesh.Frustum(position, .037f, .021f, height * .75f, 4, 7);
        for (int layer = 0; layer < 4; layer++)
        {
            float t = layer / 3f;
            var offset = new Vector3(Mathf.Sin(layer * 2.2f) * .014f, height * (.23f + t * .49f), Mathf.Cos(layer * 2.2f) * .01f);
            mesh.Frustum(position + offset, radius * (1 - t * .52f), .002f, height * (.34f - t * .06f), layer % 2 == 0 ? 1 : 2, 7);
        }
        for (int i = 0; i < 3; i++)
        {
            float a = i * Mathf.PI * 2 / 3;
            mesh.Tube(position + Vector3.up * .04f, position + new Vector3(Mathf.Cos(a) * .075f, .009f, Mathf.Sin(a) * .075f), .018f, .005f, 4, 4);
        }
    }
    static void Bush(VisualMesh mesh, Vector3 position, float radius, float height, int color)
    {
        mesh.Rock(position, new Vector3(radius, height, radius * .88f), color, .9f);
        mesh.Rock(position + new Vector3(-radius * .48f, .01f, radius * .35f), new Vector3(radius * .65f, height * .80f, radius * .67f), color == 2 ? 3 : 2, 2.4f);
        mesh.Leaf(position + new Vector3(radius * .55f, 0, -radius * .55f), radius * .4f, height * 1.1f, 3, 20);
    }

    sealed class VisualMesh
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int> triangles = new List<int>();
        void Tri(Vector3 a, Vector3 b, Vector3 c, int color)
        {
            int index = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c);
            var uv = new Vector2((color + .5f) / Palette.Length, .5f); uvs.Add(uv); uvs.Add(uv); uvs.Add(uv);
            triangles.Add(index); triangles.Add(index + 1); triangles.Add(index + 2);
        }
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int color)
        { Tri(a, b, c, color); Tri(a, c, d, color); }
        static Vector3 Radial(Vector3 center, float radius, float y, float angle)
            => center + new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
        public void Frustum(Vector3 origin, float bottomRadius, float topRadius, float height, int color, int sides)
        {
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides, b = (i + 1) * Mathf.PI * 2 / sides;
                var ba = Radial(origin, bottomRadius, 0, a); var bb = Radial(origin, bottomRadius, 0, b);
                var ta = Radial(origin, topRadius, height, a); var tb = Radial(origin, topRadius, height, b);
                Quad(ba, ta, tb, bb, color); Tri(origin + Vector3.up * height, tb, ta, color);
            }
        }
        public void Patch(Vector3 center, float radius, float y, float thickness, int color, int sides, float irregularity)
        {
            // Elliptical/irregular perimeter stays inside a cell even beside a connected cluster.
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides, b = (i + 1) * Mathf.PI * 2 / sides;
                float ra = radius * (1 - irregularity * (.5f + .5f * Mathf.Sin(i * 3.2f + .3f)));
                float rb = radius * (1 - irregularity * (.5f + .5f * Mathf.Sin((i + 1) % sides * 3.2f + .3f)));
                var ba = Radial(center, ra, y, a); var bb = Radial(center, rb, y, b);
                var ta = ba + Vector3.up * thickness; var tb = bb + Vector3.up * thickness;
                Quad(ba, ta, tb, bb, color); Tri(center + Vector3.up * (y + thickness), tb, ta, color);
            }
        }
        public void Ring(Vector3 origin, float outer, float inner, float height, int color, int sides)
        {
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides, b = (i + 1) * Mathf.PI * 2 / sides;
                var oa = Radial(origin, outer, 0, a); var ob = Radial(origin, outer, 0, b);
                var ta = Radial(origin, outer * .96f, height, a); var tb = Radial(origin, outer * .96f, height, b);
                var ia = Radial(origin, inner, height, a); var ib = Radial(origin, inner, height, b);
                Quad(oa, ta, tb, ob, color); Quad(ta, ia, ib, tb, color);
                Quad(ia, Radial(origin, inner, 0, a), Radial(origin, inner, 0, b), ib, color);
            }
        }
        public void Rock(Vector3 origin, Vector3 size, int color, float seed)
        {
            const int sides = 7;
            var lower = new Vector3[sides]; var middle = new Vector3[sides]; var upper = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float angle = (i + seed * .1f) * Mathf.PI * 2 / sides;
                float vary = .85f + .13f * Mathf.Sin(i * 2.9f + seed);
                var radial = new Vector3(Mathf.Cos(angle) * size.x * vary, 0, Mathf.Sin(angle) * size.z * vary);
                lower[i] = origin + radial * .78f;
                middle[i] = origin + radial + Vector3.up * size.y * (.35f + .07f * Mathf.Sin(i + seed));
                upper[i] = origin + radial * .60f + Vector3.up * size.y * (.83f + .10f * Mathf.Sin(i * 1.7f + seed));
            }
            var tip = origin + new Vector3(size.x * .08f, size.y, -size.z * .06f);
            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;
                Quad(lower[i], middle[i], middle[next], lower[next], color);
                Quad(middle[i], upper[i], upper[next], middle[next], color);
                Tri(tip, upper[next], upper[i], color);
            }
        }
        public void Crystal(Vector3 origin, float radius, float height, int color)
        {
            for (int i = 0; i < 5; i++)
            {
                float a = i * Mathf.PI * 2 / 5, b = (i + 1) * Mathf.PI * 2 / 5;
                var ba = Radial(origin, radius, 0, a); var bb = Radial(origin, radius, 0, b);
                var ta = Radial(origin, radius, height * .68f, a); var tb = Radial(origin, radius, height * .68f, b);
                Quad(ba, ta, tb, bb, color); Tri(ta, origin + Vector3.up * height, tb, color);
            }
        }
        public void Leaf(Vector3 origin, float width, float height, int color, float degrees)
        {
            var rotation = Quaternion.Euler(0, degrees, 0);
            var a = origin + rotation * new Vector3(-width * .5f, 0, 0);
            var b = origin + rotation * new Vector3(width * .5f, 0, 0);
            var top = origin + rotation * new Vector3(width * .15f, height, .04f);
            Tri(a, top, b, color); Tri(b, top, a, color);
            a = origin + rotation * new Vector3(0, 0, -width * .5f); b = origin + rotation * new Vector3(0, 0, width * .5f);
            Tri(a, top, b, color); Tri(b, top, a, color);
        }
        public void Tube(Vector3 start, Vector3 end, float bottomRadius, float topRadius, int color, int sides)
        {
            var axis = (end - start).normalized;
            var right = Vector3.Cross(Mathf.Abs(axis.y) > .9f ? Vector3.forward : Vector3.up, axis).normalized;
            var forward = Vector3.Cross(axis, right);
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides, b = (i + 1) * Mathf.PI * 2 / sides;
                var ra = right * Mathf.Cos(a) + forward * Mathf.Sin(a); var rb = right * Mathf.Cos(b) + forward * Mathf.Sin(b);
                Quad(start + ra * bottomRadius, start + rb * bottomRadius, end + rb * topRadius, end + ra * topRadius, color);
                Tri(start, start + rb * bottomRadius, start + ra * bottomRadius, color);
                Tri(end, end + ra * topRadius, end + rb * topRadius, color);
            }
        }
        public void Cloud(Vector3 center, Vector3 radius, int color)
        {
            // Shared vertices preserve smooth smoke normals. Other environment
            // geometry keeps hard facets; no transparent overdraw or particles.
            const int segments = 8, rings = 4;
            int first = vertices.Count;
            for (int r = 0; r <= rings; r++)
                for (int s = 0; s < segments; s++)
                {
                    float latitude = r * Mathf.PI / rings, angle = s * Mathf.PI * 2 / segments;
                    vertices.Add(center + Vector3.Scale(new Vector3(Mathf.Sin(latitude) * Mathf.Cos(angle), Mathf.Cos(latitude), Mathf.Sin(latitude) * Mathf.Sin(angle)), radius));
                    uvs.Add(new Vector2((color + .5f) / Palette.Length, .5f));
                }
            for (int r = 0; r < rings; r++) for (int s = 0; s < segments; s++)
            {
                int a = first + r * segments + s, b = first + r * segments + (s + 1) % segments;
                int c = b + segments, d = a + segments;
                if (r != 0) { triangles.Add(a); triangles.Add(b); triangles.Add(c); }
                if (r != rings - 1) { triangles.Add(a); triangles.Add(c); triangles.Add(d); }
            }
        }
        public Mesh Build(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uvs); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
#endif
