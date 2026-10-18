#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

public static partial class DarkEnemyVisualInstaller
{
    static Sculpture MakeEnemy(Kind kind)
    {
        string source;
        switch (kind)
        {
            case Kind.King: case Kind.Priest: case Kind.WhiteMage: case Kind.HolyKnight: source = "Ram"; break;
            case Kind.Archer: case Kind.Magic: case Kind.Magicsniper: case Kind.Navigator: source = "Koi"; break;
            case Kind.Guardian: case Kind.Crossbow: case Kind.Bomber: case Kind.Samurai: case Kind.Berserker: source = "Crab"; break;
            default: source = "Hound"; break;
        }
        var s = NewSculpture(source);
        switch (kind)
        {
            case Kind.King:
                Crown(s, .60f, .21f, 7); Antlers(s, .40f); s.Gem(new Vector3(0, .58f, -.13f), new Vector3(.045f, .065f, .035f), 10); break;
            case Kind.Knight:
                Spine(s, 4); Sword(s, new Vector3(.29f, -.20f, -.08f), .86f, .055f, 9);
                ShoulderArmor(s, .13f, .11f); Shield(s, new Vector3(-.23f, .10f, -.14f), .12f, .21f); break;
            case Kind.Archer:
                BoneBow(s); s.Beam(new Vector3(.11f, .26f, -.14f), new Vector3(.11f, .26f, -.43f), .012f, 6, 5); break;
            case Kind.Magic:
                Staff(s, .29f, .76f, 8); Crescent(s, new Vector3(-.23f, .39f, 0), .17f, 6); break;
            case Kind.Assassin:
                Sickle(s, -.29f, .25f, -25); Sickle(s, .29f, .29f, 25);
                FoldedCloak(s, new Vector3(0, .25f, .15f), .12f, .43f, .28f, 2); break;
            case Kind.Scout:
                for (int i = 0; i < 3; i++)
                {
                    float x = (i - 1) * .13f;
                    s.Beam(new Vector3(x, .03f, .03f), new Vector3(x * 1.8f, .60f - Mathf.Abs(x), .06f), .012f, 6, 4);
                    s.Gem(new Vector3(x * 1.8f, .60f - Mathf.Abs(x), .06f), Vector3.one * .035f, 8);
                }
                s.Fin(new Vector3(-.12f, .44f, .065f), new Vector3(.12f, .44f, .065f),
                    new Vector3(.17f, .19f, .12f), new Vector3(-.17f, .24f, .12f), 2);
                s.Beam(new Vector3(-.12f, .09f, -.08f), new Vector3(.12f, .09f, -.08f), .038f, 4, 6);
                break;
            case Kind.Priest:
                Halo(s, new Vector3(0, .55f, .09f), .23f, 4, 10); Staff(s, .29f, .56f, 10);
                s.Beam(new Vector3(-.26f, .34f, -.04f), new Vector3(-.26f, -.04f, -.04f), .008f, 4, 4);
                s.Gem(new Vector3(-.26f, -.06f, -.04f), new Vector3(.065f, .085f, .055f), 4); break;
            case Kind.Guardian:
                Shield(s, new Vector3(0, .08f, -.29f), .27f, .36f);
                ShoulderArmor(s, .15f, .13f);
                s.Horn(new Vector3(-.25f, .15f, .02f), new Vector3(-.36f, .48f, .07f), .065f, 6);
                s.Horn(new Vector3(.25f, .15f, .02f), new Vector3(.36f, .48f, .07f), .065f, 6); break;
            case Kind.Crossbow:
                s.Box(new Vector3(0, .19f, -.04f), new Vector3(.19f, .13f, .35f), 2, Quaternion.identity);
                s.Beam(new Vector3(0, .27f, .15f), new Vector3(0, .27f, -.42f), .045f, 2, 8);
                s.Horn(new Vector3(0, .27f, -.12f), new Vector3(-.38f, .27f, -.20f), .04f, 6);
                s.Horn(new Vector3(0, .27f, -.12f), new Vector3(.38f, .27f, -.20f), .04f, 6);
                s.Beam(new Vector3(-.38f, .27f, -.20f), new Vector3(0, .27f, .10f), .005f, 4, 4);
                s.Beam(new Vector3(.38f, .27f, -.20f), new Vector3(0, .27f, .10f), .005f, 4, 4);
                s.Gem(new Vector3(0, .27f, -.43f), new Vector3(.04f, .04f, .035f), 8); break;
            case Kind.Magicsniper:
                s.Beam(new Vector3(.17f, .28f, .17f), new Vector3(.17f, .28f, -.43f), .037f, 4, 6);
                s.Box(new Vector3(.17f, .28f, -.04f), new Vector3(.11f, .12f, .24f), 2, Quaternion.identity);
                Halo(s, new Vector3(.17f, .28f, -.35f), .085f, 6, 8);
                s.Gem(new Vector3(.17f, .28f, -.43f), new Vector3(.035f, .035f, .02f), 10); break;
            case Kind.Bomber:
                s.Beam(new Vector3(-.22f, .08f, .14f), new Vector3(.22f, .08f, .14f), .04f, 2, 6);
                for (int i = 0; i < 3; i++)
                {
                    var p = new Vector3((i - 1) * .14f, .17f + (i == 1 ? .12f : 0), .16f);
                    s.Gem(p, new Vector3(.09f, .105f, .09f), 9);
                    s.Beam(p + Vector3.up * .09f, p + Vector3.up * .17f, .011f, 4, 5);
                    s.Gem(p + Vector3.up * .17f, Vector3.one * .023f, 10);
                }
                break;
            case Kind.WhiteMage:
                Halo(s, new Vector3(0, .58f, .08f), .23f, 6, 12); Staff(s, -.28f, .64f, 8);
                s.Gem(new Vector3(.25f, .32f, -.03f), new Vector3(.09f, .13f, .06f), 6);
                FoldedCloak(s, new Vector3(0, .30f, .20f), .16f, .43f, .24f, 2); break;
            case Kind.HolyKnight:
                Shield(s, new Vector3(-.20f, .03f, -.19f), .17f, .25f); Staff(s, .29f, .67f, 6);
                s.Beam(new Vector3(.18f, .52f, 0), new Vector3(.40f, .52f, 0), .02f, 4, 5);
                ShoulderArmor(s, .20f, .13f); break;
            case Kind.Samurai:
                Crescent(s, new Vector3(0, .52f, -.04f), .20f, 4);
                Sword(s, new Vector3(.27f, -.22f, -.09f), .91f, .027f, 10);
                s.Beam(new Vector3(-.26f, -.20f, .04f), new Vector3(-.17f, .40f, .04f), .025f, 11, 6);
                ShoulderArmor(s, .16f, .14f); break;
            case Kind.Ninja:
                for (int side = -1; side <= 1; side += 2)
                {
                    s.Horn(new Vector3(side * .18f, .27f, 0), new Vector3(side * .35f, .52f, -.07f), .025f, 3);
                    s.Beam(new Vector3(side * .23f, -.07f, -.08f), new Vector3(side * .28f, .12f, -.16f), .012f, 4, 5);
                }
                s.Gem(new Vector3(.12f, .01f, -.25f), new Vector3(.045f, .012f, .012f), 8);
                for (int side = -1; side <= 1; side += 2)
                    s.Fin(new Vector3(side * .06f, .24f, .09f), new Vector3(side * .15f, .26f, .08f),
                        new Vector3(side * .33f, -.10f, .22f), new Vector3(side * .19f, -.04f, .24f), 2);
                break;
            case Kind.Berserker:
                for (int side = -1; side <= 1; side += 2)
                {
                    s.Beam(new Vector3(side * .29f, -.21f, -.02f), new Vector3(side * .29f, .48f, -.02f), .025f, 5, 6);
                    AxeHead(s, new Vector3(side * .29f, .43f, -.02f), side);
                    s.Horn(new Vector3(side * .16f, .37f, .02f), new Vector3(side * .22f, .67f, .08f), .04f, 6);
                }
                break;
            case Kind.Navigator:
                s.Beam(new Vector3(.25f, -.18f, 0), new Vector3(.25f, .68f, 0), .026f, 4, 6);
                Halo(s, new Vector3(.25f, .68f, 0), .075f, 4, 8);
                s.Beam(new Vector3(.09f, .48f, 0), new Vector3(.41f, .48f, 0), .023f, 4, 6);
                s.Horn(new Vector3(.25f, -.08f, 0), new Vector3(.07f, .11f, 0), .045f, 3);
                s.Horn(new Vector3(.25f, -.08f, 0), new Vector3(.43f, .11f, 0), .045f, 3); break;
        }
        float ornamentHeight = source == "Ram" ? .82f : source == "Crab" ? .80f : .86f;
        const float ornamentWidth = .82f;
        s.CompactOrnaments(ornamentHeight, ornamentWidth);
        ConnectEquipment(s, kind, source, ornamentHeight, ornamentWidth);
        return s;
    }
    static Sculpture NewSculpture(string source)
    {
        var s = new Sculpture();
        s.Lathe(new Vector3(0, -.50f, 0), new[] { new Vector2(.28f, 0), new Vector2(.355f, .025f), new Vector2(.355f, .065f), new Vector2(.31f, .10f) }, 0, 12);
        s.Lathe(new Vector3(0, -.40f, 0), new[] { new Vector2(.31f, 0), new Vector2(.30f, .035f), new Vector2(.26f, .10f), new Vector2(.25f, .14f) }, 1, 12);
        s.Lathe(new Vector3(0, -.405f, 0), new[] { new Vector2(.312f, 0), new Vector2(.312f, .014f) }, 4, 12);
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI / 4;
            s.Gem(new Vector3(Mathf.Cos(a) * .30f, -.34f, Mathf.Sin(a) * .30f), Vector3.one * .013f, 4, 4, 2);
        }
        // The long koi was almost a horizontal sliver from game height. Preserve its face/fins while giving its body more relief.
        s.Import(LoadSource(source), source == "Koi" ? new Vector3(1, 1.43f, 1.10f) : Vector3.one);
        s.BeginOrnaments(); return s;
    }
    static Sculpture MakeBoss(WildBossArchetype boss)
    {
        Sculpture s;
        switch (boss)
        {
            case WildBossArchetype.GhostKing:
                s = NewSculpture("Ram"); Crown(s, .58f, .25f, 9); Antlers(s, .47f);
                FoldedCloak(s, new Vector3(0, .32f, .19f), .21f, .51f, .41f, 2);
                ShoulderArmor(s, .24f, .18f);
                Halo(s, new Vector3(0, .59f, .10f), .30f, 4, 12); break;
            case WildBossArchetype.Dragon:
                s = NewSculpture("Koi");
                for (int side = -1; side <= 1; side += 2)
                {
                    DragonWing(s, side);
                    s.Horn(new Vector3(side * .10f, .16f, -.09f), new Vector3(side * .18f, .57f, -.02f), .04f, 6);
                }
                Spine(s, 7, -.08f); break;
            case WildBossArchetype.RebelKnight:
                s = NewSculpture("Hound"); Crown(s, .50f, .18f, 5);
                Shield(s, new Vector3(-.22f, .08f, -.18f), .20f, .35f);
                Sword(s, new Vector3(.29f, -.22f, -.02f), 1.08f, .065f, 8);
                ShoulderArmor(s, .15f, .16f);
                FoldedCloak(s, new Vector3(0, .25f, .16f), .17f, .46f, .32f, 2);
                s.Beam(new Vector3(-.20f, -.19f, .20f), new Vector3(-.20f, .88f, .20f), .018f, 4, 6);
                s.Fin(new Vector3(-.20f, .87f, .20f), new Vector3(.14f, .81f, .23f), new Vector3(.03f, .55f, .27f), new Vector3(-.20f, .59f, .20f), 9); break;
            default:
                s = NewSculpture("Crab");
                for (int i = 0; i < 3; i++)
                {
                    float x = (i - 1) * .26f; float y = i == 1 ? .81f : .61f;
                    s.Beam(new Vector3(x, .02f, .09f), new Vector3(x, y - .11f, .09f), .013f, 4, 5);
                    s.Gem(new Vector3(x, y, .09f), new Vector3(.072f, .15f, .065f), 8, 6, 3);
                    s.Horn(new Vector3(x, y + .08f, .09f), new Vector3(x + .05f, y + .22f, .06f), .022f, 6);
                }
                Halo(s, new Vector3(0, .25f, -.15f), .20f, 4, 10);
                ShoulderArmor(s, .19f, .17f);
                s.Box(new Vector3(0, .13f, .09f), new Vector3(.47f, .16f, .16f), 2, Quaternion.identity); break;
        }
        float compactY = boss == WildBossArchetype.GhostKing ? .88f : .92f;
        const float bossWidth = .88f;
        s.CompactOrnaments(compactY, bossWidth);
        if (boss == WildBossArchetype.Dragon)
        {
            // The original koi keeps its broad side profile; a rooted tail and wing shoulders make a single sculpture.
            KoiRoot(s);
            for (int side = -1; side <= 1; side += 2)
                s.Beam(new Vector3(side * .03f, -.08f, .01f), CompactPoint(new Vector3(side * .10f, .16f, .06f), compactY, bossWidth), .045f, 1, 6);
        }
        else if (boss == WildBossArchetype.ThunderMagus)
        {
            for (int i = -1; i <= 1; i++) s.Beam(new Vector3(0, -.09f, .02f), CompactPoint(new Vector3(i * .26f, .02f, .09f), compactY, bossWidth), .033f, 1, 6);
        }
        else if (boss == WildBossArchetype.RebelKnight) ConnectEquipment(s, Kind.Knight, "Hound", compactY, bossWidth);
        return s;
    }
    static void DragonWing(Sculpture s, int side)
    {
        var root = new Vector3(side * .08f, .14f, .04f);
        var elbow = new Vector3(side * .25f, .40f, .08f);
        var tip = new Vector3(side * .47f, .69f, .07f);
        var outer = new Vector3(side * .44f, .11f, .29f);
        var lower = new Vector3(side * .30f, -.06f, .21f);
        var inner = new Vector3(side * .17f, .02f, .12f);
        // Two folded, closed membrane panels and three rooted wing fingers read as a wing from side and top views.
        s.Fin(root, elbow, tip, outer, 9);
        s.Fin(root, outer, lower, inner, 9);
        s.Beam(root, elbow, .040f, 6, 6); s.Beam(elbow, tip, .027f, 6, 5);
        s.Beam(elbow, outer, .018f, 6, 5); s.Beam(root, lower, .018f, 6, 5);
        s.Horn(tip, tip + new Vector3(side * .035f, .09f, -.035f), .022f, 6);
    }
    static void Spine(Sculpture s, int count, float baseY = .15f)
    {
        for (int i = 0; i < count; i++)
        {
            float z = -.16f + .32f * i / Mathf.Max(1, count - 1);
            s.Horn(new Vector3(0, baseY, z), new Vector3(0, baseY + .20f + .08f * Mathf.Sin(i), z + .05f), .032f, 6);
        }
    }
    static Vector3 CompactPoint(Vector3 point, float height, float width)
        => new Vector3(point.x * width, -.26f + (point.y + .26f) * height, point.z * width);
    static void ConnectEquipment(Sculpture s, Kind kind, string source, float height, float width)
    {
        if (source == "Koi") KoiRoot(s);
        bool right = kind == Kind.Knight || kind == Kind.Archer || kind == Kind.Magic || kind == Kind.Priest
            || kind == Kind.HolyKnight || kind == Kind.Samurai || kind == Kind.Navigator || kind == Kind.Berserker;
        bool left = kind == Kind.Assassin || kind == Kind.WhiteMage || kind == Kind.Ninja || kind == Kind.Berserker;
        float shoulderY = source == "Ram" ? .08f : -.10f;
        var rightGrip = CompactPoint(new Vector3(.29f, -.06f, -.04f), height, width);
        var leftGrip = CompactPoint(new Vector3(-.27f, -.06f, -.04f), height, width);
        if (right) ClawArm(s, new Vector3(.04f, shoulderY, .015f), rightGrip);
        if (left) ClawArm(s, new Vector3(-.04f, shoulderY, .015f), leftGrip);
        if (kind == Kind.Assassin || kind == Kind.Ninja) ClawArm(s, new Vector3(.04f, -.10f, .015f), rightGrip);
        if (kind == Kind.Guardian)
        {
            for (int side = -1; side <= 1; side += 2)
                s.Beam(new Vector3(side * .06f, -.08f, .01f), CompactPoint(new Vector3(side * .25f, .15f, .02f), height, width), .032f, 1, 6);
        }
        if (kind == Kind.Guardian || kind == Kind.HolyKnight)
            s.Beam(new Vector3(0, -.08f, 0), CompactPoint(new Vector3(kind == Kind.HolyKnight ? -.20f : 0, .03f, kind == Kind.HolyKnight ? -.19f : -.29f), height, width), .035f, 1, 6);
        if (kind == Kind.Priest)
            s.Beam(new Vector3(-.04f, .16f, .01f), CompactPoint(new Vector3(-.26f, .34f, -.04f), height, width), .023f, 1, 6);
        if (kind == Kind.Ninja)
            for (int side = -1; side <= 1; side += 2)
                s.Beam(new Vector3(side * .04f, -.08f, .01f), CompactPoint(new Vector3(side * .18f, .27f, 0), height, width), .022f, 1, 5);
        if (kind == Kind.Magic)
            s.Beam(new Vector3(-.15f, -.08f, 0), CompactPoint(new Vector3(-.23f, .24f, 0), height, width), .018f, 1, 5);
        if (kind == Kind.WhiteMage)
            s.Beam(new Vector3(.05f, .08f, .01f), CompactPoint(new Vector3(.25f, .32f, -.03f), height, width), .022f, 1, 6);
        if (kind == Kind.Bomber)
            for (int i = -1; i <= 1; i++)
                s.Beam(new Vector3(0, -.09f, .02f), CompactPoint(new Vector3(i * .14f, i == 0 ? .29f : .17f, .16f), height, width), .025f, 1, 5);
        if (kind == Kind.Crossbow || kind == Kind.Magicsniper)
            s.Beam(new Vector3(0, -.10f, .01f), CompactPoint(new Vector3(kind == Kind.Crossbow ? 0 : .17f, .27f, -.06f), height, width), .028f, 1, 6);
        if (kind == Kind.King)
            s.Beam(new Vector3(0, .31f, .01f), new Vector3(0, .37f, .01f), .028f, 1, 5);
    }
    static void ClawArm(Sculpture s, Vector3 shoulder, Vector3 grip)
    {
        Vector3 elbow = Vector3.Lerp(shoulder, grip, .58f) + Vector3.back * .045f;
        s.Beam(shoulder, elbow, .026f, 1, 6); s.Beam(elbow, grip, .021f, 1, 6);
        s.Gem(grip, Vector3.one * .026f, 1, 5, 2);
    }
    static void KoiRoot(Sculpture s)
    {
        // A dark, tapered coil joins the swimming sculpture to its plinth instead of a floating fish and weapon.
        s.Beam(new Vector3(.06f, -.26f, .025f), new Vector3(.04f, -.17f, .03f), .040f, 1, 7);
        s.Beam(new Vector3(.04f, -.17f, .03f), new Vector3(-.02f, -.11f, .015f), .030f, 1, 6);
    }
    static void Crown(Sculpture s, float y, float r, int points)
    {
        s.Lathe(new Vector3(0, y - .07f, 0), new[] { new Vector2(r, 0), new Vector2(r * 1.05f, .045f) }, 4, 10);
        for (int i = 0; i < points; i++)
        {
            float a = i * Mathf.PI * 2 / points;
            var from = new Vector3(Mathf.Cos(a) * r, y - .04f, Mathf.Sin(a) * r);
            s.Horn(from, from + new Vector3(Mathf.Cos(a) * .04f, i % 2 == 0 ? .16f : .11f, Mathf.Sin(a) * .04f), .035f, 4);
        }
    }
    static void Antlers(Sculpture s, float y)
    {
        for (int side = -1; side <= 1; side += 2)
        {
            var a = new Vector3(side * .11f, y, .03f); var b = new Vector3(side * .26f, y + .14f, .07f); var c = new Vector3(side * .32f, y + .31f, .04f);
            s.Beam(a, b, .024f, 6, 5); s.Horn(b, c, .023f, 6);
            s.Horn(b, new Vector3(side * .38f, y + .17f, .11f), .018f, 6);
        }
    }
    static void Sword(Sculpture s, Vector3 origin, float height, float width, float tilt)
    {
        Quaternion rotation = Quaternion.Euler(0, 0, tilt); var grip = origin + rotation * Vector3.up * .15f;
        s.Beam(origin, grip, .02f, 5, 6); var hand = grip + rotation * Vector3.up * .025f;
        s.Beam(hand + rotation * Vector3.left * .09f, hand + rotation * Vector3.right * .09f, .018f, 4, 5);
        s.Blade(hand, origin + rotation * Vector3.up * height, width * 1.5f, .015f, 3);
        s.Gem(origin, new Vector3(.025f, .032f, .025f), 4, 5, 2);
    }
    static void Staff(Sculpture s, float x, float y, int gemColor)
    {
        s.Beam(new Vector3(x, -.22f, 0), new Vector3(x, y - .08f, 0), .018f, 5, 6);
        s.Gem(new Vector3(x, y, 0), new Vector3(.065f, .10f, .065f), gemColor, 6, 3);
        Halo(s, new Vector3(x, y, .012f), .11f, 4, 8);
    }
    static void Halo(Sculpture s, Vector3 center, float radius, int color, int sides)
    {
        for (int i = 0; i < sides; i++)
        {
            float a = i * Mathf.PI * 2 / sides, b = (i + 1) * Mathf.PI * 2 / sides;
            s.Beam(center + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * radius,
                center + new Vector3(Mathf.Cos(b), Mathf.Sin(b), 0) * radius, .010f, color, 4);
        }
    }
    static void Crescent(Sculpture s, Vector3 center, float radius, int color)
    {
        for (int i = 0; i < 7; i++)
        {
            float a = (i / 7f * 1.4f + .3f) * Mathf.PI, b = ((i + 1) / 7f * 1.4f + .3f) * Mathf.PI;
            s.Beam(center + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * radius,
                center + new Vector3(Mathf.Cos(b), Mathf.Sin(b), 0) * radius, .018f, color, 5);
        }
    }
    static void Sickle(Sculpture s, float x, float y, float tilt)
    {
        s.Beam(new Vector3(x, -.20f, -.04f), new Vector3(x, y, -.04f), .018f, 5, 5);
        var root = new Vector3(x, y, -.04f); var bend = new Vector3(x * .75f, y + .15f, -.075f);
        s.Blade(root, bend, .055f, .016f, 6);
        s.Blade(bend, new Vector3(x * .25f, y + .20f, -.11f), .05f, .012f, 6);
    }
    static void BoneBow(Sculpture s)
    {
        var top = new Vector3(.22f, .68f, -.02f); var middle = new Vector3(.40f, .20f, -.02f); var bottom = new Vector3(.22f, -.22f, -.02f);
        s.Beam(top, middle, .028f, 6, 6); s.Beam(middle, bottom, .028f, 6, 6); s.Beam(top, bottom, .005f, 4, 4);
        s.Horn(top, new Vector3(.18f, .77f, -.02f), .023f, 6); s.Horn(bottom, new Vector3(.18f, -.26f, -.02f), .023f, 6);
    }
    static void Shield(Sculpture s, Vector3 center, float width, float height)
    {
        // A closed, six-sided shield with a raised central ridge replaces the paper-thin quadrilateral.
        var rim = new[] { new Vector3(0, height, 0), new Vector3(width, height * .62f, 0),
            new Vector3(width * .84f, -height * .43f, 0), new Vector3(0, -height, 0),
            new Vector3(-width * .84f, -height * .43f, 0), new Vector3(-width, height * .62f, 0) };
        for (int i = 0; i < rim.Length; i++) rim[i] += center;
        s.RidgedPlate(rim, center + Vector3.back * .055f, .025f, 2);
        s.Beam(center + Vector3.up * height * .65f, center - Vector3.up * height * .80f, .018f, 4, 5);
        s.Gem(center + new Vector3(0, 0, -.058f), new Vector3(.045f, .06f, .02f), 10);
    }
    static void ShoulderArmor(Sculpture s, float y, float size)
    {
        for (int side = -1; side <= 1; side += 2)
        {
            var c = new Vector3(side * .16f, y, .005f);
            s.Gem(c, new Vector3(size, size * .50f, size * .83f), 2, 5, 2);
            s.Beam(c + Vector3.down * size * .28f, new Vector3(side * .07f, y - .07f, .02f), .035f, 1, 5);
        }
    }
    static void AxeHead(Sculpture s, Vector3 center, int side)
    {
        var rim = new[] { center + new Vector3(-.04f * side, .10f, 0), center + new Vector3(.16f * side, .11f, 0),
            center + new Vector3(.19f * side, -.12f, 0), center + new Vector3(.06f * side, -.07f, 0),
            center + new Vector3(-.04f * side, -.03f, 0) };
        s.RidgedPlate(rim, center + Vector3.back * .04f, .028f, 3);
    }
    static void FoldedCloak(Sculpture s, Vector3 shoulder, float topWidth, float length, float hemWidth, int color)
    {
        // Separate folded strips keep the source animal visible through the open front and make a jagged, heavy hem.
        for (int i = 0; i < 4; i++)
        {
            float a = i * .5f - 1, b = (i + 1) * .5f - 1;
            var topA = shoulder + new Vector3(a * topWidth, 0, 0);
            var topB = shoulder + new Vector3(b * topWidth, 0, 0);
            var hemA = shoulder + new Vector3(a * hemWidth, -length * (i % 2 == 0 ? .84f : 1), .10f);
            var hemB = shoulder + new Vector3(b * hemWidth, -length * (i % 2 == 0 ? 1 : .84f), .10f);
            s.Fin(topA, topB, hemB, hemA, color);
        }
    }

    sealed class Sculpture
    {
        readonly List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>(); readonly List<int> triangles = new List<int>();
        int ornamentStart;
        public void BeginOrnaments() => ornamentStart = vertices.Count;
        public void CompactOrnaments(float heightScale, float widthScale)
        {
            var anchor = new Vector3(0, -.26f, 0);
            var scale = new Vector3(widthScale, heightScale, widthScale);
            for (int i = ornamentStart; i < vertices.Count; i++)
            {
                vertices[i] = anchor + Vector3.Scale(vertices[i] - anchor, scale);
                var n = normals[i]; normals[i] = new Vector3(n.x / scale.x, n.y / scale.y, n.z / scale.z).normalized;
            }
        }
        void Tri(Vector3 a, Vector3 b, Vector3 c, int color)
        {
            Vector3 n = Vector3.Cross(b - a, c - a).normalized; int index = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); normals.Add(n); normals.Add(n); normals.Add(n);
            var uv = new Vector2((color + .5f) / 12, .5f); uvs.Add(uv); uvs.Add(uv); uvs.Add(uv);
            triangles.Add(index); triangles.Add(index + 1); triangles.Add(index + 2);
        }
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int color) { Tri(a, b, c, color); Tri(a, c, d, color); }
        public void Import(ImportedBody body, Vector3 scale)
        {
            for (int i = 0; i < body.positions.Length; i += 3)
            {
                var anchor = new Vector3(0, -.26f, 0);
                int index = vertices.Count;
                vertices.Add(anchor + Vector3.Scale(new Vector3(body.positions[i], body.positions[i + 1], body.positions[i + 2]) - anchor, scale));
                normals.Add(new Vector3(body.normals[i] / scale.x, body.normals[i + 1] / scale.y, body.normals[i + 2] / scale.z).normalized);
                uvs.Add(new Vector2(1.5f / 12, .5f)); triangles.Add(index);
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
                int last = profile.Length - 1; Tri(origin + Vector3.up * profile[last].y, Radial(origin, profile[last], b), Radial(origin, profile[last], a), color);
            }
        }
        static Vector3 Radial(Vector3 origin, Vector2 p, float angle) => origin + new Vector3(Mathf.Cos(angle) * p.x, p.y, Mathf.Sin(angle) * p.x);
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
            var direction = to - from; var rotation = Quaternion.FromToRotation(Vector3.up, direction.normalized);
            var side = rotation * Vector3.right; var forward = rotation * Vector3.forward;
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides, b = (i + 1) * Mathf.PI * 2 / sides;
                var pa = (Mathf.Cos(a) * side + Mathf.Sin(a) * forward) * radius; var pb = (Mathf.Cos(b) * side + Mathf.Sin(b) * forward) * radius;
                Quad(from + pa, to + pa, to + pb, from + pb, color); Tri(from, from + pa, from + pb, color); Tri(to, to + pb, to + pa, color);
            }
        }
        public void Horn(Vector3 from, Vector3 tip, float radius, int color)
        {
            var direction = tip - from; var rotation = Quaternion.FromToRotation(Vector3.up, direction.normalized);
            for (int i = 0; i < 5; i++)
            {
                float a = i * Mathf.PI * 2 / 5, b = (i + 1) * Mathf.PI * 2 / 5;
                var pa = from + rotation * new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius);
                var pb = from + rotation * new Vector3(Mathf.Cos(b) * radius, 0, Mathf.Sin(b) * radius);
                Tri(pa, tip, pb, color); Tri(from, pa, pb, color);
            }
        }
        public void Gem(Vector3 center, Vector3 size, int color, int sides = 6, int rings = 3)
        {
            for (int ring = 0; ring < rings; ring++) for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides, b = (i + 1) * Mathf.PI * 2 / sides;
                float low = -Mathf.PI * .5f + ring * Mathf.PI / rings, high = low + Mathf.PI / rings;
                Quad(GemPoint(center, size, a, low), GemPoint(center, size, a, high), GemPoint(center, size, b, high), GemPoint(center, size, b, low), color);
            }
        }
        static Vector3 GemPoint(Vector3 c, Vector3 size, float a, float y) => c + Vector3.Scale(size, new Vector3(Mathf.Cos(a) * Mathf.Cos(y), Mathf.Sin(y), Mathf.Sin(a) * Mathf.Cos(y)));
        public void Fin(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int color)
        {
            var normal = Vector3.Cross(b - a, c - a).normalized;
            var rim = new[] { a, b, c, d };
            var center = (a + b + c + d) * .25f;
            var front = center + normal * .026f; var back = center - normal * .014f;
            // Closed thickness and a raised fold remain visible when the camera looks along the panel.
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                Tri(rim[i], rim[j], front, color);
                Tri(rim[j] - normal * .014f, rim[i] - normal * .014f, back, color);
                Quad(rim[j], rim[i], rim[i] - normal * .014f, rim[j] - normal * .014f, color);
            }
        }
        public void RidgedPlate(Vector3[] rim, Vector3 front, float thickness, int color)
        {
            var back = Vector3.zero;
            foreach (var p in rim) back += p;
            back /= rim.Length; back += Vector3.forward * thickness;
            bool clockwise = Vector3.Cross(rim[1] - rim[0], rim[2] - rim[0]).z < 0;
            for (int i = 0; i < rim.Length; i++)
            {
                int j = (i + 1) % rim.Length;
                var a = clockwise ? rim[i] : rim[j]; var b = clockwise ? rim[j] : rim[i];
                Tri(a, b, front, color);
                Tri(b + Vector3.forward * thickness, a + Vector3.forward * thickness, back, color);
                Quad(b, a, a + Vector3.forward * thickness, b + Vector3.forward * thickness, color);
            }
        }
        public void Blade(Vector3 root, Vector3 tip, float width, float thickness, int color)
        {
            var axis = tip - root;
            var rotation = Quaternion.FromToRotation(Vector3.up, axis.normalized);
            var right = rotation * Vector3.right; var front = rotation * Vector3.forward;
            var start = new[] { root + right * width, root + front * thickness, root - right * width, root - front * thickness };
            var neck = root + axis * .76f;
            var end = new[] { neck + right * width * .67f, neck + front * thickness, neck - right * width * .67f, neck - front * thickness };
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                Quad(start[i], end[i], end[j], start[j], color);
                Tri(end[i], tip, end[j], color); Tri(root, start[i], start[j], color);
            }
        }
        public Mesh Build(string name)
        {
            var mesh = new Mesh { name = name }; mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uvs); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds(); return mesh;
        }
    }
}
#endif


