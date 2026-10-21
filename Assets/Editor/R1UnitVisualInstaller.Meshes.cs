#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

public static partial class R1UnitVisualInstaller
{
    // Palette: dark steel 0/1/2, silver 3, brass 4/5, ivory 6, teal 7/8, red 9/10, black 11.
    static ChessMesh MakeVisual(Kind kind)
    {
        var mesh = new ChessMesh();
        mesh.Lathe(new Vector3(0, -.5f, 0), new[] { new Vector2(.32f, 0), new Vector2(.382f, .025f), new Vector2(.382f, .065f), new Vector2(.342f, .10f) }, 4, 12);
        mesh.Lathe(new Vector3(0, -.4f, 0), new[] { new Vector2(.335f, 0), new Vector2(.32f, .055f), new Vector2(.26f, .09f) }, 0, 12);
        mesh.Lathe(new Vector3(0, -.31f, 0), new[] { new Vector2(.265f, 0), new Vector2(.265f, .025f), new Vector2(.225f, .045f) }, 4, 12);
        // Brass dots keep the same visible base silhouette at distant map zooms.
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI / 4;
            mesh.Gem(new Vector3(Mathf.Cos(a) * .334f, -.355f, Mathf.Sin(a) * .334f), new Vector3(.018f, .015f, .018f), 4, 4, 4);
        }
        switch (kind)
        {
            case Kind.Knight: Knight(mesh); break;
            case Kind.Archer: Archer(mesh); break;
            case Kind.Magic: Magic(mesh); break;
            case Kind.Assassin: Assassin(mesh); break;
            case Kind.Scout: Scout(mesh); break;
            case Kind.Priest: Priest(mesh); break;
            case Kind.Guardian: Guardian(mesh); break;
            case Kind.Crossbow: Crossbow(mesh); break;
            case Kind.Magicsniper: MagicSniper(mesh); break;
            case Kind.Bomber: Bomber(mesh); break;
            case Kind.WhiteMage: WhiteMage(mesh); break;
            case Kind.HolyKnight: HolyKnight(mesh); break;
            case Kind.Samurai: Samurai(mesh); break;
            case Kind.Ninja: Ninja(mesh); break;
            case Kind.Berserker: Berserker(mesh); break;
            case Kind.Navigator: Navigator(mesh); break;
            case Kind.HeavyWarrior: HeavyWarrior(mesh); break;
            case Kind.DragonKnight: DragonKnight(mesh); break;
        }
        // A broader body on a weighted plinth reads as a carved board piece rather than a light figurine.
        // Compress only the sculpture above the plinth; the footprint and contact height remain unchanged.
        mesh.LowerStance(.92f);
        return mesh;
    }

    /// <summary>Rebuild only installed stock mesh assets in the isolated validation checkout.</summary>
    public static void RebuildForValidation()
    {
        var project = new System.IO.DirectoryInfo(Application.dataPath).Parent;
        if (project == null || project.Name != "UnityValidation" || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
            throw new System.InvalidOperationException("生成モデルの再焼成は停止中のUnityValidationで実行してください。");
        foreach (var kind in Kinds)
        {
            var existing = UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath(kind));
            if (existing == null || UnityEditor.AssetDatabase.GetAssetPath(existing) != MeshPath(kind))
                throw new System.InvalidOperationException("再焼成対象の標準Meshがありません: " + kind);
            var replacement = MakeVisual(kind).Build("R1_" + kind + "_Chess");
            UnityEditor.EditorUtility.CopySerialized(replacement, existing);
            Object.DestroyImmediate(replacement);
            UnityEditor.EditorUtility.SetDirty(existing);
        }
        UnityEditor.AssetDatabase.SaveAssets();
        ValidateAssets();
    }
    static void Knight(ChessMesh mesh)
    {
        mesh.EllipticLathe(new Vector3(0, -.265f, .025f), new[] {
            new Vector2(.24f, 0), new Vector2(.225f, .07f), new Vector2(.16f, .22f),
            new Vector2(.15f, .36f), new Vector2(.115f, .48f) }, .97f, 1, 12);
        // A traditional chess knight profile. The outline is extruded across X; the muzzle points forward (-Z).
        mesh.Profile(new Vector3(0, .12f, 0), new[] {
            new Vector2(.15f, 0), new Vector2(.15f, .34f), new Vector2(.10f, .52f), new Vector2(.04f, .48f),
            new Vector2(-.01f, .41f), new Vector2(-.10f, .39f), new Vector2(-.24f, .22f), new Vector2(-.23f, .14f),
            new Vector2(-.11f, .13f), new Vector2(-.02f, .23f), new Vector2(.03f, .04f)
        }, .118f, 3, Quaternion.Euler(0, -90, 0));
        mesh.Beam(new Vector3(-.10f, .45f, -.02f), new Vector3(.10f, .45f, -.02f), .02f, 4, 6);
        mesh.Gem(new Vector3(.099f, .47f, -.07f), new Vector3(.015f, .017f, .018f), 11, 5, 4);
        mesh.Gem(new Vector3(-.099f, .47f, -.07f), new Vector3(.015f, .017f, .018f), 11, 5, 4);
        mesh.Gem(new Vector3(0, .43f, -.135f), new Vector3(.105f, .15f, .13f), 3, 8, 4);
        mesh.CarvedBox(new Vector3(0, .305f, -.21f), new Vector3(.19f, .12f, .16f), 3);
        for (int side = -1; side <= 1; side += 2)
        {
            mesh.Blade(new Vector3(side * .06f, .57f, .025f), .14f, .035f, 3, Quaternion.Euler(-12, 0, side * -12));
            mesh.Gem(new Vector3(side * .099f, .325f, -.25f), new Vector3(.013f, .018f, .009f), 11, 4, 3);
        }
        for (int i = 0; i < 5; i++)
            mesh.CarvedBox(new Vector3(0, .20f + .065f * i, .135f), new Vector3(.16f - i * .009f, .065f, .07f), 0,
                Quaternion.Euler(-22, 0, 0));
        mesh.Lathe(new Vector3(0, -.03f, 0), new[] { new Vector2(.165f, 0), new Vector2(.16f, .035f) }, 4, 10);
        for (int side = -1; side <= 1; side += 2)
            mesh.CarvedBox(new Vector3(side * .155f, -.01f, .035f), new Vector3(.12f, .235f, .225f), 2,
                Quaternion.Euler(0, side * 12, side * -8));
        mesh.CarvedBox(new Vector3(0, -.075f, -.135f), new Vector3(.225f, .205f, .085f), 3);
    }
    static void Archer(ChessMesh mesh)
    {
        JoinedArms(mesh, .16f, new Vector3(.25f, .20f, .12f), new Vector3(.37f, .20f, -.04f), 7);
        Torso(mesh, .16f, 7);
        mesh.Gem(new Vector3(0, .35f, 0), new Vector3(.12f, .14f, .115f), 0, 8, 5);
        mesh.Box(new Vector3(0, .35f, -.106f), new Vector3(.14f, .065f, .025f), 6);
        mesh.Box(new Vector3(0, .36f, -.124f), new Vector3(.11f, .012f, .01f), 11);
        mesh.Box(new Vector3(0, .13f, -.145f), new Vector3(.045f, .24f, .035f), 5, Quaternion.Euler(0, 0, -28));
        var bowTop = new Vector3(.25f, .61f, -.04f); var bowMiddle = new Vector3(.38f, .20f, -.04f); var bowBottom = new Vector3(.25f, -.21f, -.04f);
        mesh.Beam(bowTop, new Vector3(.35f, .40f, -.04f), .025f, 4, 6);
        mesh.Beam(new Vector3(.35f, .40f, -.04f), bowMiddle, .027f, 5, 6);
        mesh.Beam(bowMiddle, new Vector3(.35f, -.02f, -.04f), .027f, 5, 6);
        mesh.Beam(new Vector3(.35f, -.02f, -.04f), bowBottom, .025f, 4, 6);
        mesh.Beam(bowTop, bowBottom, .006f, 6, 4);
        mesh.Beam(new Vector3(.25f, .20f, .12f), new Vector3(.25f, .20f, -.36f), .008f, 3, 5);
        mesh.Gem(new Vector3(.25f, .20f, -.35f), new Vector3(.020f, .020f, .048f), 3, 4, 4);
        mesh.Beam(new Vector3(-.16f, -.05f, .10f), new Vector3(-.16f, .42f, .10f), .045f, 5, 8);
        for (int i = 0; i < 3; i++) mesh.Beam(new Vector3(-.19f + i * .03f, .30f, .10f), new Vector3(-.19f + i * .03f, .54f, .10f), .008f, 6, 4);
    }
    static void Magic(ChessMesh mesh)
    {
        CarvedRobe(mesh, .23f, 2);
        JoinedArms(mesh, .145f, new Vector3(-.18f, .07f, -.12f), new Vector3(.26f, .14f, 0), 2);
        mesh.Gem(new Vector3(0, .32f, -.01f), new Vector3(.085f, .10f, .085f), 6, 8, 5);
        mesh.Lathe(new Vector3(0, .38f, 0), new[] { new Vector2(.20f, 0), new Vector2(.20f, .025f), new Vector2(.115f, .04f), new Vector2(.015f, .29f) }, 0, 10);
        mesh.Lathe(new Vector3(0, .43f, 0), new[] { new Vector2(.105f, 0), new Vector2(.09f, .035f) }, 4, 10);
        mesh.Beam(new Vector3(.26f, -.21f, 0), new Vector3(.26f, .57f, 0), .023f, 5, 7);
        mesh.Gem(new Vector3(.26f, .63f, 0), new Vector3(.085f, .13f, .085f), 8, 6, 4);
        mesh.VerticalRing(new Vector3(.26f, .61f, 0), .12f, .105f, .018f, 4, 8);
        mesh.Box(new Vector3(-.18f, .03f, -.12f), new Vector3(.14f, .16f, .045f), 9, Quaternion.Euler(0, 12, -15));
        mesh.Box(new Vector3(-.18f, .03f, -.148f), new Vector3(.025f, .115f, .012f), 4, Quaternion.Euler(0, 12, -15));
        mesh.Box(new Vector3(0, -.02f, -.17f), new Vector3(.033f, .30f, .02f), 7);
    }
    static void Assassin(ChessMesh mesh)
    {
        JoinedArms(mesh, .155f, new Vector3(-.25f, .075f, -.10f), new Vector3(.25f, .075f, -.10f), 9);
        Torso(mesh, .155f, 9);
        mesh.Lathe(new Vector3(0, -.26f, .025f), new[] { new Vector2(.23f, 0), new Vector2(.17f, .18f), new Vector2(.13f, .43f) }, 11, 8);
        mesh.Gem(new Vector3(0, .35f, -.01f), new Vector3(.12f, .135f, .115f), 9, 8, 5);
        mesh.Lathe(new Vector3(0, .40f, .01f), new[] { new Vector2(.115f, 0), new Vector2(0, .20f) }, 9, 8);
        mesh.Box(new Vector3(0, .34f, -.112f), new Vector3(.135f, .035f, .025f), 11);
        mesh.Box(new Vector3(0, .345f, -.129f), new Vector3(.095f, .009f, .01f), 8);
        mesh.Box(new Vector3(0, .07f, -.145f), new Vector3(.035f, .29f, .025f), 4, Quaternion.Euler(0, 0, 28));
        for (int side = -1; side <= 1; side += 2)
        {
            mesh.Beam(new Vector3(side * .12f, .16f, -.02f), new Vector3(side * .25f, .06f, -.10f), .04f, 0, 6);
            mesh.Beam(new Vector3(side * .25f, -.035f, -.10f), new Vector3(side * .25f, .10f, -.10f), .017f, 5, 6);
            mesh.Blade(new Vector3(side * .25f, .10f, -.10f), .28f, .040f, 3, Quaternion.identity);
        }
    }
    static void Scout(ChessMesh mesh)
    {
        JoinedArms(mesh, .15f, new Vector3(-.25f, .10f, .03f), new Vector3(.235f, .27f, .01f), 7);
        Torso(mesh, .15f, 7);
        mesh.Lathe(new Vector3(0, -.265f, .035f), new[] { new Vector2(.22f, 0), new Vector2(.17f, .15f), new Vector2(.125f, .43f) }, 5, 8);
        mesh.Gem(new Vector3(0, .33f, -.005f), new Vector3(.115f, .13f, .11f), 5, 8, 5);
        mesh.Box(new Vector3(0, .33f, -.104f), new Vector3(.14f, .075f, .025f), 6);
        mesh.Box(new Vector3(0, .35f, -.12f), new Vector3(.10f, .012f, .012f), 11);
        mesh.Beam(new Vector3(.12f, .17f, 0), new Vector3(.24f, .27f, -.10f), .035f, 7, 6);
        mesh.Beam(new Vector3(.235f, .29f, .09f), new Vector3(.235f, .29f, -.25f), .045f, 5, 8);
        mesh.Gem(new Vector3(.235f, .29f, -.27f), new Vector3(.047f, .047f, .018f), 8, 8, 4);
        mesh.Beam(new Vector3(-.25f, -.24f, .03f), new Vector3(-.25f, .61f, .03f), .018f, 5, 6);
        mesh.Profile(new Vector3(-.25f, .42f, .03f), new[] { new Vector2(0, 0), new Vector2(0, .18f), new Vector2(-.16f, .14f), new Vector2(-.09f, .06f) }, .009f, 7, Quaternion.identity);
        mesh.Box(new Vector3(-.13f, -.05f, -.10f), new Vector3(.12f, .11f, .07f), 4);
    }
    static void Priest(ChessMesh mesh)
    {
        CarvedRobe(mesh, .22f, 1);
        JoinedArms(mesh, .145f, new Vector3(-.17f, .07f, -.16f), new Vector3(.26f, .12f, .01f), 1);
        mesh.Box(new Vector3(0, .01f, -.16f), new Vector3(.055f, .38f, .025f), 6);
        mesh.Gem(new Vector3(0, .34f, 0), new Vector3(.087f, .105f, .085f), 6, 8, 5);
        mesh.Profile(new Vector3(0, .47f, .005f), new[] {
            new Vector2(-.08f, -.08f), new Vector2(-.10f, .04f), new Vector2(0, .19f),
            new Vector2(.10f, .04f), new Vector2(.08f, -.08f)
        }, .065f, 6, Quaternion.identity);
        mesh.CarvedBox(new Vector3(0, .395f, .005f), new Vector3(.18f, .045f, .145f), 4);
        mesh.Box(new Vector3(0, .50f, -.063f), new Vector3(.024f, .16f, .015f), 4);
        mesh.Box(new Vector3(0, .53f, -.064f), new Vector3(.10f, .023f, .016f), 4);
        mesh.Beam(new Vector3(.26f, -.21f, .01f), new Vector3(.26f, .65f, .01f), .020f, 4, 8);
        mesh.Box(new Vector3(.26f, .55f, .01f), new Vector3(.20f, .035f, .04f), 4);
        mesh.Box(new Vector3(-.17f, .07f, -.16f), new Vector3(.17f, .12f, .035f), 6, Quaternion.Euler(12, 12, -15));
        mesh.Box(new Vector3(-.17f, .07f, -.183f), new Vector3(.018f, .10f, .012f), 5, Quaternion.Euler(12, 12, -15));
    }
    static void Guardian(ChessMesh mesh)
    {
        JoinedArms(mesh, .21f, new Vector3(-.11f, .06f, -.19f), new Vector3(.29f, .12f, -.02f), 1);
        Torso(mesh, .21f, 1);
        mesh.Gem(new Vector3(-.21f, .19f, 0), new Vector3(.10f, .105f, .14f), 3, 6, 4);
        mesh.Gem(new Vector3(.21f, .19f, 0), new Vector3(.10f, .105f, .14f), 3, 6, 4);
        mesh.CarvedBox(new Vector3(0, .37f, 0), new Vector3(.19f, .22f, .18f), 2);
        mesh.Box(new Vector3(0, .40f, -.097f), new Vector3(.14f, .018f, .024f), 11);
        mesh.CarvedBox(new Vector3(0, .46f, .005f), new Vector3(.22f, .035f, .20f), 4);
        mesh.Shield(new Vector3(-.09f, -.005f, -.24f), .21f, .29f, .035f, 4);
        mesh.Shield(new Vector3(-.09f, -.005f, -.28f), .175f, .25f, .014f, 1);
        mesh.Gem(new Vector3(-.09f, .02f, -.30f), new Vector3(.050f, .067f, .018f), 8, 6, 4);
        mesh.Beam(new Vector3(.29f, -.23f, -.02f), new Vector3(.29f, .47f, -.02f), .028f, 5, 7);
        mesh.Box(new Vector3(.29f, .45f, -.02f), new Vector3(.18f, .13f, .17f), 3);
        mesh.Box(new Vector3(.29f, .45f, -.02f), new Vector3(.055f, .17f, .20f), 4);
    }
    static void Crossbow(ChessMesh mesh)
    {
        JoinedArms(mesh, .18f, new Vector3(-.15f, .17f, -.13f), new Vector3(.08f, .14f, .015f), 1);
        Torso(mesh, .18f, 1);
        mesh.Gem(new Vector3(0, .36f, 0), new Vector3(.11f, .13f, .10f), 3, 8, 5);
        mesh.Box(new Vector3(0, .36f, -.098f), new Vector3(.15f, .025f, .020f), 11);
        mesh.CarvedBox(new Vector3(0, .49f, .015f), new Vector3(.23f, .03f, .18f), 2);
        mesh.Box(new Vector3(0, .17f, -.10f), new Vector3(.07f, .07f, .46f), 5);
        mesh.Beam(new Vector3(-.32f, .18f, -.12f), new Vector3(-.15f, .18f, -.23f), .028f, 4, 6);
        mesh.Beam(new Vector3(-.15f, .18f, -.23f), new Vector3(0, .18f, -.24f), .028f, 5, 6);
        mesh.Beam(new Vector3(.32f, .18f, -.12f), new Vector3(.15f, .18f, -.23f), .028f, 4, 6);
        mesh.Beam(new Vector3(.15f, .18f, -.23f), new Vector3(0, .18f, -.24f), .028f, 5, 6);
        mesh.Beam(new Vector3(-.32f, .18f, -.12f), new Vector3(0, .18f, .03f), .006f, 6, 4);
        mesh.Beam(new Vector3(.32f, .18f, -.12f), new Vector3(0, .18f, .03f), .006f, 6, 4);
        mesh.Beam(new Vector3(0, .215f, -.05f), new Vector3(0, .215f, -.40f), .01f, 3, 5);
        mesh.Gem(new Vector3(0, .215f, -.40f), new Vector3(.023f, .023f, .045f), 8, 4, 4);
        mesh.Box(new Vector3(-.17f, -.06f, .04f), new Vector3(.10f, .22f, .12f), 4);
    }
    static void MagicSniper(ChessMesh mesh)
    {
        JoinedArms(mesh, .16f, new Vector3(.13f, .12f, -.22f), new Vector3(.13f, .09f, .08f), 0);
        Torso(mesh, .16f, 0);
        mesh.Lathe(new Vector3(0, -.26f, .025f), new[] { new Vector2(.22f, 0), new Vector2(.16f, .18f), new Vector2(.125f, .43f) }, 1, 8);
        mesh.Gem(new Vector3(0, .35f, 0), new Vector3(.12f, .14f, .115f), 0, 8, 5);
        mesh.Box(new Vector3(0, .35f, -.111f), new Vector3(.14f, .025f, .022f), 8);
        mesh.Box(new Vector3(.13f, .11f, -.02f), new Vector3(.10f, .09f, .39f), 5);
        mesh.Beam(new Vector3(.13f, .13f, .02f), new Vector3(.13f, .13f, -.42f), .028f, 2, 8);
        mesh.Gem(new Vector3(.13f, .13f, -.43f), new Vector3(.034f, .034f, .027f), 8, 8, 4);
        mesh.Beam(new Vector3(.13f, .235f, .01f), new Vector3(.13f, .235f, -.20f), .034f, 4, 8);
        mesh.Gem(new Vector3(.13f, .235f, -.22f), new Vector3(.035f, .035f, .02f), 8, 8, 4);
        mesh.Box(new Vector3(-.13f, -.04f, -.13f), new Vector3(.11f, .14f, .055f), 7);
    }
    static void Bomber(ChessMesh mesh)
    {
        JoinedArms(mesh, .19f, new Vector3(-.20f, -.01f, -.07f), new Vector3(.25f, .16f, -.06f), 9);
        Torso(mesh, .19f, 9);
        mesh.Gem(new Vector3(0, .36f, 0), new Vector3(.11f, .12f, .10f), 5, 8, 5);
        mesh.CarvedBox(new Vector3(0, .47f, .015f), new Vector3(.20f, .055f, .18f), 0);
        mesh.VerticalRing(new Vector3(-.052f, .39f, -.098f), .044f, .025f, .025f, 4, 8);
        mesh.VerticalRing(new Vector3(.052f, .39f, -.098f), .044f, .025f, .025f, 4, 8);
        mesh.Gem(new Vector3(-.052f, .39f, -.112f), new Vector3(.025f, .025f, .013f), 8, 6, 4);
        mesh.Gem(new Vector3(.052f, .39f, -.112f), new Vector3(.025f, .025f, .013f), 8, 6, 4);
        mesh.Box(new Vector3(0, .06f, .15f), new Vector3(.25f, .30f, .13f), 5);
        for (int i = 0; i < 3; i++)
        {
            var p = new Vector3(-.12f + i * .12f, -.02f, -.16f);
            mesh.Gem(p, new Vector3(.055f, .065f, .055f), 0, 6, 4); mesh.Beam(p + Vector3.up * .055f, p + Vector3.up * .105f, .011f, 4, 4);
        }
        mesh.Beam(new Vector3(.14f, .16f, -.02f), new Vector3(.25f, .16f, -.06f), .035f, 9, 6);
        mesh.Gem(new Vector3(.28f, .23f, -.07f), new Vector3(.10f, .115f, .10f), 0, 8, 6);
        mesh.Beam(new Vector3(.28f, .33f, -.07f), new Vector3(.25f, .40f, -.07f), .013f, 4, 5);
        mesh.Gem(new Vector3(.25f, .405f, -.07f), new Vector3(.025f, .030f, .025f), 10, 4, 4);
    }
    static void Torso(ChessMesh mesh, float width, int color)
    {
        width *= 1.12f;
        mesh.EllipticLathe(new Vector3(0, -.26f, .035f), new[] {
            new Vector2(width * .98f, 0), new Vector2(width, .045f), new Vector2(width * .84f, .19f),
            new Vector2(width * .96f, .30f), new Vector2(width * 1.08f, .405f), new Vector2(width * .70f, .48f)
        }, .91f, color, 10);
        mesh.EllipticLathe(new Vector3(0, -.12f, .035f), new[] { new Vector2(width * .95f, 0), new Vector2(width * .95f, .045f) }, .92f, 4, 8);
        mesh.EllipticLathe(new Vector3(0, .20f, 0), new[] { new Vector2(width * .62f, 0), new Vector2(width * .45f, .045f), new Vector2(width * .44f, .075f) }, .86f, 3, 8);
    }
    static void WhiteMage(ChessMesh mesh)
    {
        JoinedArms(mesh, .14f, new Vector3(-.19f, .025f, -.12f), new Vector3(.27f, .12f, 0), 6);
        CarvedRobe(mesh, .22f, 6);
        mesh.Lathe(new Vector3(0, -.23f, 0), new[] { new Vector2(.215f, 0), new Vector2(.207f, .025f) }, 4, 10);
        mesh.Box(new Vector3(0, .05f, -.145f), new Vector3(.052f, .42f, .025f), 7);
        mesh.Gem(new Vector3(0, .34f, 0), new Vector3(.09f, .115f, .082f), 6, 8, 5);
        mesh.Lathe(new Vector3(0, .35f, 0), new[] { new Vector2(.135f, 0), new Vector2(.105f, .14f), new Vector2(0, .28f) }, 6, 8);
        mesh.Box(new Vector3(0, .42f, -.102f), new Vector3(.12f, .04f, .019f), 4);
        mesh.Beam(new Vector3(.27f, -.22f, 0), new Vector3(.27f, .64f, 0), .022f, 4, 8);
        mesh.VerticalRing(new Vector3(.27f, .62f, 0), .10f, .077f, .025f, 4, 12);
        mesh.Gem(new Vector3(.27f, .62f, -.005f), new Vector3(.061f, .070f, .045f), 8, 8, 5);
        mesh.Beam(new Vector3(-.115f, .16f, 0), new Vector3(-.20f, -.01f, -.06f), .047f, 6, 6);
        mesh.Box(new Vector3(-.19f, .025f, -.12f), new Vector3(.11f, .12f, .038f), 7, Quaternion.Euler(0, 10, -10));
        mesh.Box(new Vector3(-.19f, .025f, -.144f), new Vector3(.018f, .09f, .01f), 4, Quaternion.Euler(0, 10, -10));
    }
    static void HolyKnight(ChessMesh mesh)
    {
        JoinedArms(mesh, .175f, new Vector3(-.14f, .035f, -.18f), new Vector3(.285f, -.04f, -.055f), 3);
        Torso(mesh, .175f, 3);
        mesh.CarvedBox(new Vector3(0, .10f, -.13f), new Vector3(.20f, .23f, .055f), 6);
        mesh.Box(new Vector3(0, .12f, -.165f), new Vector3(.04f, .16f, .018f), 4);
        mesh.Box(new Vector3(0, .15f, -.166f), new Vector3(.14f, .04f, .02f), 4);
        mesh.Gem(new Vector3(0, .38f, 0), new Vector3(.105f, .13f, .10f), 3, 8, 5);
        mesh.Box(new Vector3(0, .38f, -.096f), new Vector3(.15f, .025f, .025f), 11);
        mesh.Box(new Vector3(0, .51f, .015f), new Vector3(.045f, .11f, .14f), 4);
        mesh.Gem(new Vector3(-.17f, .20f, 0), new Vector3(.095f, .09f, .12f), 4, 6, 4);
        mesh.Gem(new Vector3(.17f, .20f, 0), new Vector3(.095f, .09f, .12f), 4, 6, 4);
        mesh.VerticalRing(new Vector3(0, .42f, .13f), .20f, .177f, .025f, 4, 16);
        mesh.Shield(new Vector3(-.14f, .015f, -.215f), .15f, .245f, .035f, 6);
        mesh.Box(new Vector3(-.14f, .04f, -.252f), new Vector3(.035f, .28f, .018f), 4);
        mesh.Box(new Vector3(-.14f, .09f, -.253f), new Vector3(.18f, .035f, .019f), 4);
        Sword(mesh, new Vector3(.285f, -.15f, -.055f), .63f, 0);
    }
    static void Samurai(ChessMesh mesh)
    {
        JoinedArms(mesh, .17f, new Vector3(-.22f, .03f, .04f), new Vector3(.29f, -.06f, -.07f), 9);
        Torso(mesh, .17f, 9);
        for (int i = 0; i < 4; i++)
        {
            float y = -.19f + i * .09f;
            mesh.Box(new Vector3(0, y, -.132f), new Vector3(.27f - i * .015f, .055f, .065f), i % 2 == 0 ? 0 : 9);
            mesh.Box(new Vector3(0, y + .027f, -.168f), new Vector3(.255f - i * .015f, .012f, .012f), 4);
        }
        mesh.Gem(new Vector3(-.18f, .17f, 0), new Vector3(.075f, .065f, .13f), 0, 6, 4);
        mesh.Gem(new Vector3(.18f, .17f, 0), new Vector3(.075f, .065f, .13f), 0, 6, 4);
        mesh.Gem(new Vector3(0, .35f, 0), new Vector3(.09f, .10f, .09f), 3, 8, 5);
        mesh.Lathe(new Vector3(0, .38f, 0), new[] { new Vector2(.18f, 0), new Vector2(.16f, .035f), new Vector2(.105f, .13f), new Vector2(.02f, .17f) }, 0, 10);
        mesh.Box(new Vector3(0, .33f, -.095f), new Vector3(.13f, .035f, .025f), 9);
        mesh.Beam(new Vector3(-.035f, .47f, -.105f), new Vector3(-.20f, .57f, -.09f), .026f, 4, 5);
        mesh.Beam(new Vector3(-.20f, .57f, -.09f), new Vector3(-.25f, .72f, -.06f), .018f, 4, 5);
        mesh.Beam(new Vector3(.035f, .47f, -.105f), new Vector3(.20f, .57f, -.09f), .026f, 4, 5);
        mesh.Beam(new Vector3(.20f, .57f, -.09f), new Vector3(.25f, .72f, -.06f), .018f, 4, 5);
        Sword(mesh, new Vector3(.29f, -.20f, -.07f), .69f, -12);
        mesh.Beam(new Vector3(-.22f, -.15f, .04f), new Vector3(-.26f, .21f, .04f), .025f, 11, 6);
    }
    static void Ninja(ChessMesh mesh)
    {
        JoinedArms(mesh, .155f, new Vector3(-.24f, .04f, -.10f), new Vector3(.25f, .015f, -.08f), 11);
        Torso(mesh, .155f, 11);
        mesh.Lathe(new Vector3(0, -.265f, .025f), new[] { new Vector2(.23f, 0), new Vector2(.17f, .19f), new Vector2(.14f, .43f) }, 0, 8);
        mesh.Gem(new Vector3(0, .365f, -.005f), new Vector3(.12f, .145f, .115f), 11, 8, 6);
        mesh.Box(new Vector3(0, .37f, -.112f), new Vector3(.15f, .035f, .019f), 6);
        mesh.Box(new Vector3(-.038f, .37f, -.126f), new Vector3(.027f, .011f, .008f), 8);
        mesh.Box(new Vector3(.038f, .37f, -.126f), new Vector3(.027f, .011f, .008f), 8);
        mesh.Box(new Vector3(0, .44f, -.091f), new Vector3(.20f, .04f, .043f), 9);
        mesh.Box(new Vector3(0, .44f, -.116f), new Vector3(.065f, .031f, .011f), 3);
        mesh.Box(new Vector3(.15f, .34f, .07f), new Vector3(.27f, .045f, .07f), 9, Quaternion.Euler(0, 15, -20));
        mesh.Beam(new Vector3(.13f, .16f, -.02f), new Vector3(.25f, .015f, -.08f), .04f, 0, 6);
        mesh.Beam(new Vector3(.265f, -.12f, -.09f), new Vector3(.265f, .07f, -.09f), .017f, 4, 6);
        mesh.DiamondBlade(new Vector3(.265f, -.23f, -.09f), .043f, .16f, 3);
        mesh.Beam(new Vector3(-.13f, .16f, -.02f), new Vector3(-.24f, .04f, -.10f), .04f, 0, 6);
        mesh.DiamondBlade(new Vector3(-.25f, -.055f, -.115f), .04f, .18f, 3);
        mesh.Beam(new Vector3(-.16f, -.10f, .12f), new Vector3(.16f, .40f, .12f), .021f, 3, 5);
    }
    static void Berserker(ChessMesh mesh)
    {
        JoinedArms(mesh, .20f, new Vector3(-.29f, .11f, -.025f), new Vector3(.29f, .11f, -.025f), 9);
        Torso(mesh, .20f, 9);
        mesh.CarvedBox(new Vector3(0, .08f, -.152f), new Vector3(.23f, .20f, .05f), 0);
        mesh.Box(new Vector3(0, .07f, -.185f), new Vector3(.045f, .22f, .018f), 4, Quaternion.Euler(0, 0, -20));
        mesh.Gem(new Vector3(-.21f, .18f, 0), new Vector3(.105f, .11f, .13f), 1, 6, 4);
        mesh.Gem(new Vector3(.21f, .18f, 0), new Vector3(.105f, .11f, .13f), 1, 6, 4);
        mesh.Gem(new Vector3(0, .37f, 0), new Vector3(.105f, .11f, .095f), 2, 8, 5);
        mesh.Box(new Vector3(0, .37f, -.098f), new Vector3(.13f, .025f, .02f), 10);
        mesh.Beam(new Vector3(-.075f, .43f, .025f), new Vector3(-.16f, .53f, .025f), .026f, 6, 5);
        mesh.Beam(new Vector3(-.16f, .53f, .025f), new Vector3(-.14f, .66f, .025f), .015f, 6, 5);
        mesh.Beam(new Vector3(.075f, .43f, .025f), new Vector3(.16f, .53f, .025f), .026f, 6, 5);
        mesh.Beam(new Vector3(.16f, .53f, .025f), new Vector3(.14f, .66f, .025f), .015f, 6, 5);
        Axe(mesh, -.29f); Axe(mesh, .29f);
    }
    static void Navigator(ChessMesh mesh)
    {
        JoinedArms(mesh, .18f, new Vector3(-.29f, .12f, -.07f), new Vector3(.26f, .05f, -.04f), 7);
        Torso(mesh, .18f, 7);
        mesh.CarvedBox(new Vector3(0, .08f, -.143f), new Vector3(.22f, .28f, .028f), 0);
        for (int i = 0; i < 3; i++) mesh.Gem(new Vector3(0, -.02f + i * .075f, -.166f), new Vector3(.015f, .015f, .013f), 4, 4, 4);
        mesh.Gem(new Vector3(0, .36f, 0), new Vector3(.10f, .11f, .095f), 6, 8, 5);
        mesh.Box(new Vector3(0, .38f, -.10f), new Vector3(.14f, .025f, .024f), 11);
        mesh.Lathe(new Vector3(0, .44f, 0), new[] { new Vector2(.19f, 0), new Vector2(.19f, .025f), new Vector2(.10f, .075f), new Vector2(.08f, .12f) }, 0, 3);
        mesh.Beam(new Vector3(-.155f, .454f, -.035f), new Vector3(.155f, .454f, -.035f), .015f, 4, 5);
        mesh.VerticalRing(new Vector3(0, .15f, .16f), .24f, .208f, .035f, 4, 16);
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.PI / 3;
            var radial = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
            mesh.Beam(new Vector3(0, .15f, .16f), new Vector3(0, .15f, .16f) + radial * .28f, .013f, 4, 5);
        }
        mesh.Gem(new Vector3(0, .15f, .16f), new Vector3(.044f, .044f, .03f), 5, 6, 4);
        // The anchor is a closed, solid silhouette rather than a transparent effect or runtime line.
        mesh.Beam(new Vector3(-.29f, -.17f, -.07f), new Vector3(-.29f, .43f, -.07f), .024f, 3, 7);
        mesh.VerticalRing(new Vector3(-.29f, .45f, -.07f), .053f, .030f, .022f, 4, 10);
        mesh.Beam(new Vector3(-.39f, .28f, -.07f), new Vector3(-.19f, .28f, -.07f), .020f, 3, 6);
        mesh.Beam(new Vector3(-.29f, -.17f, -.07f), new Vector3(-.40f, -.09f, -.07f), .026f, 3, 6);
        mesh.Beam(new Vector3(-.29f, -.17f, -.07f), new Vector3(-.18f, -.09f, -.07f), .026f, 3, 6);
        mesh.Beam(new Vector3(-.40f, -.09f, -.07f), new Vector3(-.40f, -.015f, -.07f), .020f, 3, 6);
        mesh.Beam(new Vector3(-.18f, -.09f, -.07f), new Vector3(-.18f, -.015f, -.07f), .020f, 3, 6);
        mesh.Beam(new Vector3(.15f, .15f, -.02f), new Vector3(.27f, .05f, -.10f), .047f, 7, 6);
        mesh.Beam(new Vector3(.26f, .05f, -.17f), new Vector3(.26f, .05f, .09f), .037f, 5, 8);
    }
    static void HeavyWarrior(ChessMesh mesh)
    {
        // Broad closed armour and a square maul remain legible with the shared white material.
        JoinedArms(mesh, .22f, new Vector3(-.27f, .04f, -.13f), new Vector3(.29f, .10f, -.04f), 1);
        Torso(mesh, .22f, 1);
        mesh.CarvedBox(new Vector3(0, .09f, -.17f), new Vector3(.29f, .29f, .075f), 3);
        mesh.Box(new Vector3(0, .09f, -.218f), new Vector3(.045f, .27f, .022f), 4);
        mesh.Gem(new Vector3(0, .36f, 0), new Vector3(.125f, .14f, .12f), 2, 8, 5);
        mesh.CarvedBox(new Vector3(0, .38f, -.105f), new Vector3(.18f, .10f, .055f), 3);
        mesh.Box(new Vector3(0, .395f, -.139f), new Vector3(.13f, .018f, .012f), 11);
        mesh.Box(new Vector3(0, .505f, 0), new Vector3(.065f, .12f, .17f), 4);
        mesh.Shield(new Vector3(-.265f, .015f, -.17f), .13f, .25f, .055f, 3);
        mesh.Beam(new Vector3(.29f, -.22f, -.04f), new Vector3(.29f, .60f, -.04f), .034f, 5, 7);
        mesh.CarvedBox(new Vector3(.29f, .52f, -.04f), new Vector3(.23f, .20f, .18f), 3);
        mesh.Box(new Vector3(.29f, .52f, -.137f), new Vector3(.17f, .12f, .025f), 4);
    }
    static void DragonKnight(ChessMesh mesh)
    {
        JoinedArms(mesh, .18f, new Vector3(-.24f, .07f, -.13f), new Vector3(.28f, .10f, -.03f), 1);
        Torso(mesh, .18f, 1);
        // Folded, solid dragon wings stay inside the selection footprint and need no animation.
        for (int side = -1; side <= 1; side += 2)
        {
            mesh.Profile(new Vector3(side * .11f, -.05f, .14f), new[] {
                new Vector2(0, 0), new Vector2(side * .21f, .03f), new Vector2(side * .26f, .30f),
                new Vector2(side * .15f, .47f), new Vector2(side * .075f, .26f) }, .045f, 2, Quaternion.identity);
            mesh.Beam(new Vector3(side * .10f, .12f, .14f), new Vector3(side * .26f, .42f, .14f), .024f, 4, 5);
            mesh.Beam(new Vector3(side * .06f, .44f, .015f), new Vector3(side * .14f, .61f, .055f), .025f, 4, 5);
        }
        mesh.Gem(new Vector3(0, .35f, 0), new Vector3(.11f, .14f, .10f), 2, 8, 5);
        mesh.CarvedBox(new Vector3(0, .34f, -.105f), new Vector3(.14f, .08f, .14f), 3);
        mesh.Box(new Vector3(0, .38f, -.178f), new Vector3(.11f, .02f, .015f), 11);
        mesh.Shield(new Vector3(-.235f, .015f, -.18f), .11f, .21f, .04f, 3);
        mesh.Beam(new Vector3(.28f, -.23f, -.03f), new Vector3(.28f, .61f, -.03f), .023f, 5, 7);
        mesh.DiamondBlade(new Vector3(.28f, .58f, -.03f), .055f, .23f, 3);
        mesh.Box(new Vector3(.28f, .59f, -.03f), new Vector3(.16f, .025f, .04f), 4);
    }
    static void CarvedRobe(ChessMesh mesh, float width, int color)
    {
        width *= 1.08f;
        mesh.EllipticLathe(new Vector3(0, -.265f, .025f), new[] {
            new Vector2(width, 0), new Vector2(width * .95f, .045f), new Vector2(width * .72f, .19f),
            new Vector2(width * .58f, .33f), new Vector2(width * .80f, .44f), new Vector2(width * .45f, .50f)
        }, .91f, color, 12);
        // Raised folds are part of the mesh, so a single white material retains fabric depth.
        for (int side = -1; side <= 1; side += 2)
            mesh.Beam(new Vector3(side * width * .52f, -.22f, -.11f), new Vector3(side * width * .22f, .105f, -.078f), .018f, color, 5);
        mesh.EllipticLathe(new Vector3(0, .20f, 0), new[] { new Vector2(.09f, 0), new Vector2(.065f, .075f) }, .85f, 4, 8);
    }
    static void JoinedArms(ChessMesh mesh, float shoulderWidth, Vector3 leftHand, Vector3 rightHand, int color)
    {
        for (int side = -1; side <= 1; side += 2)
        {
            var hand = side < 0 ? leftHand : rightHand;
            var shoulder = new Vector3(side * shoulderWidth * 1.01f, .175f, .005f);
            var elbow = Vector3.Lerp(shoulder, hand, .52f) + new Vector3(side * .025f, -.035f, .018f);
            mesh.Beam(shoulder, elbow, .064f, color, 5);
            mesh.Beam(elbow, hand, .049f, color, 5);
            mesh.Gem(hand, new Vector3(.053f, .050f, .055f), 3, 5, 3);
            // Broad bevels, rather than small surface decorations, survive the white-marble map lighting.
            mesh.CarvedBox(shoulder + new Vector3(side * .025f, .005f, .005f), new Vector3(.135f, .12f, .19f), color,
                Quaternion.Euler(0, side * 8, side * -12));
            mesh.CarvedBox(Vector3.Lerp(elbow, hand, .52f), new Vector3(.105f, .125f, .11f), 3,
                Quaternion.FromToRotation(Vector3.up, (hand - elbow).normalized));
        }
    }
    static void Sword(ChessMesh mesh, Vector3 origin, float height, float angle)
    {
        var rotation = Quaternion.Euler(0, 0, angle);
        mesh.Box(origin + rotation * new Vector3(0, .08f, 0), new Vector3(.045f, .16f, .045f), 5, rotation);
        mesh.Box(origin + rotation * new Vector3(0, .16f, 0), new Vector3(.18f, .035f, .065f), 4, rotation);
        mesh.Blade(origin + rotation * new Vector3(0, .17f, 0), height - .17f, .055f, 3, rotation);
    }
    static void Axe(ChessMesh mesh, float x)
    {
        mesh.Beam(new Vector3(x, -.20f, -.025f), new Vector3(x, .50f, -.025f), .029f, 5, 7);
        mesh.Box(new Vector3(x, .39f, -.025f), new Vector3(.185f, .17f, .085f), 3, Quaternion.Euler(0, 0, x < 0 ? -12 : 12));
        mesh.Blade(new Vector3(x + (x < 0 ? -.09f : .09f), .31f, -.025f), .22f, .068f, 3, Quaternion.Euler(0, 0, x < 0 ? -15 : 15));
        mesh.Gem(new Vector3(x, .50f, -.025f), new Vector3(.035f, .05f, .035f), 4, 5, 4);
    }

    sealed class ChessMesh
    {
        readonly List<Vector3> vertices = new List<Vector3>(); readonly List<Vector2> uvs = new List<Vector2>(); readonly List<int> triangles = new List<int>();
        void Tri(Vector3 a, Vector3 b, Vector3 c, int color)
        {
            int i = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c);
            var uv = new Vector2((color + .5f) / Palette.Length, .5f); uvs.Add(uv); uvs.Add(uv); uvs.Add(uv);
            triangles.Add(i); triangles.Add(i + 1); triangles.Add(i + 2);
        }
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int color) { Tri(a, b, c, color); Tri(a, c, d, color); }
        public void LowerStance(float heightScale)
        {
            for (int i = 0; i < vertices.Count; i++)
            {
                var p = vertices[i];
                if (p.y > -.26f) p.y = -.26f + (p.y + .26f) * heightScale;
                vertices[i] = p;
            }
        }
        public void Lathe(Vector3 origin, Vector2[] profile, int color, int sides)
        {
            for (int j = 0; j < profile.Length - 1; j++) for (int i = 0; i < sides; i++)
            {
                float a = (i + .5f) * Mathf.PI * 2 / sides, b = (i + 1.5f) * Mathf.PI * 2 / sides;
                Quad(Radial(origin, profile[j], a), Radial(origin, profile[j + 1], a), Radial(origin, profile[j + 1], b), Radial(origin, profile[j], b), color);
            }
            for (int i = 0; i < sides; i++)
            {
                float a = (i + .5f) * Mathf.PI * 2 / sides, b = (i + 1.5f) * Mathf.PI * 2 / sides;
                Tri(origin + Vector3.up * profile[0].y, Radial(origin, profile[0], a), Radial(origin, profile[0], b), color);
                int last = profile.Length - 1;
                Tri(origin + Vector3.up * profile[last].y, Radial(origin, profile[last], b), Radial(origin, profile[last], a), color);
            }
        }
        static Vector3 Radial(Vector3 origin, Vector2 p, float angle) => origin + new Vector3(Mathf.Cos(angle) * p.x, p.y, Mathf.Sin(angle) * p.x);
        public void EllipticLathe(Vector3 origin, Vector2[] profile, float depthScale, int color, int sides)
        {
            int start = vertices.Count;
            Lathe(origin, profile, color, sides);
            for (int i = start; i < vertices.Count; i++)
            {
                var p = vertices[i]; p.z = origin.z + (p.z - origin.z) * depthScale; vertices[i] = p;
            }
        }
        public void CarvedBox(Vector3 center, Vector3 size, int color) => CarvedBox(center, size, color, Quaternion.identity);
        public void CarvedBox(Vector3 center, Vector3 size, int color, Quaternion rotation)
        {
            // Eight-sided cross-section keeps broad stone faces and a small bevel for edge light.
            var outline = new[] { new Vector2(-.32f, -.5f), new Vector2(.32f, -.5f), new Vector2(.5f, -.32f),
                new Vector2(.5f, .32f), new Vector2(.32f, .5f), new Vector2(-.32f, .5f),
                new Vector2(-.5f, .32f), new Vector2(-.5f, -.32f) };
            var bottom = new Vector3[8]; var top = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                bottom[i] = center + rotation * new Vector3(outline[i].x * size.x, -size.y * .5f, outline[i].y * size.z);
                top[i] = center + rotation * new Vector3(outline[i].x * size.x, size.y * .5f, outline[i].y * size.z);
            }
            for (int i = 0; i < 8; i++) Quad(bottom[i], top[i], top[(i + 1) % 8], bottom[(i + 1) % 8], color);
            for (int i = 1; i < 7; i++) { Tri(bottom[0], bottom[i], bottom[i + 1], color); Tri(top[0], top[i + 1], top[i], color); }
        }
        public void Box(Vector3 center, Vector3 size, int color) => Box(center, size, color, Quaternion.identity);
        public void Box(Vector3 center, Vector3 size, int color, Quaternion rotation)
        {
            var p = new Vector3[8];
            for (int i = 0; i < 8; i++) p[i] = center + rotation * Vector3.Scale(size * .5f, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            Quad(p[0], p[2], p[3], p[1], color); Quad(p[4], p[5], p[7], p[6], color);
            Quad(p[0], p[4], p[6], p[2], color); Quad(p[1], p[3], p[7], p[5], color);
            Quad(p[2], p[6], p[7], p[3], color); Quad(p[0], p[1], p[5], p[4], color);
        }
        public void Beam(Vector3 from, Vector3 to, float radius, int color, int sides)
        {
            if (radius >= .017f) radius *= 1.14f;
            var direction = to - from; var rotation = Quaternion.FromToRotation(Vector3.up, direction.normalized);
            var side = rotation * Vector3.right; var forward = rotation * Vector3.forward;
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides, b = (i + 1) * Mathf.PI * 2 / sides;
                var pa = (Mathf.Cos(a) * side + Mathf.Sin(a) * forward) * radius; var pb = (Mathf.Cos(b) * side + Mathf.Sin(b) * forward) * radius;
                Quad(from + pa, to + pa, to + pb, from + pb, color); Tri(from, from + pa, from + pb, color); Tri(to, to + pb, to + pa, color);
            }
        }
        public void Gem(Vector3 center, Vector3 size, int color, int sides, int rings)
        {
            for (int ring = 0; ring < rings; ring++) for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides, b = (i + 1) * Mathf.PI * 2 / sides;
                float low = -Mathf.PI * .5f + ring * Mathf.PI / rings, high = low + Mathf.PI / rings;
                Quad(GemPoint(center, size, a, low), GemPoint(center, size, a, high), GemPoint(center, size, b, high), GemPoint(center, size, b, low), color);
            }
        }
        static Vector3 GemPoint(Vector3 c, Vector3 size, float a, float y) => c + Vector3.Scale(size, new Vector3(Mathf.Cos(a) * Mathf.Cos(y), Mathf.Sin(y), Mathf.Sin(a) * Mathf.Cos(y)));
        public void VerticalRing(Vector3 center, float outer, float inner, float thickness, int color, int sides)
        {
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides, b = (i + 1) * Mathf.PI * 2 / sides;
                var oa = new Vector3(Mathf.Cos(a) * outer, Mathf.Sin(a) * outer, 0); var ob = new Vector3(Mathf.Cos(b) * outer, Mathf.Sin(b) * outer, 0);
                var ia = oa * (inner / outer); var ib = ob * (inner / outer); var z = Vector3.forward * thickness * .5f;
                Quad(center + oa - z, center + ia - z, center + ib - z, center + ob - z, color);
                Quad(center + oa + z, center + ob + z, center + ib + z, center + ia + z, color);
                Quad(center + oa - z, center + ob - z, center + ob + z, center + oa + z, color);
                Quad(center + ia + z, center + ib + z, center + ib - z, center + ia - z, color);
            }
        }
        public void Shield(Vector3 center, float halfWidth, float halfHeight, float depth, int color)
        {
            depth *= 1.4f;
            var outline = new[] { new Vector2(0, -halfHeight), new Vector2(-halfWidth, halfHeight * .25f), new Vector2(-halfWidth * .8f, halfHeight), new Vector2(halfWidth * .8f, halfHeight), new Vector2(halfWidth, halfHeight * .25f) };
            for (int i = 0; i < outline.Length; i++)
            {
                var a = center + new Vector3(outline[i].x, outline[i].y, -depth); var b = center + new Vector3(outline[(i + 1) % outline.Length].x, outline[(i + 1) % outline.Length].y, -depth);
                Tri(center - Vector3.forward * depth, a, b, color); Tri(center + Vector3.forward * depth, b + Vector3.forward * depth * 2, a + Vector3.forward * depth * 2, color);
                Quad(a, a + Vector3.forward * depth * 2, b + Vector3.forward * depth * 2, b, 4);
            }
        }
        public void Profile(Vector3 origin, Vector2[] outline, float depth, int color, Quaternion rotation)
        {
            float signedArea = 0;
            for (int i = 0; i < outline.Length; i++) signedArea += outline[i].x * outline[(i + 1) % outline.Length].y - outline[(i + 1) % outline.Length].x * outline[i].y;
            float orientation = signedArea > 0 ? 1 : -1;
            var remaining = new List<int>(); for (int i = 0; i < outline.Length; i++) remaining.Add(i);
            var faces = new List<int>(); int budget = outline.Length * outline.Length;
            while (remaining.Count > 3 && budget-- > 0)
            {
                bool clipped = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    int a = remaining[(i + remaining.Count - 1) % remaining.Count], b = remaining[i], c = remaining[(i + 1) % remaining.Count];
                    if (Cross(outline[a], outline[b], outline[c]) * orientation <= .000001f) continue;
                    bool contains = false;
                    foreach (int p in remaining)
                    {
                        if (p == a || p == b || p == c) continue;
                        if (Cross(outline[a], outline[b], outline[p]) * orientation >= -.000001f
                            && Cross(outline[b], outline[c], outline[p]) * orientation >= -.000001f
                            && Cross(outline[c], outline[a], outline[p]) * orientation >= -.000001f) { contains = true; break; }
                    }
                    if (contains) continue;
                    faces.Add(a); faces.Add(b); faces.Add(c); remaining.RemoveAt(i); clipped = true; break;
                }
                if (!clipped) throw new System.InvalidOperationException("駒のシルエットを三角形化できません。");
            }
            if (remaining.Count != 3) throw new System.InvalidOperationException("駒のシルエットの三角形化が予算を超えました。");
            faces.AddRange(remaining);
            for (int i = 0; i < faces.Count; i += 3)
            {
                var a = outline[faces[i]]; var b = outline[faces[i + 1]]; var c = outline[faces[i + 2]];
                var af = origin + rotation * new Vector3(a.x, a.y, -depth); var bf = origin + rotation * new Vector3(b.x, b.y, -depth); var cf = origin + rotation * new Vector3(c.x, c.y, -depth);
                var offset = rotation * Vector3.forward * depth * 2;
                if (orientation > 0) { Tri(af, cf, bf, color); Tri(af + offset, bf + offset, cf + offset, color); }
                else { Tri(af, bf, cf, color); Tri(af + offset, cf + offset, bf + offset, color); }
            }
            for (int i = 0; i < outline.Length; i++)
            {
                var a = origin + rotation * new Vector3(outline[i].x, outline[i].y, -depth);
                var b = origin + rotation * new Vector3(outline[(i + 1) % outline.Length].x, outline[(i + 1) % outline.Length].y, -depth);
                var offset = rotation * Vector3.forward * depth * 2;
                if (orientation > 0) Quad(b, b + offset, a + offset, a, color); else Quad(a, a + offset, b + offset, b, color);
            }
        }
        static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        public void Blade(Vector3 origin, float height, float width, int color, Quaternion rotation)
        {
            var a = origin + rotation * new Vector3(-width, 0, 0); var b = origin + rotation * new Vector3(width, 0, 0);
            var ridge = origin + rotation * new Vector3(0, height * .80f, -.030f); var back = origin + rotation * new Vector3(0, height * .80f, .030f);
            var top = origin + rotation * new Vector3(0, height, 0);
            Tri(a, ridge, b, color); Tri(a, top, ridge, color); Tri(b, ridge, top, color);
            Tri(b, back, a, color); Tri(a, back, top, color); Tri(b, top, back, color);
        }
        public void DiamondBlade(Vector3 center, float halfWidth, float height, int color)
        {
            Blade(center - Vector3.up * height * .5f, height, halfWidth, color, Quaternion.identity);
        }
        public Mesh Build(string name)
        {
            var mesh = new Mesh { name = name }; mesh.SetVertices(vertices); mesh.SetUVs(0, uvs); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
    }
}
#endif
