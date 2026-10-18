#if UNITY_EDITOR
using UnityEngine;

public static partial class TerrainEnvironmentProductionInstaller
{
    static void AddNatureDetails(VisualMesh mesh, TerrainEnvironmentKind kind)
    {
        switch (kind)
        {
            case TerrainEnvironmentKind.Forest:
                // Fallen wood, roots and two fern fans read as woodland rather
                // than three isolated cones. The central actor footprint stays open.
                mesh.Tube(new Vector3(-.36f, Ground + .035f, -.06f), new Vector3(-.26f, Ground + .06f, .12f), .036f, .029f, 4, 6);
                mesh.Tube(new Vector3(-.265f, Ground + .06f, .11f), new Vector3(-.255f, Ground + .06f, .13f), .025f, .025f, 5, 6);
                Fern(mesh, new Vector3(.34f, Ground + .018f, .07f), .10f, 3);
                Fern(mesh, new Vector3(-.07f, Ground + .018f, -.37f), .09f, 2);
                ScatterPebbles(mesh, 3, 6);
                break;
            case TerrainEnvironmentKind.Bush:
                Fern(mesh, new Vector3(-.36f, Ground + .016f, -.01f), .10f, 2);
                Fern(mesh, new Vector3(.08f, Ground + .016f, .38f), .08f, 3);
                // Tiny pale seed heads, kept on the rim, make the grass distinct
                // from a low forest without covering selection or attack markers.
                for (int i = 0; i < 4; i++)
                {
                    float a = i * Mathf.PI * .5f + .45f;
                    var p = new Vector3(Mathf.Cos(a) * .35f, Ground + .13f, Mathf.Sin(a) * .35f);
                    mesh.Tube(p - Vector3.up * .10f, p, .006f, .003f, 2, 3);
                    mesh.Cloud(p, new Vector3(.017f, .013f, .017f), 23);
                }
                break;
            case TerrainEnvironmentKind.Rock:
                // Lighter strata and low scree explain the cover footprint.
                mesh.Tube(new Vector3(-.37f, Ground + .15f, .15f), new Vector3(-.17f, Ground + .21f, .16f), .009f, .006f, 8, 4);
                mesh.Tube(new Vector3(.17f, Ground + .11f, .16f), new Vector3(.37f, Ground + .15f, .18f), .008f, .006f, 8, 4);
                ScatterPebbles(mesh, 5, 7);
                break;
            case TerrainEnvironmentKind.PoisonSwamp:
                mesh.Ring(new Vector3(-.17f, Ground + .034f, -.10f), .095f, .09f, .003f, 9, 12);
                mesh.Ring(new Vector3(.21f, Ground + .033f, .13f), .075f, .069f, .003f, 9, 12);
                // Discrete scum islands, reeds and bubbles retain a dangerous
                // natural wetland silhouette instead of a uniformly green disc.
                mesh.Patch(new Vector3(.08f, 0, -.22f), .072f, Ground + .031f, .003f, 9, 7, .12f);
                mesh.Patch(new Vector3(-.22f, 0, .16f), .061f, Ground + .031f, .003f, 9, 7, .13f);
                mesh.Cloud(new Vector3(-.17f, Ground + .047f, -.10f), new Vector3(.025f, .016f, .025f), 11);
                for (int i = 0; i < 4; i++)
                    mesh.Tube(new Vector3(.25f + i * .02f, Ground + .025f, .26f), new Vector3(.23f + i * .025f, Ground + .18f + i * .013f, .30f), .007f, .004f, 10, 3);
                break;
            case TerrainEnvironmentKind.Mud:
                // Ruts and shallow puddles make the movement penalty readable.
                mesh.Patch(new Vector3(.07f, 0, .17f), .10f, Ground + .029f, .002f, 12, 9, .28f);
                mesh.Patch(new Vector3(-.17f, 0, -.15f), .078f, Ground + .029f, .002f, 6, 8, .22f);
                for (int side = -1; side <= 1; side += 2)
                    mesh.Tube(new Vector3(side * .10f, Ground + .032f, -.24f), new Vector3(side * .10f + .03f, Ground + .032f, .23f), .011f, .006f, 12, 4);
                break;
            case TerrainEnvironmentKind.Road:
                ScatterPebbles(mesh, 5, 7);
                for (int side = -1; side <= 1; side += 2)
                    mesh.Leaf(new Vector3(side * .38f, Ground + .01f, side * .12f), .06f, .065f, 2, side * 25);
                break;
            case TerrainEnvironmentKind.HealingSpring:
                for (int i = 0; i < 6; i++)
                {
                    float a = i * Mathf.PI / 3 + .15f;
                    var p = new Vector3(Mathf.Cos(a) * .39f, Ground + .068f, Mathf.Sin(a) * .39f);
                    mesh.Rock(p, new Vector3(.052f, .037f, .04f), i % 2 == 0 ? 7 : 8, i * .7f);
                }
                mesh.Ring(new Vector3(.025f, Ground + .052f, -.018f), .065f, .060f, .002f, 16, 12);
                Fern(mesh, new Vector3(.34f, Ground + .08f, .19f), .065f, 2);
                break;
            case TerrainEnvironmentKind.Geyser:
                mesh.Ring(new Vector3(0, Ground + .047f, 0), .34f, .29f, .018f, 8, 12);
                mesh.Ring(new Vector3(0, Ground + .070f, 0), .19f, .15f, .021f, 6, 10);
                mesh.Ring(new Vector3(0, Ground + .052f, 0), .054f, .048f, .003f, 16, 10);
                ScatterPebbles(mesh, 3, 8);
                break;
            case TerrainEnvironmentKind.Cave:
                // The twin portal lights and broken arch keep the center open.
                // A single normal mesh never creates an extra physical obstacle.
                for (int side = -1; side <= 1; side += 2)
                {
                    mesh.Tube(new Vector3(side * .22f, Ground + .035f, -.08f), new Vector3(side * .24f, Ground + .13f, .08f), .012f, .007f, 8, 5);
                    mesh.Rock(new Vector3(side * .20f, Ground + .01f, -.31f), new Vector3(.062f, .025f, .08f), 7, side * .6f);
                }
                mesh.Patch(new Vector3(0, 0, .02f), .18f, Ground + .022f, .002f, 18, 10, .06f);
                break;
            case TerrainEnvironmentKind.Smoke:
                // Charred stumps provide a grounded base for rounded smoke billows.
                mesh.Tube(new Vector3(-.28f, Ground + .015f, .28f), new Vector3(-.24f, Ground + .23f, .25f), .033f, .019f, 18, 5);
                mesh.Tube(new Vector3(.31f, Ground + .01f, -.26f), new Vector3(.25f, Ground + .10f, -.22f), .025f, .014f, 18, 5);
                ScatterPebbles(mesh, 3, 18);
                break;
        }
    }
    static void Fern(VisualMesh mesh, Vector3 p, float radius, int color)
    {
        for (int i = 0; i < 5; i++)
        {
            float a = i * 72 * Mathf.Deg2Rad;
            var offset = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius * .40f;
            mesh.Leaf(p + offset, radius * .60f, radius * (.75f + (i % 2) * .25f), color, -i * 72);
        }
    }
    static void ScatterPebbles(VisualMesh mesh, int count, int color)
    {
        for (int i = 0; i < count; i++)
        {
            float a = (i * 2.39996f + .7f), r = .36f + (i % 2) * .055f;
            mesh.Rock(new Vector3(Mathf.Cos(a) * r, Ground + .006f, Mathf.Sin(a) * r), new Vector3(.024f, .020f, .019f), color, i * 1.7f);
        }
    }
}
#endif
