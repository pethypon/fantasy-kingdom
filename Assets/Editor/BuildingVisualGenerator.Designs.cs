#if UNITY_EDITOR
using UnityEngine;

public static partial class BuildingVisualGenerator
{
    // Palette index: oak, cut wood, stone, slate, brass, iron, water,
    // red tile, furnace amber, wheat, foliage, plaster, earth, crystal, dark, silver.
    static Builder MakeBuilding(FacilityKind kind)
    {
        var b = new Builder();
        b.Box(new Vector3(0, .04f, 0), new Vector3(.86f, .08f, .86f), 5);
        b.Box(new Vector3(0, .083f, 0), new Vector3(.81f, .025f, .81f), 4);
        switch (kind)
        {
            case FacilityKind.Field: Field(b); break;
            case FacilityKind.Bakery: Bakery(b); break;
            case FacilityKind.LoggingCamp: LoggingCamp(b); break;
            case FacilityKind.Quarry: Quarry(b); break;
            case FacilityKind.Mine: Mine(b); break;
            case FacilityKind.Barracks: Barracks(b); break;
            case FacilityKind.House: House(b, false); break;
            case FacilityKind.LuxuryHouse: House(b, true); break;
            case FacilityKind.Well: Well(b); break;
            case FacilityKind.Warehouse: Warehouse(b); break;
            case FacilityKind.WoodWall: WoodWall(b); break;
            case FacilityKind.StoneWall: StoneWall(b); break;
            case FacilityKind.Mortar: Mortar(b); break;
            case FacilityKind.Cannon: Cannon(b); break;
            case FacilityKind.RestraintTrap: RestraintTrap(b); break;
            case FacilityKind.SpikeTrap: SpikeTrap(b); break;
            case FacilityKind.HeroSword: HeroSword(b); break;
            case FacilityKind.SubCrystal: SubCrystal(b); break;
        }
        return b;
    }

    static void Cabin(Builder b, Vector3 center, Vector3 size, int wall, int roof, float rise)
    {
        b.Box(center, size, wall);
        float bottom = center.y - size.y / 2, top = center.y + size.y / 2;
        for (int x = -1; x <= 1; x += 2) for (int z = -1; z <= 1; z += 2)
            b.Box(center + new Vector3(x * (size.x / 2 - .022f), 0, z * (size.z / 2 - .022f)), new Vector3(.047f, size.y + .015f, .047f), 0);
        b.Box(new Vector3(center.x, bottom + .035f, center.z), new Vector3(size.x + .02f, .055f, size.z + .02f), 0);
        b.Roof(new Vector3(center.x, top + .015f, center.z), size.x + .095f, size.z + .10f, rise, roof);
        b.Box(new Vector3(center.x, top + rise + .02f, center.z), new Vector3(.05f, .045f, size.z + .10f), 4);
        // Small roof ribs read clearly at board distance without a texture or decal.
        for (int side = -1; side <= 1; side += 2)
            for (int i = 0; i < 3; i++)
                b.Box(new Vector3(center.x + side * size.x * .26f, top + rise * .48f + .028f, center.z + (i - 1) * size.z * .32f), new Vector3(.022f, .024f, size.z * .27f), roof);
    }

    static void Window(Builder b, float x, float y, float z, bool side = false)
    {
        var size = side ? new Vector3(.015f, .13f, .13f) : new Vector3(.13f, .13f, .015f);
        b.Box(new Vector3(x, y, z), size + new Vector3(.028f, .028f, .028f), 0);
        b.Box(new Vector3(x, y, z) + (side ? new Vector3(.018f, 0, 0) : new Vector3(0, 0, -.018f)), size, 8);
        b.Box(new Vector3(x, y, z) + (side ? new Vector3(.03f, 0, 0) : new Vector3(0, 0, -.03f)), side ? new Vector3(.016f, .016f, .15f) : new Vector3(.15f, .016f, .016f), 4);
    }

    static void Barrel(Builder b, Vector3 center, float radius = .10f, float height = .20f)
    {
        b.Frustum(center, radius * .88f, radius, height * .5f, 1);
        b.Frustum(center + Vector3.up * height * .5f, radius, radius * .88f, height * .5f, 1);
        b.Ring(center + Vector3.up * height * .2f, radius + .008f, radius - .015f, .022f, 5, 8);
        b.Ring(center + Vector3.up * height * .75f, radius + .008f, radius - .015f, .022f, 5, 8);
    }

    static void Field(Builder b)
    {
        b.Box(new Vector3(0, .12f, 0), new Vector3(.77f, .07f, .77f), 12);
        for (int i = -1; i <= 1; i++)
        {
            float x = i * .24f;
            b.Box(new Vector3(x, .17f, 0), new Vector3(.17f, .07f, .65f), 1);
            for (int j = 0; j < 5; j++)
            {
                float z = -.25f + j * .12f, tall = .20f + ((j + i + 4) % 3) * .025f;
                b.Frustum(new Vector3(x, .19f, z), .010f, .007f, tall, 9, 4);
                b.Frustum(new Vector3(x, .19f + tall * .63f, z), .040f, 0, .075f, 9, 4);
                b.Frustum(new Vector3(x, .24f, z), .013f, 0, .08f, 10, 3, new Vector3(1, .8f, .3f));
                b.Frustum(new Vector3(x, .28f, z), .013f, 0, .07f, 10, 3, new Vector3(-1, .8f, -.3f));
            }
        }
        b.Box(new Vector3(0, .17f, .355f), new Vector3(.73f, .025f, .035f), 6);
        b.Frustum(new Vector3(.36f, .18f, .30f), .038f, .038f, .19f, 4, 6);
    }

    static void Bakery(Builder b)
    {
        Cabin(b, new Vector3(-.05f, .37f, .06f), new Vector3(.58f, .50f, .52f), 11, 7, .25f);
        b.Box(new Vector3(-.08f, .30f, -.218f), new Vector3(.29f, .26f, .035f), 2);
        b.Frustum(new Vector3(-.08f, .38f, -.24f), .145f, .145f, .025f, 2, 8, Vector3.back);
        b.Box(new Vector3(-.08f, .27f, -.247f), new Vector3(.21f, .20f, .015f), 14);
        b.Box(new Vector3(-.08f, .215f, -.26f), new Vector3(.18f, .06f, .015f), 8);
        b.Box(new Vector3(.15f, .80f, .15f), new Vector3(.14f, .59f, .15f), 2);
        for (int i = 0; i < 5; i++) b.Box(new Vector3(.15f, .57f + i * .095f, .15f), new Vector3(.158f, .018f, .168f), 4);
        b.Box(new Vector3(.15f, 1.11f, .15f), new Vector3(.21f, .06f, .22f), 5);
        b.Box(new Vector3(.15f, 1.145f, .15f), new Vector3(.12f, .015f, .13f), 14);
        b.Box(new Vector3(.22f, .15f, -.31f), new Vector3(.28f, .05f, .14f), 0);
        for (int i = 0; i < 3; i++) b.Frustum(new Vector3(.12f + i * .08f, .18f, -.31f), .035f, .025f, .09f, 9, 6, Vector3.forward);
    }

    static void LoggingCamp(Builder b)
    {
        Cabin(b, new Vector3(-.12f, .33f, .14f), new Vector3(.42f, .40f, .40f), 1, 3, .22f);
        b.Box(new Vector3(-.12f, .28f, -.067f), new Vector3(.21f, .27f, .022f), 14);
        b.Box(new Vector3(.13f, .18f, -.22f), new Vector3(.50f, .09f, .20f), 0);
        for (int i = 0; i < 3; i++)
        {
            var p = new Vector3(-.28f + i * .18f, .23f, .015f);
            b.Frustum(p, .065f, .065f, .38f, 0, 8, Vector3.back);
            b.Frustum(p + Vector3.back * .385f, .054f, .054f, .005f, 1, 8, Vector3.back);
            b.Frustum(p + Vector3.back * .391f, .022f, .022f, .005f, 9, 8, Vector3.back);
        }
        var saw = new Vector3(.255f, .37f, -.20f);
        b.Frustum(saw, .145f, .145f, .035f, 15, 12, Vector3.right);
        for (int i = 0; i < 10; i++)
        {
            float angle = i * Mathf.PI * 2 / 10;
            var p = saw + new Vector3(.015f, Mathf.Sin(angle) * .15f, Mathf.Cos(angle) * .15f);
            b.Frustum(p, .027f, 0, .04f, 15, 3, new Vector3(0, Mathf.Sin(angle), Mathf.Cos(angle)));
        }
        b.Frustum(saw - Vector3.right * .015f, .045f, .045f, .07f, 4, 8, Vector3.right);
    }

    static void Quarry(Builder b)
    {
        for (int i = 0; i < 4; i++) b.Box(new Vector3(-.22f + (i % 2) * .24f, .18f + (i / 2) * .10f, -.16f + (i / 2) * .18f), new Vector3(.22f, .18f, .22f), 2, i * 23);
        b.Box(new Vector3(-.29f, .56f, .23f), new Vector3(.09f, .88f, .11f), 0);
        b.Box(new Vector3(.29f, .56f, .23f), new Vector3(.09f, .88f, .11f), 0);
        b.Box(new Vector3(0, 1.01f, .23f), new Vector3(.78f, .09f, .12f), 4);
        b.Box(new Vector3(0, .98f, -.04f), new Vector3(.10f, .06f, .63f), 0);
        b.Frustum(new Vector3(0, .91f, -.29f), .085f, .085f, .05f, 4, 8, Vector3.right);
        b.Box(new Vector3(0, .62f, -.29f), new Vector3(.018f, .60f, .018f), 5);
        b.Box(new Vector3(0, .27f, -.29f), new Vector3(.27f, .20f, .20f), 2);
        b.Ring(new Vector3(.35f, .42f, .20f), .10f, .07f, .04f, 4, 8, Vector3.right);
    }

    static void Mine(Builder b)
    {
        b.Frustum(new Vector3(0, .11f, .1f), .36f, .24f, .50f, 2, 7);
        b.Frustum(new Vector3(-.10f, .49f, .10f), .25f, .08f, .30f, 3, 5);
        b.Box(new Vector3(.015f, .29f, -.253f), new Vector3(.30f, .35f, .045f), 14);
        for (int i = -1; i <= 1; i += 2) b.Box(new Vector3(i * .18f, .32f, -.28f), new Vector3(.055f, .40f, .065f), 0);
        b.Box(new Vector3(0, .51f, -.28f), new Vector3(.43f, .075f, .07f), 4);
        for (int i = -1; i <= 1; i += 2) b.Box(new Vector3(i * .12f, .12f, -.27f), new Vector3(.027f, .025f, .33f), 5);
        b.Box(new Vector3(.02f, .21f, -.30f), new Vector3(.24f, .15f, .19f), 5);
        for (int i = -1; i <= 1; i += 2) b.Frustum(new Vector3(i * .13f, .155f, -.30f), .045f, .045f, .025f, 4, 8, Vector3.right);
        b.Frustum(new Vector3(-.07f, .27f, -.29f), .06f, 0, .16f, 13, 5);
        b.Frustum(new Vector3(.07f, .27f, -.29f), .065f, 0, .10f, 15, 5);
    }

    static void Barracks(Builder b)
    {
        Cabin(b, new Vector3(-.06f, .36f, 0), new Vector3(.54f, .50f, .59f), 2, 3, .23f);
        b.Box(new Vector3(-.08f, .28f, -.307f), new Vector3(.24f, .30f, .03f), 0);
        b.Box(new Vector3(-.08f, .40f, -.329f), new Vector3(.27f, .04f, .025f), 4);
        b.Box(new Vector3(.28f, .60f, .22f), new Vector3(.21f, .98f, .23f), 3);
        b.Frustum(new Vector3(.28f, 1.1f, .22f), .16f, 0, .21f, 4, 4);
        b.Box(new Vector3(-.18f, .96f, .13f), new Vector3(.021f, .50f, .021f), 4);
        b.Box(new Vector3(-.05f, 1.13f, .13f), new Vector3(.24f, .18f, .012f), 7);
        b.Box(new Vector3(-.05f, 1.13f, .12f), new Vector3(.04f, .18f, .008f), 4);
        Window(b, .28f, .75f, .094f);
        b.Box(new Vector3(.28f, .16f, -.25f), new Vector3(.15f, .07f, .21f), 1);
    }

    static void House(Builder b, bool luxury)
    {
        float height = luxury ? .64f : .42f, y = .13f + height / 2;
        Cabin(b, new Vector3(0, y, .05f), new Vector3(.57f, height, .54f), luxury ? 11 : 1, luxury ? 3 : 7, luxury ? .30f : .27f);
        b.Box(new Vector3(0, .25f, -.235f), new Vector3(.15f, .25f, .03f), 0);
        b.Box(new Vector3(.045f, .26f, -.256f), new Vector3(.022f, .023f, .013f), 4);
        Window(b, -.19f, .36f, -.238f); Window(b, .19f, .36f, -.238f);
        Window(b, .294f, .37f, .09f, true);
        b.Box(new Vector3(0, .13f, -.34f), new Vector3(.26f, .08f, .15f), 2);
        if (!luxury)
        {
            b.Box(new Vector3(.18f, .70f, .16f), new Vector3(.10f, .30f, .10f), 2);
            b.Box(new Vector3(.18f, .86f, .16f), new Vector3(.13f, .035f, .13f), 5);
        }
        else
        {
            b.Box(new Vector3(0, .63f, -.24f), new Vector3(.61f, .055f, .035f), 4);
            Window(b, -.15f, .64f, -.257f); Window(b, .15f, .64f, -.257f);
            b.Frustum(new Vector3(.25f, .12f, .24f), .16f, .16f, .86f, 2, 8);
            b.Frustum(new Vector3(.25f, .98f, .24f), .20f, 0, .27f, 3, 8);
            b.Ring(new Vector3(.25f, .91f, .24f), .174f, .149f, .07f, 4, 8);
            b.Box(new Vector3(.05f, .17f, -.34f), new Vector3(.40f, .06f, .10f), 11);
        }
    }

    static void Well(Builder b)
    {
        b.Frustum(new Vector3(0, .11f, 0), .28f, .28f, .09f, 2, 12);
        b.Ring(new Vector3(0, .20f, 0), .29f, .21f, .20f, 2, 12);
        b.Ring(new Vector3(0, .40f, 0), .315f, .205f, .055f, 4, 12);
        b.Frustum(new Vector3(0, .30f, 0), .205f, .205f, .01f, 6, 12);
        for (int x = -1; x <= 1; x += 2) b.Box(new Vector3(x * .29f, .53f, 0), new Vector3(.065f, .83f, .075f), 0);
        b.Box(new Vector3(0, .94f, 0), new Vector3(.74f, .06f, .08f), 4);
        b.Roof(new Vector3(0, .965f, 0), .76f, .57f, .19f, 3);
        b.Frustum(new Vector3(-.33f, .74f, 0), .041f, .041f, .69f, 4, 8, Vector3.right);
        b.Ring(new Vector3(.35f, .74f, 0), .14f, .11f, .025f, 4, 12, Vector3.right);
        b.Box(new Vector3(0, .61f, 0), new Vector3(.017f, .27f, .017f), 0);
        Barrel(b, new Vector3(0, .44f, 0), .07f, .11f);
    }

    static void Warehouse(Builder b)
    {
        Cabin(b, new Vector3(-.06f, .43f, .1f), new Vector3(.60f, .62f, .51f), 1, 3, .26f);
        b.Box(new Vector3(-.06f, .32f, -.169f), new Vector3(.35f, .39f, .025f), 0);
        b.Box(new Vector3(-.06f, .31f, -.185f), new Vector3(.022f, .37f, .022f), 4);
        b.Box(new Vector3(-.06f, .49f, -.185f), new Vector3(.34f, .035f, .022f), 4);
        Window(b, .06f, .66f, -.17f);
        Barrel(b, new Vector3(.28f, .12f, -.26f), .11f, .25f);
        b.Box(new Vector3(-.29f, .20f, -.29f), new Vector3(.19f, .18f, .19f), 1);
        b.Box(new Vector3(-.29f, .21f, -.393f), new Vector3(.19f, .032f, .016f), 4);
        b.Box(new Vector3(-.29f, .21f, -.393f), new Vector3(.032f, .18f, .016f), 4);
        b.Box(new Vector3(-.29f, .40f, -.28f), new Vector3(.15f, .19f, .15f), 11);
    }

    static void WoodWall(Builder b)
    {
        for (int i = 0; i < 5; i++)
        {
            var p = new Vector3((i - 2) * .17f, .11f, 0);
            b.Frustum(p, .075f, .075f, .64f, 0, 6);
            b.Frustum(p + Vector3.up * .64f, .075f, 0, .17f, 1, 6);
            b.Ring(p + Vector3.up * .22f, .081f, .07f, .04f, 5, 6);
        }
        b.Box(new Vector3(0, .36f, -.073f), new Vector3(.83f, .065f, .043f), 1);
        b.Box(new Vector3(0, .65f, -.073f), new Vector3(.83f, .065f, .043f), 1);
        for (int i = -1; i <= 1; i++) b.Box(new Vector3(i * .33f, .51f, -.10f), new Vector3(.047f, .37f, .023f), 4);
    }

    static void StoneWall(Builder b)
    {
        b.Box(new Vector3(0, .36f, 0), new Vector3(.84f, .51f, .33f), 2);
        for (int row = 0; row < 3; row++)
        {
            float y = .2f + row * .15f;
            b.Box(new Vector3(0, y, -.177f), new Vector3(.85f, .012f, .012f), 3);
            for (int i = 0; i < 3; i++) b.Box(new Vector3((i - 1) * .28f + (row % 2) * .07f, y + .07f, -.177f), new Vector3(.014f, .13f, .014f), 3);
        }
        b.Box(new Vector3(0, .65f, 0), new Vector3(.9f, .09f, .39f), 3);
        for (int i = -1; i <= 1; i++) b.Box(new Vector3(i * .32f, .79f, 0), new Vector3(.19f, .20f, .36f), 2);
        for (int i = -1; i <= 1; i++) b.Box(new Vector3(i * .32f, .90f, 0), new Vector3(.20f, .035f, .38f), 4);
    }

    static void Mortar(Builder b)
    {
        b.Frustum(new Vector3(0, .10f, 0), .31f, .29f, .14f, 2, 8);
        b.Ring(new Vector3(0, .21f, 0), .30f, .25f, .055f, 4, 12);
        b.Box(new Vector3(0, .30f, .03f), new Vector3(.39f, .22f, .26f), 5);
        var axis = new Vector3(0, .92f, -.39f).normalized;
        var start = new Vector3(0, .35f, .02f);
        b.Frustum(start, .19f, .16f, .41f, 5, 12, axis);
        var end = start + axis * .41f;
        b.Ring(end, .19f, .12f, .055f, 4, 12, axis);
        b.Frustum(end, .118f, .118f, .01f, 14, 12, axis);
        for (int i = 0; i < 3; i++) b.Frustum(new Vector3(-.25f + i * .1f, .12f, -.29f), .045f, .045f, .10f, 15, 8);
    }

    static void Cannon(Builder b)
    {
        b.Box(new Vector3(0, .22f, .05f), new Vector3(.43f, .16f, .52f), 0);
        for (int i = -1; i <= 1; i += 2)
        {
            var p = new Vector3(i * .29f, .26f, .10f);
            b.Frustum(p, .17f, .17f, .045f, 0, 12, Vector3.right);
            b.Ring(p + Vector3.right * .045f, .173f, .138f, .015f, 5, 12, Vector3.right);
            b.Frustum(p + Vector3.right * .046f, .05f, .05f, .02f, 4, 8, Vector3.right);
        }
        var axis = new Vector3(0, .18f, -.98f).normalized;
        var start = new Vector3(0, .48f, .24f);
        b.Frustum(start, .13f, .10f, .55f, 5, 12, axis);
        b.Ring(start + axis * .28f, .13f, .09f, .035f, 4, 12, axis);
        var muzzle = start + axis * .55f;
        b.Ring(muzzle, .125f, .073f, .055f, 4, 12, axis);
        b.Frustum(muzzle, .071f, .071f, .006f, 14, 12, axis);
        b.Frustum(new Vector3(0, .38f, .10f), .035f, .035f, .45f, 4, 8, Vector3.right);
    }

    static void RestraintTrap(Builder b)
    {
        b.Box(new Vector3(0, .12f, 0), new Vector3(.58f, .04f, .58f), 0);
        b.Ring(new Vector3(0, .16f, 0), .27f, .23f, .04f, 5, 12);
        for (int row = -1; row <= 1; row += 2)
            for (int i = -2; i <= 2; i++)
            {
                float x = i * .075f, z = row * (.20f - Mathf.Abs(i) * .018f);
                b.Frustum(new Vector3(x, .18f, z), .032f, 0, .12f, 15, 3, new Vector3(0, .75f, -row * .66f));
            }
        b.Frustum(new Vector3(0, .18f, 0), .09f, .09f, .025f, 4, 8);
        for (int i = 0; i < 4; i++) b.Ring(new Vector3(.20f + i * .046f, .16f, .23f + i * .029f), .035f, .024f, .011f, 4, 6);
        b.Frustum(new Vector3(.37f, .11f, .34f), .035f, .02f, .15f, 5, 6);
    }

    static void SpikeTrap(Builder b)
    {
        b.Box(new Vector3(0, .13f, 0), new Vector3(.70f, .06f, .70f), 0);
        for (int i = -1; i <= 1; i++) for (int j = -1; j <= 1; j++)
        {
            var p = new Vector3(i * .21f, .17f, j * .21f);
            b.Frustum(p, .045f, 0, .25f + (i == 0 && j == 0 ? .09f : 0), 15, 4);
            b.Ring(p, .051f, .04f, .021f, 4, 6);
        }
        b.Box(new Vector3(0, .18f, -.36f), new Vector3(.73f, .03f, .035f), 7);
        b.Box(new Vector3(0, .18f, .36f), new Vector3(.73f, .03f, .035f), 7);
    }

    static void HeroSword(Builder b)
    {
        b.Frustum(new Vector3(0, .10f, 0), .31f, .24f, .19f, 3, 8);
        b.Frustum(new Vector3(0, .29f, 0), .24f, .17f, .17f, 2, 6);
        b.Ring(new Vector3(0, .31f, 0), .245f, .215f, .045f, 4, 12);
        b.Box(new Vector3(0, .75f, 0), new Vector3(.075f, .63f, .034f), 15);
        b.Box(new Vector3(0, .73f, -.024f), new Vector3(.018f, .49f, .014f), 13);
        b.Frustum(new Vector3(0, 1.06f, 0), .048f, 0, .10f, 15, 4);
        b.Box(new Vector3(0, .62f, 0), new Vector3(.32f, .055f, .065f), 4);
        b.Frustum(new Vector3(0, .43f, 0), .035f, .035f, .18f, 0, 8);
        b.Frustum(new Vector3(0, .405f, 0), .06f, .04f, .05f, 4, 8);
        for (int i = -1; i <= 1; i += 2) b.Frustum(new Vector3(i * .20f, .30f, -.15f), .033f, 0, .15f, 13, 4);
    }

    static void SubCrystal(Builder b)
    {
        b.Frustum(new Vector3(0, .10f, 0), .33f, .30f, .13f, 2, 8);
        b.Ring(new Vector3(0, .23f, 0), .31f, .24f, .07f, 4, 12);
        b.Frustum(new Vector3(0, .29f, 0), .24f, .20f, .10f, 3, 8);
        b.Frustum(new Vector3(0, .39f, 0), .10f, .20f, .18f, 13, 6);
        b.Frustum(new Vector3(0, .57f, 0), .20f, .15f, .32f, 6, 6);
        b.Frustum(new Vector3(0, .89f, 0), .15f, 0, .26f, 13, 6);
        b.Ring(new Vector3(0, .72f, 0), .25f, .23f, .034f, 4, 12);
        for (int i = 0; i < 4; i++)
        {
            float angle = i * Mathf.PI / 2;
            var p = new Vector3(Mathf.Cos(angle) * .26f, .31f, Mathf.Sin(angle) * .26f);
            b.Frustum(p, .026f, .02f, .29f, 4, 6, new Vector3(-Mathf.Cos(angle) * .15f, 1, -Mathf.Sin(angle) * .15f));
            b.Frustum(p + Vector3.up * .29f, .045f, 0, .10f, 13, 4);
        }
    }
}
#endif
