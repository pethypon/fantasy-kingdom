#if UNITY_EDITOR
using System;
using UnityEngine;
using ChessMesh = R1UnitVisualInstaller.ChessMesh;

public static partial class R3UnitVisualInstaller
{
    // The pieces read by geometry on one triplanar marble material.
    const int Stone = 3;

    static Mesh Sculpt(Kind kind)
    {
        var mesh = new ChessMesh();
        mesh.Lathe(new Vector3(0, -.5f, 0), new[] {
            new Vector2(.335f, 0), new Vector2(.382f, .034f), new Vector2(.382f, .074f),
            new Vector2(.306f, .126f), new Vector2(.306f, .153f), new Vector2(.252f, .19f)
        }, Stone, 16);

        switch (kind)
        {
            case Kind.WindSpirit: Wind(mesh); break;
            case Kind.LightSpirit: Light(mesh); break;
            case Kind.PurifyingSpirit: Purification(mesh); break;
            case Kind.Martyr: Martyr(mesh); break;
            case Kind.BladeSpirit: Blade(mesh); break;
            case Kind.RockGolem: Golem(mesh); break;
            case Kind.CrystalSpirit: CrystalCluster(mesh); break;
            case Kind.Thunderbird: Thunderbird(mesh); break;
            case Kind.CurseSpirit: Curse(mesh); break;
            case Kind.ThunderSpirit: Thunder(mesh); break;
            case Kind.LifeSpirit: Life(mesh); break;
            case Kind.IceSpirit: Ice(mesh); break;
            case Kind.Swordsman:
            case Kind.Spearman:
            case Kind.Javelineer:
            case Kind.Shieldbearer: Infantry(mesh, kind); break;
            case Kind.Cavalry: Cavalry(mesh); break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
        return mesh.Build(kind + " Sculpture");
    }

    static void Wind(ChessMesh mesh)
    {
        // Two tapered ribbons with open space between them. The higher ribbon curls back into the wind.
        Sweep(mesh, new Vector3(0, -.32f, 0), 2.6f, .83f, .23f, 7, 0f);
        Sweep(mesh, new Vector3(0, -.26f, 0), 1.4f, .54f, .30f, 5, Mathf.PI);
    }

    static void Sweep(ChessMesh mesh, Vector3 origin, float turns, float height, float width, int segments, float offset)
    {
        var path = new Vector3[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float step = (float)i / segments;
            float angle = step * turns * Mathf.PI + offset;
            float radius = width * Mathf.Sin(Mathf.PI * step * .88f);
            path[i] = origin + new Vector3(Mathf.Cos(angle) * radius, height * step, Mathf.Sin(angle) * radius);
            if (i > 0)
            {
                float thickness = Mathf.Lerp(.098f, .023f, step);
                mesh.Beam(path[i - 1], path[i], thickness, Stone, 6);
                if (i < segments) mesh.Gem(path[i], Vector3.one * thickness * 1.07f, Stone, 6, 3);
            }
        }
    }

    static void Light(ChessMesh mesh)
    {
        mesh.Lathe(new Vector3(0, -.31f, 0), new[] {
            new Vector2(.17f, 0), new Vector2(.085f, .11f), new Vector2(.065f, .36f)
        }, Stone, 8);
        var center = new Vector3(0, .22f, 0);
        mesh.VerticalRing(center, .237f, .177f, .09f, Stone, 12);
        mesh.Gem(center, new Vector3(.12f, .145f, .11f), Stone, 8, 4);
        for (int i = 0; i < 8; i++)
        {
            var ray = Quaternion.Euler(0, 0, i * 45f);
            mesh.Blade(center + ray * (Vector3.up * .218f), i % 2 == 0 ? .114f : .078f, .036f, Stone, ray);
        }
    }

    static void Purification(ChessMesh mesh)
    {
        // An open pointed arch enclosing a water drop. The basin anchors both uprights.
        mesh.Lathe(new Vector3(0, -.31f, 0), new[] {
            new Vector2(.12f, 0), new Vector2(.18f, .08f), new Vector2(.265f, .16f), new Vector2(.22f, .19f)
        }, Stone, 12);
        for (int side = -1; side <= 1; side += 2)
        {
            var foot = new Vector3(side * .18f, -.18f, 0);
            var bend = new Vector3(side * .233f, .19f, 0);
            var shoulder = new Vector3(side * .155f, .37f, 0);
            var top = new Vector3(0, .55f, 0);
            mesh.Beam(foot, bend, .045f, Stone, 6);
            mesh.Beam(bend, shoulder, .047f, Stone, 6);
            mesh.Beam(shoulder, top, .048f, Stone, 6);
            mesh.Gem(bend, new Vector3(.065f, .064f, .062f), Stone, 6, 3);
        }
        mesh.Lathe(new Vector3(0, -.10f, 0), new[] {
            new Vector2(.047f, 0), new Vector2(.100f, .095f), new Vector2(.095f, .16f), new Vector2(.022f, .35f)
        }, Stone, 8);
    }

    static void Martyr(ChessMesh mesh)
    {
        mesh.EllipticLathe(new Vector3(0, -.31f, -.013f), new[] {
            new Vector2(.215f, 0), new Vector2(.208f, .07f), new Vector2(.161f, .15f),
            new Vector2(.162f, .30f), new Vector2(.110f, .39f)
        }, 1.12f, Stone, 10);
        mesh.Gem(new Vector3(0, .16f, -.095f), new Vector3(.126f, .127f, .138f), Stone, 8, 5);
        mesh.Blade(new Vector3(0, .181f, -.011f), .21f, .105f, Stone, Quaternion.Euler(34, 0, 0));
        for (int side = -1; side <= 1; side += 2)
        {
            mesh.Beam(new Vector3(side * .12f, .05f, .013f), new Vector3(side * .15f, -.045f, -.17f), .054f, Stone, 6);
            mesh.Beam(new Vector3(side * .15f, -.045f, -.17f), new Vector3(side * .017f, .05f, -.25f), .048f, Stone, 6);
        }
        mesh.CarvedBox(new Vector3(0, .035f, -.25f), new Vector3(.080f, .17f, .067f), Stone, Quaternion.Euler(-35, 0, 0));
        mesh.Box(new Vector3(0, -.184f, -.21f), new Vector3(.23f, .08f, .06f), Stone);
    }

    static void Blade(ChessMesh mesh)
    {
        // A tall scythe-shaped sculpture with a narrow grip and a single broad cutting face.
        mesh.Beam(new Vector3(-.08f, -.31f, .04f), new Vector3(.075f, .33f, .04f), .062f, Stone, 8);
        mesh.Beam(new Vector3(-.20f, -.09f, .04f), new Vector3(.15f, -.04f, .04f), .037f, Stone, 6);
        mesh.Profile(new Vector3(0, .07f, .04f), new[] {
            new Vector2(-.032f, -.13f), new Vector2(.095f, .035f), new Vector2(.169f, .25f),
            new Vector2(.108f, .38f), new Vector2(-.07f, .467f), new Vector2(-.277f, .469f),
            new Vector2(-.16f, .39f), new Vector2(-.055f, .275f), new Vector2(-.019f, .16f),
            new Vector2(-.123f, .028f)
        }, .057f, Stone, Quaternion.Euler(0, -16, 0));
        mesh.Beam(new Vector3(.004f, .17f, -.02f), new Vector3(.067f, .34f, -.02f), .026f, Stone, 6);
    }

    static void Golem(ChessMesh mesh)
    {
        mesh.CarvedBox(new Vector3(0, .04f, .015f), new Vector3(.42f, .41f, .30f), Stone, Quaternion.Euler(6, 0, 0));
        mesh.CarvedBox(new Vector3(0, .311f, -.033f), new Vector3(.245f, .184f, .248f), Stone);
        mesh.CarvedBox(new Vector3(0, .325f, -.159f), new Vector3(.278f, .064f, .062f), Stone);
        for (int side = -1; side <= 1; side += 2)
        {
            mesh.CarvedBox(new Vector3(side * .115f, -.202f, -.065f), new Vector3(.177f, .234f, .31f), Stone);
            mesh.CarvedBox(new Vector3(side * .245f, .177f, .025f), new Vector3(.22f, .247f, .267f), Stone, Quaternion.Euler(3, 0, side * -27));
            mesh.CarvedBox(new Vector3(side * .29f, -.034f, -.066f), new Vector3(.246f, .26f, .24f), Stone, Quaternion.Euler(15, side * 15, side * 9));
        }
        mesh.Gem(new Vector3(0, .064f, -.163f), new Vector3(.12f, .13f, .08f), Stone, 5, 3);
    }

    static void CrystalCluster(ChessMesh mesh)
    {
        Prism(mesh, new Vector3(0, -.31f, .027f), .16f, .91f);
        Prism(mesh, new Vector3(-.207f, -.30f, -.062f), .097f, .56f);
        Prism(mesh, new Vector3(.205f, -.31f, -.010f), .118f, .67f);
        Prism(mesh, new Vector3(.018f, -.31f, -.205f), .106f, .36f);
        Prism(mesh, new Vector3(-.135f, -.31f, .167f), .072f, .51f);
    }

    static void Prism(ChessMesh mesh, Vector3 root, float radius, float height)
    {
        mesh.Lathe(root, new[] { new Vector2(radius * .72f, 0), new Vector2(radius, height * .22f),
            new Vector2(radius * .90f, height * .78f), new Vector2(.004f, height)
        }, Stone, 6);
    }

    static void Thunderbird(ChessMesh mesh)
    {
        mesh.Gem(new Vector3(0, .066f, .005f), new Vector3(.116f, .341f, .137f), Stone, 8, 5);
        mesh.Gem(new Vector3(0, .296f, -.12f), new Vector3(.092f, .104f, .101f), Stone, 8, 4);
        mesh.Blade(new Vector3(0, .289f, -.18f), .145f, .057f, Stone, Quaternion.Euler(-115, 0, 0));
        mesh.Blade(new Vector3(0, .344f, -.085f), .16f, .043f, Stone, Quaternion.Euler(34, 0, 0));
        for (int side = -1; side <= 1; side += 2)
        {
            mesh.Profile(new Vector3(side * .04f, .08f, .037f), new[] {
                new Vector2(0, -.11f), new Vector2(side * .193f, .02f), new Vector2(side * .392f, .33f),
                new Vector2(side * .416f, .486f), new Vector2(side * .301f, .416f),
                new Vector2(side * .184f, .384f), new Vector2(side * .081f, .19f)
            }, .055f, Stone, Quaternion.Euler(0, side * 16, 0));
            mesh.Blade(new Vector3(side * .206f, .26f, .064f), .215f, .057f, Stone, Quaternion.Euler(10, 0, side * -52));
            mesh.Blade(new Vector3(side * .075f, .028f, .070f), .325f, .069f, Stone, Quaternion.Euler(162, 0, side * -11));
        }
    }

    static void Curse(ChessMesh mesh)
    {
        mesh.EllipticLathe(new Vector3(0, -.31f, 0), new[] {
            new Vector2(.195f, 0), new Vector2(.15f, .11f), new Vector2(.116f, .29f), new Vector2(.185f, .42f)
        }, .86f, Stone, 8);
        mesh.Gem(new Vector3(0, .219f, .02f), new Vector3(.17f, .158f, .14f), Stone, 8, 4);
        mesh.Shield(new Vector3(0, .163f, -.106f), .127f, .165f, .036f, Stone);
        for (int side = -1; side <= 1; side += 2)
        {
            var start = new Vector3(side * .119f, .27f, -.01f);
            var elbow = new Vector3(side * .284f, .363f, .01f);
            mesh.Beam(start, elbow, .065f, Stone, 7);
            mesh.Blade(elbow, .197f, .056f, Stone, Quaternion.Euler(9, 0, side * -14));
            mesh.CarvedBox(new Vector3(side * .077f, .193f, -.168f), new Vector3(.158f, .054f, .068f), Stone, Quaternion.Euler(0, 0, side * 20));
        }
    }

    static void Thunder(ChessMesh mesh)
    {
        mesh.Profile(new Vector3(0, 0, 0), new[] {
            new Vector2(-.113f, -.31f), new Vector2(.035f, .045f), new Vector2(-.154f, -.004f),
            new Vector2(-.002f, .491f), new Vector2(.252f, .566f), new Vector2(.116f, .233f),
            new Vector2(.285f, .275f), new Vector2(.108f, -.142f), new Vector2(.128f, -.327f)
        }, .083f, Stone, Quaternion.Euler(0, 15, 0));
        mesh.Blade(new Vector3(-.211f, -.31f, -.047f), .38f, .059f, Stone, Quaternion.Euler(0, 0, -17));
        mesh.Blade(new Vector3(.132f, -.31f, .113f), .43f, .075f, Stone, Quaternion.Euler(12, 0, -26));
    }

    static void Life(ChessMesh mesh)
    {
        mesh.Beam(new Vector3(0, -.31f, 0), new Vector3(0, .37f, 0), .066f, Stone, 9);
        mesh.Gem(new Vector3(0, .43f, 0), new Vector3(.105f, .15f, .104f), Stone, 8, 5);
        for (int i = 0; i < 4; i++)
        {
            var turn = Quaternion.Euler(0, i * 90f + 20f, 0);
            var root = turn * new Vector3(.034f, -.196f + .065f * i, 0);
            var tip = turn * new Vector3(.283f, .12f + .062f * i, 0);
            var direction = tip - root;
            mesh.Blade(root, direction.magnitude, .076f, Stone, Quaternion.FromToRotation(Vector3.up, direction.normalized));
        }
        mesh.Lathe(new Vector3(0, -.31f, 0), new[] { new Vector2(.202f, 0), new Vector2(.15f, .075f),
            new Vector2(.084f, .135f) }, Stone, 10);
    }

    static void Ice(ChessMesh mesh)
    {
        Prism(mesh, new Vector3(0, -.31f, 0), .074f, .55f);
        var core = new Vector3(0, .24f, 0);
        mesh.Gem(core, new Vector3(.12f, .125f, .12f), Stone, 6, 4);
        // Tilt the snowflake toward the board camera. Six branches and their forks are solid stone.
        var tilt = Quaternion.Euler(30, 0, 0);
        for (int i = 0; i < 6; i++)
        {
            var branch = tilt * Quaternion.Euler(0, 0, i * 60f);
            mesh.Blade(core, .315f, .038f, Stone, branch);
            for (int side = -1; side <= 1; side += 2)
            {
                var limb = branch * Quaternion.Euler(0, 0, side * 52);
                mesh.Blade(core + branch * (Vector3.up * .164f), .134f, .036f, Stone, limb);
            }
        }
    }

    static void Infantry(ChessMesh mesh, Kind kind)
    {
        bool guard = kind == Kind.Shieldbearer;
        float breadth = guard ? .47f : .335f;
        mesh.CarvedBox(new Vector3(0, -.011f, .022f), new Vector3(breadth, .402f, .258f), Stone);
        mesh.CarvedBox(new Vector3(0, .288f, -.013f), new Vector3(.205f, .208f, .218f), Stone);
        mesh.CarvedBox(new Vector3(0, .281f, -.128f), new Vector3(.185f, .060f, .053f), Stone);
        for (int side = -1; side <= 1; side += 2)
        {
            mesh.CarvedBox(new Vector3(side * .095f, -.24f, -.012f), new Vector3(.136f, .148f, .213f), Stone);
            mesh.CarvedBox(new Vector3(side * breadth * .475f, .128f, .013f), new Vector3(.162f, .168f, .206f), Stone, Quaternion.Euler(0, 0, side * 12));
            mesh.Beam(new Vector3(side * .174f, .104f, .023f), new Vector3(side * .24f, -.008f, -.139f), .056f, Stone, 6);
        }
        mesh.CarvedBox(new Vector3(0, -.119f, -.13f), new Vector3(.27f, .063f, .05f), Stone);
        switch (kind)
        {
            case Kind.Swordsman:
                mesh.Blade(new Vector3(0, .365f, .02f), .132f, .068f, Stone, Quaternion.Euler(22, 0, 0));
                var hilt = new Vector3(.236f, -.115f, -.234f);
                var swordAngle = Quaternion.Euler(-16, 0, 43);
                mesh.Beam(hilt, hilt + swordAngle * (Vector3.up * .20f), .032f, Stone, 8);
                var guardCenter = hilt + swordAngle * (Vector3.up * .18f);
                mesh.CarvedBox(guardCenter, new Vector3(.208f, .044f, .098f), Stone, swordAngle);
                mesh.Blade(guardCenter, .60f, .079f, Stone, swordAngle);
                break;
            case Kind.Spearman:
                Lance(mesh, new Vector3(.248f, -.306f, -.147f), new Vector3(.231f, .493f, -.144f), .035f, .251f);
                mesh.Blade(new Vector3(0, .381f, .016f), .15f, .026f, Stone, Quaternion.Euler(37, 0, 0));
                mesh.Gem(new Vector3(-.166f, .015f, -.194f), new Vector3(.147f, .144f, .052f), Stone, 10, 4);
                mesh.Gem(new Vector3(-.166f, .015f, -.239f), new Vector3(.061f, .069f, .036f), Stone, 8, 4);
                break;
            case Kind.Javelineer:
                Lance(mesh, new Vector3(-.39f, .109f, .203f), new Vector3(.285f, .371f, -.213f), .036f, .163f);
                mesh.CarvedBox(new Vector3(-.101f, .078f, .146f), new Vector3(.175f, .447f, .094f), Stone, Quaternion.Euler(-13, 0, -20));
                Lance(mesh, new Vector3(-.227f, -.11f, .174f), new Vector3(-.06f, .405f, .174f), .02f, .139f);
                Lance(mesh, new Vector3(-.262f, -.12f, .195f), new Vector3(-.095f, .460f, .195f), .02f, .11f);
                break;
            case Kind.Shieldbearer:
                mesh.CarvedBox(new Vector3(0, .076f, -.233f), new Vector3(.614f, .60f, .146f), Stone, Quaternion.Euler(-6, 0, 0));
                mesh.CarvedBox(new Vector3(0, .08f, -.309f), new Vector3(.506f, .476f, .044f), Stone);
                mesh.Gem(new Vector3(0, .064f, -.35f), new Vector3(.134f, .138f, .065f), Stone, 8, 4);
                mesh.Box(new Vector3(0, .20f, -.342f), new Vector3(.072f, .207f, .037f), Stone);
                break;
        }
    }

    static void Lance(ChessMesh mesh, Vector3 grip, Vector3 socket, float radius, float pointLength)
    {
        mesh.Beam(grip, socket, radius, Stone, 6);
        mesh.Blade(socket, pointLength, radius * 1.55f, Stone, Quaternion.FromToRotation(Vector3.up, (socket - grip).normalized));
    }

    static void Cavalry(ChessMesh mesh)
    {
        // Full horse and rider, with the low lance carried away from the horse's crest.
        mesh.CarvedBox(new Vector3(0, -.070f, .068f), new Vector3(.32f, .308f, .565f), Stone);
        mesh.CarvedBox(new Vector3(0, .085f, -.188f), new Vector3(.217f, .383f, .218f), Stone, Quaternion.Euler(-24, 0, 0));
        mesh.CarvedBox(new Vector3(0, .269f, -.275f), new Vector3(.178f, .142f, .221f), Stone, Quaternion.Euler(18, 0, 0));
        mesh.CarvedBox(new Vector3(0, .217f, -.372f), new Vector3(.155f, .108f, .148f), Stone);
        for (int side = -1; side <= 1; side += 2)
        {
            mesh.Blade(new Vector3(side * .054f, .319f, -.252f), .119f, .036f, Stone, Quaternion.Euler(11, 0, side * -14));
            mesh.CarvedBox(new Vector3(side * .089f, -.207f, -.154f), new Vector3(.105f, .207f, .121f), Stone, Quaternion.Euler(18, 0, 0));
            mesh.CarvedBox(new Vector3(side * .103f, -.201f, .216f), new Vector3(.108f, .206f, .143f), Stone, Quaternion.Euler(-19, 0, 0));
        }
        mesh.CarvedBox(new Vector3(0, .198f, .115f), new Vector3(.213f, .299f, .17f), Stone, Quaternion.Euler(16, 0, 0));
        mesh.Gem(new Vector3(0, .418f, .074f), new Vector3(.092f, .114f, .103f), Stone, 8, 5);
        mesh.Blade(new Vector3(0, .467f, .097f), .128f, .04f, Stone, Quaternion.Euler(37, 0, 0));
        mesh.Shield(new Vector3(-.126f, .159f, .017f), .105f, .143f, .043f, Stone);
        mesh.Beam(new Vector3(.082f, .273f, .087f), new Vector3(.236f, .173f, -.06f), .048f, Stone, 6);
        Lance(mesh, new Vector3(.225f, .058f, .317f), new Vector3(.239f, .237f, -.286f), .032f, .163f);
    }
}
#endif
