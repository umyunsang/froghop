using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

/// <summary>
/// Builds SampleScene from scratch: tilemap terrain, hazards, pickups, the player rig,
/// the camera, and the full HUD. Deterministic and re-runnable, so the level is data in
/// this file rather than something hand-dragged and impossible to review.
///
/// One tile is one world unit (16 px art at 16 PPU). A solid placed at tile row Y occupies
/// world Y..Y+1, so a ground block whose top row is -1 has a walking surface at world Y = 0.
/// </summary>
public static class LevelBuilder
{
    private const string Pack = "Assets/Pixel Adventure 1/Assets/";
    private const string Frog = Pack + "Main Characters/Ninja Frog/";
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";

    // Pixel-perfect: 320x180 upscales to 1920x1080 exactly at 6x.
    private const int RefW = 320;
    private const int RefH = 180;
    private const float OrthoSize = RefH / (2f * PixelArtImportFixer.TargetPPU); // 5.625

    // ---- Terrain atlas indices (green grass biome, 3x3 nine-slice) ----
    private const int TopL = 5, TopM = 6, TopR = 7;
    private const int MidL = 22, MidM = 23, MidR = 24;
    private const int BotL = 37, BotM = 38, BotR = 39;
    // Thin brown plank used for the one-way platform.
    private const int PlankL = 31, PlankM = 32, PlankR = 33;

    // ---- Level geometry, in tile coordinates (x, bottomRow, width, height) ----
    private static readonly RectInt[] Solids =
    {
        new RectInt(0,  -3, 18, 3),   // A  start plaza                 surface y = 0
                                      //    gap x18..22  (5 wide)
        new RectInt(23, -3, 16, 3),   // B  spike-head corridor         surface y = 0
        new RectInt(27,  2,  5, 1),   //    P1                          surface y = 3
        new RectInt(33,  5,  5, 1),   //    P2                          surface y = 6
                                      //    gap x39..42  (4 wide)
        new RectInt(43, -3, 17, 3),   // C  trampoline + saw run        surface y = 0
        new RectInt(47,  4,  8, 1),   //    P3  (reached by trampoline) surface y = 5
        new RectInt(54,  7,  5, 1),   //    P4                          surface y = 8
                                      //    gap x60..65  (6 wide, bridged by a one-way plank)
        new RectInt(66, -3, 30, 3),   // D  final ascent                surface y = 0
        // A platform 3 tiles up leaves only 2 tiles of headroom beneath it, so a jump started
        // underneath bonks its base instead of landing on top. Each step therefore begins
        // ~3 tiles right of its natural launch point and is wide enough to be forgiving.
        new RectInt(71,  2,  7, 1),   //    D1                          surface y = 3
        new RectInt(81,  5,  6, 1),   //    D2                          surface y = 6
        new RectInt(90,  6,  6, 1),   //    goal ledge                  surface y = 7
    };

    private const int LevelMinX = 0;
    private const int LevelMaxX = 96;

    private static Sprite[] terrainTiles;
    private static TileBase[] tileAssets;

    // ==================================================================== entry point

    [MenuItem("Tools/2D Game/3. Build Level")]
    public static void Build()
    {
        EnsureTag("Ground");
        EnsureTag("Obstacle");
        int groundLayer = EnsureLayer("Ground");
        EnsureLayer("Hazard");

        UnityEngine.SceneManagement.Scene scene = EditorSceneManager.GetActiveScene();
        ClearScene(scene);

        Physics2D.queriesStartInColliders = false;
        LoadTiles();   // the hill silhouettes need these too, so load before the background

        Camera cam = BuildCamera();
        BuildBackground(cam);
        Tilemap tm = BuildTilemap(groundLayer);
        BuildOneWayPlatform(61, 1, 4, groundLayer);

        GameObject hitPrefab = BuildHitPrefab();
        Player player = BuildPlayer(groundLayer);

        BuildProps(player);

        GameManager gm = BuildUI(player, hitPrefab);
        cam.GetComponent<CameraFollow2D>().target = player.transform;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("[LevelBuilder] SampleScene rebuilt. Tiles painted: " + PaintedCount);
    }

    [MenuItem("Tools/2D Game/0. Build Everything")]
    public static void BuildEverything()
    {
        PixelArtImportFixer.FixAll();
        CharacterAssetBuilder.Build();
        Build();
    }

    // ==================================================================== scene scaffolding

    private static void ClearScene(UnityEngine.SceneManagement.Scene scene)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++) Object.DestroyImmediate(roots[i]);
    }

    private static Camera BuildCamera()
    {
        GameObject go = new GameObject("Main Camera");
        go.tag = "MainCamera";
        Camera cam = go.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = OrthoSize;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.129f, 0.145f, 0.243f);
        cam.nearClipPlane = -100f;
        cam.farClipPlane = 100f;
        go.transform.position = new Vector3(8f, 1.2f, -10f);
        go.AddComponent<AudioListener>();
        go.AddComponent<UniversalAdditionalCameraData>();

        PixelPerfectCamera ppc = go.AddComponent<PixelPerfectCamera>();
        ppc.assetsPPU = PixelArtImportFixer.TargetPPU;
        ppc.refResolutionX = RefW;
        ppc.refResolutionY = RefH;
        // Integer upscaling keeps the art crisp; snapping is left off so movement stays smooth.
        ppc.upscaleRT = false;
        ppc.pixelSnapping = false;
        ppc.cropFrameX = false;
        ppc.cropFrameY = false;

        CameraFollow2D follow = go.AddComponent<CameraFollow2D>();
        follow.useBounds = true;
        follow.boundsMin = new Vector2(LevelMinX, -3f);
        follow.boundsMax = new Vector2(LevelMaxX, 15f);
        follow.offset = new Vector2(0f, 1.5f);

        GameObject light = new GameObject("Global Light 2D");
        Light2D l2d = light.AddComponent<Light2D>();
        l2d.lightType = Light2D.LightType.Global;
        l2d.intensity = 1f;
        return cam;
    }

    private static void BuildBackground(Camera cam)
    {
        GameObject root = new GameObject("Background");

        // Far sky: the 64x64 pack tile, drifting slowly.
        AddBackdrop(root.transform, Pack + "Background/Blue.png", 0.0f, 50, 12f,
                    new Color(0.42f, 0.47f, 0.86f), new Vector2(-0.25f, 0f));

        // Two silhouette ridges built from the same terrain tiles as the playfield, tinted
        // dark and moved at a fraction of camera speed. Real depth from art already in the pack,
        // rather than a second flat colour wash that just muddies the sky.
        // Scaled below 1 so the same 16px tiles read as distant landforms rather than
        // terrain at the player's own scale, and tinted toward the sky with distance.
        //            follow   z   order  tint                              base amp  wave  yOff  scale
        AddHills(root.transform, "Hills_Far", 0.62f, 10f, -900,
                 new Color(0.34f, 0.38f, 0.72f), 5, 2.0f, 15f, -1.0f, 0.5f);
        AddHills(root.transform, "Hills_Near", 0.42f, 11f, -800,
                 new Color(0.22f, 0.25f, 0.52f), 3, 1.5f, 10f, -2.6f, 0.6f);
    }

    /// <summary>
    /// A rolling ridge of terrain tiles used as a parallax silhouette. The profile is a pair of
    /// offset sines so the two ridges never line up into an obvious repeat.
    /// </summary>
    private static void AddHills(Transform parent, string name, float follow, float z,
                                 int sortingOrder, Color tint, int baseHeight,
                                 float amplitude, float wavelength, float yOffset, float scale)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent);
        go.transform.position = new Vector3(0f, yOffset, z);
        go.transform.localScale = new Vector3(scale, scale, 1f);
        // A Tilemap needs a Grid on itself or an ancestor to lay out cells - without one the
        // tiles are stored but nothing is ever drawn.
        Grid grid = go.AddComponent<Grid>();
        grid.cellSize = new Vector3(1f, 1f, 0f);

        GameObject tmGo = new GameObject("Tilemap");
        // worldPositionStays defaults to true, which back-compensates the child's localScale to
        // preserve its world scale - silently cancelling the parent's shrink and rendering the
        // "distant" hills at full playfield size.
        tmGo.transform.SetParent(go.transform, false);
        tmGo.transform.localPosition = Vector3.zero;
        tmGo.transform.localScale = Vector3.one;

        Tilemap tm = tmGo.AddComponent<Tilemap>();
        TilemapRenderer tr = tmGo.AddComponent<TilemapRenderer>();
        tr.sortingOrder = sortingOrder;
        tm.color = tint;

        // The layer scrolls at `follow` of camera speed, so it must span more than the level.
        const int padX = 40;
        HashSet<Vector2Int> solid = new HashSet<Vector2Int>();
        for (int x = -padX; x < LevelMaxX + padX; x++)
        {
            float h = baseHeight
                    + Mathf.Sin(x / wavelength) * amplitude
                    + Mathf.Sin(x / (wavelength * 0.37f)) * amplitude * 0.35f;
            int top = Mathf.RoundToInt(h);
            for (int y = -8; y <= top; y++) solid.Add(new Vector2Int(x, y));
        }
        foreach (Vector2Int c in solid)
        {
            TileBase t = PickTile(solid, c);
            if (t != null) tm.SetTile(new Vector3Int(c.x, c.y, 0), t);
        }

        ParallaxTransform p = go.AddComponent<ParallaxTransform>();
        p.followCamera = follow;
        p.verticalScale = 0.3f;
    }

    private static void AddBackdrop(Transform parent, string path, float factor, int z,
                                    float sortZ, Color tint, Vector2 drift)
    {
        Sprite sprite = PixelArtImportFixer.LoadSingle(path);
        if (sprite == null) return;

        GameObject go = new GameObject("BG " + System.IO.Path.GetFileNameWithoutExtension(path));
        go.transform.SetParent(parent);
        go.transform.position = new Vector3(0f, 0f, sortZ);

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = tint;
        sr.sortingOrder = -1000 + z;
        sr.drawMode = SpriteDrawMode.Tiled;
        sr.tileMode = SpriteTileMode.Continuous;

        ParallaxLayer p = go.AddComponent<ParallaxLayer>();
        p.parallaxFactor = factor;
        p.autoScroll = drift;
        p.tileWorldSize = sprite.rect.width / sprite.pixelsPerUnit;
    }

    // ==================================================================== terrain

    private static int PaintedCount;

    private static Tilemap BuildTilemap(int groundLayer)
    {
        GameObject gridGo = new GameObject("Grid");
        Grid grid = gridGo.AddComponent<Grid>();
        grid.cellSize = new Vector3(1f, 1f, 0f);

        GameObject tmGo = new GameObject("Tilemap");
        tmGo.transform.SetParent(gridGo.transform);
        tmGo.layer = groundLayer;
        tmGo.tag = "Ground";

        Tilemap tm = tmGo.AddComponent<Tilemap>();
        TilemapRenderer tr = tmGo.AddComponent<TilemapRenderer>();
        tr.sortingOrder = 0;

        // Occupancy first, so the auto-tiler can look at neighbours.
        HashSet<Vector2Int> solid = new HashSet<Vector2Int>();
        foreach (RectInt r in Solids)
            for (int x = r.xMin; x < r.xMax; x++)
                for (int y = r.yMin; y < r.yMax; y++)
                    solid.Add(new Vector2Int(x, y));

        PaintedCount = 0;
        foreach (Vector2Int c in solid)
        {
            TileBase tile = PickTile(solid, c);
            if (tile == null) continue;
            tm.SetTile(new Vector3Int(c.x, c.y, 0), tile);
            PaintedCount++;
        }

        // A composite collider merges thousands of per-tile boxes into a handful of edges,
        // which also removes the seam-catching that makes a player snag while running.
        Rigidbody2D rb = tmGo.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Static;

        TilemapCollider2D tc = tmGo.AddComponent<TilemapCollider2D>();
        tc.compositeOperation = Collider2D.CompositeOperation.Merge;

        CompositeCollider2D cc = tmGo.AddComponent<CompositeCollider2D>();
        cc.geometryType = CompositeCollider2D.GeometryType.Polygons;
        cc.generationType = CompositeCollider2D.GenerationType.Synchronous;
        cc.offsetDistance = 0f;

        return tm;
    }

    /// <summary>Nine-slice auto-tiling from the four orthogonal neighbours.</summary>
    private static TileBase PickTile(HashSet<Vector2Int> solid, Vector2Int c)
    {
        bool up = solid.Contains(c + Vector2Int.up);
        bool down = solid.Contains(c + Vector2Int.down);
        bool left = solid.Contains(c + Vector2Int.left);
        bool right = solid.Contains(c + Vector2Int.right);

        int idx;
        if (!up) idx = !left ? TopL : (!right ? TopR : TopM);
        else if (!down) idx = !left ? BotL : (!right ? BotR : BotM);
        else idx = !left ? MidL : (!right ? MidR : MidM);
        return Tile(idx);
    }

    private static void LoadTiles()
    {
        tileAssets = new TileBase[200];
        for (int i = 0; i < tileAssets.Length; i++)
        {
            string p = $"{Pack}Terrain/Terrain Sliced (16x16)_{i}.asset";
            tileAssets[i] = AssetDatabase.LoadAssetAtPath<TileBase>(p);
        }
        terrainTiles = PixelArtImportFixer.LoadFrames(Pack + "Terrain/Terrain Sliced (16x16).png");
    }

    private static TileBase Tile(int i)
    {
        if (tileAssets == null || i < 0 || i >= tileAssets.Length) return null;
        return tileAssets[i];
    }

    private static Sprite TerrainSprite(int i)
    {
        if (terrainTiles == null || i < 0 || i >= terrainTiles.Length) return null;
        return terrainTiles[i];
    }

    /// <summary>A drop-through plank built from three sprites under one effector collider.</summary>
    private static void BuildOneWayPlatform(int x, int row, int width, int groundLayer)
    {
        GameObject go = new GameObject("OneWayPlatform");
        go.layer = groundLayer;
        go.tag = "Ground";
        go.transform.position = new Vector3(x + width * 0.5f, row + 0.5f, 0f);

        for (int i = 0; i < width; i++)
        {
            int idx = i == 0 ? PlankL : (i == width - 1 ? PlankR : PlankM);
            Sprite s = TerrainSprite(idx);
            if (s == null) continue;
            GameObject part = new GameObject("plank_" + i);
            part.transform.SetParent(go.transform);
            part.transform.localPosition = new Vector3(-width * 0.5f + i + 0.5f, 0f, 0f);
            SpriteRenderer sr = part.AddComponent<SpriteRenderer>();
            sr.sprite = s;
            sr.sortingOrder = 1;
        }

        BoxCollider2D col = go.AddComponent<BoxCollider2D>();
        col.size = new Vector2(width, 0.35f);
        col.offset = new Vector2(0f, 0.15f);
        go.AddComponent<PlatformEffector2D>();
        go.AddComponent<OneWayPlatform>();
    }

    // ==================================================================== player

    private static Player BuildPlayer(int groundLayer)
    {
        // Clear of the start flag, which is 4 units wide with a bottom-centre pivot.
        GameObject go = new GameObject("Character");
        go.transform.position = new Vector3(5f, 0.2f, 0f);

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        Sprite[] idle = PixelArtImportFixer.LoadFrames(Frog + "Idle (32x32).png");
        if (idle.Length > 0) sr.sprite = idle[0];
        sr.sortingOrder = 20;

        Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
        rb.gravityScale = 2f;                      // practice PDF value
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        // The frog art sits in the lower middle of its 32x32 frame; the collider hugs the body
        // rather than the frame, so the player does not clip corners they visually cleared.
        BoxCollider2D col = go.AddComponent<BoxCollider2D>();
        col.size = new Vector2(0.85f, 1.0f);
        col.offset = new Vector2(0f, 0.5f);

        // Zero friction stops the player sticking to walls mid-jump; the script owns all
        // horizontal velocity, so surface drag would only fight it.
        const string matPath = "Assets/PlayerNoFriction.physicsMaterial2D";
        PhysicsMaterial2D noFriction = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(matPath);
        if (noFriction == null)
        {
            noFriction = new PhysicsMaterial2D("PlayerNoFriction");
            AssetDatabase.CreateAsset(noFriction, matPath);
        }
        noFriction.friction = 0f;
        noFriction.bounciness = 0f;
        EditorUtility.SetDirty(noFriction);
        col.sharedMaterial = noFriction;

        Animator anim = go.AddComponent<Animator>();
        anim.runtimeAnimatorController =
            AssetDatabase.LoadAssetAtPath<AnimatorController>(CharacterAssetBuilder.ControllerPath);
        anim.applyRootMotion = false;
        anim.updateMode = AnimatorUpdateMode.Normal;
        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        Player p = go.AddComponent<Player>();
        p.HP = 3;
        p.speed = 6.5f;
        p.JumpPower = 11.5f;      // ~3.4 tiles of rise at gravityScale 2
        p.groundMask = 1 << groundLayer;
        p.groundCheckWidth = 0.7f;
        p.killPlaneY = -11f;

        // Foot dust.
        p.dustJump = MakeDust(go.transform, "DustJump", 8, 2.2f);
        p.dustLand = MakeDust(go.transform, "DustLand", 12, 2.8f);
        p.dustRun = MakeDust(go.transform, "DustRun", 3, 1.2f);
        return p;
    }

    private static ParticleSystem MakeDust(Transform parent, string name, int burst, float speed)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent);
        go.transform.localPosition = new Vector3(0f, 0.05f, 0f);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule m = ps.main;
        m.duration = 0.5f;
        m.loop = false;
        m.playOnAwake = false;
        m.startLifetime = 0.32f;
        m.startSpeed = speed;
        m.startSize = 0.14f;
        m.gravityModifier = 0.35f;
        m.startColor = new Color(1f, 1f, 1f, 0.8f);
        m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.maxParticles = 40;

        ParticleSystem.EmissionModule e = ps.emission;
        e.rateOverTime = 0f;
        e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)burst) });

        ParticleSystem.ShapeModule sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Circle;
        sh.radius = 0.22f;
        sh.arc = 180f;

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = FadeOut(new Color(0.92f, 0.92f, 0.85f));

        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.1f));

        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
        r.material = DefaultParticleMaterial();
        r.sortingOrder = 19;
        return ps;
    }

    private static ParticleSystem.MinMaxGradient FadeOut(Color c)
    {
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        return new ParticleSystem.MinMaxGradient(g);
    }

    private static Material cachedParticleMat;
    private static Material DefaultParticleMaterial()
    {
        if (cachedParticleMat != null) return cachedParticleMat;
        cachedParticleMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/FX_Particle.mat");
        if (cachedParticleMat == null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            cachedParticleMat = new Material(sh);
            cachedParticleMat.name = "FX_Particle";
            AssetDatabase.CreateAsset(cachedParticleMat, "Assets/FX_Particle.mat");
        }
        return cachedParticleMat;
    }

    /// <summary>
    /// The practice-PDF hit burst: red circle emitter, radius 0.3, alpha fading to zero.
    /// Saved to Assets/Hit.prefab so the reference on the Player inspector still resolves.
    /// </summary>
    private static GameObject BuildHitPrefab()
    {
        GameObject go = new GameObject("Hit");
        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule m = ps.main;
        m.duration = 0.6f;
        m.loop = false;
        m.playOnAwake = true;
        m.startLifetime = 0.5f;
        m.startSpeed = 5f;
        m.startSize = 0.16f;
        m.gravityModifier = 0.6f;
        m.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.36f, 0.36f), new Color(1f, 0.75f, 0.3f));
        m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.maxParticles = 200;

        ParticleSystem.EmissionModule e = ps.emission;
        e.rateOverTime = 0f;
        e.SetBursts(new[] { new ParticleSystem.Burst(0f, 26) });

        ParticleSystem.ShapeModule sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Circle;
        sh.radius = 0.3f;                 // practice PDF value
        sh.radiusThickness = 1f;

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = FadeOut(Color.white);

        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
        r.material = DefaultParticleMaterial();
        r.sortingOrder = 40;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, "Assets/Hit.prefab");
        Object.DestroyImmediate(go);
        return prefab;
    }

    // ==================================================================== props and hazards

    private static void BuildProps(Player player)
    {
        Transform root = new GameObject("Level").transform;

        // ---- Checkpoints ----
        Prop(root, "Start (Idle)", Pack + "Items/Checkpoints/Start/Start (Idle).png",
             new Vector3(2f, 0f, 0f), 5);

        GameObject endGo = Prop(root, "End (Idle)", Pack + "Items/Checkpoints/End/End (Idle).png",
                                new Vector3(93f, 7f, 0f), 5);
        BoxCollider2D endCol = endGo.AddComponent<BoxCollider2D>();
        endCol.isTrigger = true;
        endCol.size = new Vector2(2f, 2.4f);
        endCol.offset = new Vector2(0f, 1.2f);
        LevelGoal goal = endGo.AddComponent<LevelGoal>();
        goal.idleFrames = PixelArtImportFixer.LoadFrames(Pack + "Items/Checkpoints/End/End (Idle).png");
        goal.pressedFrames = PixelArtImportFixer.LoadFrames(Pack + "Items/Checkpoints/End/End (Pressed) (64x64).png");

        MakeCheckpoint(root, new Vector3(45f, 0f, 0f));
        MakeCheckpoint(root, new Vector3(67f, 0f, 0f));

        // ---- Trampolines ----
        MakeTrampoline(root, new Vector3(45.5f, 0f, 0f), 16f);   // ~6.5 units of rise, clears P3 at y = 5
        MakeTrampoline(root, new Vector3(79.5f, 0f, 0f), 16f);   // recovery route back up to D2

        // ---- Spike heads ----
        // Each starts at its low point. The collider is 2.5 tall around a centre pivot, so a
        // base of y=1.4 puts the underside at 0.15 - low enough to gate a player walking the
        // floor - and the retracted top clears the platform above it.
        // Kept clear of P1's left edge; the 2.6-wide collider would otherwise clip its corner.
        MakeSpikeHead(root, new Vector3(24.5f, 1.4f, 0f), SpikeHead.Axis.Vertical, 4.6f, 7f);
        // Runs above P2 rather than through it, threatening anyone standing on the platform.
        MakeSpikeHead(root, new Vector3(30f, 8f, 0f), SpikeHead.Axis.Horizontal, 6f, 5f);
        MakeSpikeHead(root, new Vector3(49f, 11f, 0f), SpikeHead.Axis.Horizontal, 6f, 5f);
        MakeSpikeHead(root, new Vector3(74f, 4.4f, 0f), SpikeHead.Axis.Vertical, 4f, 6f);

        // ---- Saws ----
        MakeSaw(root, new Vector3(47f, 1.1f, 0f), Saw.Axis.Horizontal, 9f, 3.2f);
        MakeSaw(root, new Vector3(83f, 1.1f, 0f), Saw.Axis.Horizontal, 7f, 2.6f);

        // ---- Static spikes ----
        for (int i = 0; i < 3; i++) MakeSpikes(root, new Vector3(56f + i, 0f, 0f));
        for (int i = 0; i < 2; i++) MakeSpikes(root, new Vector3(68f + i, 0f, 0f));

        // ---- Crates: scenery that also doubles as a step ----
        MakeBox(root, "Box1", new Vector3(8.5f, 0f, 0f));
        MakeBox(root, "Box2", new Vector3(10.1f, 0f, 0f));
        MakeBox(root, "Box3", new Vector3(9.3f, 1.45f, 0f));

        // ---- Fruit ----
        Vector3[] fruitSpots =
        {
            new Vector3(13.5f, 1.2f, 0f), new Vector3(16f, 1.2f, 0f),
            new Vector3(29f, 4.2f, 0f), new Vector3(35f, 7.2f, 0f),
            new Vector3(49.5f, 6.2f, 0f), new Vector3(56f, 9.2f, 0f),
            new Vector3(63f, 3.2f, 0f),
            new Vector3(74.5f, 4.2f, 0f), new Vector3(84f, 7.2f, 0f),
            new Vector3(92.5f, 8.2f, 0f),
        };
        string[] fruitKinds = { "Apple", "Bananas", "Cherries", "Kiwi", "Melon",
                                "Orange", "Pineapple", "Strawberry" };
        for (int i = 0; i < fruitSpots.Length; i++)
            MakeFruit(root, fruitKinds[i % fruitKinds.Length], fruitSpots[i]);
    }

    private static GameObject Prop(Transform parent, string name, string spritePath,
                                   Vector3 pos, int order)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent);
        go.transform.position = pos;
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = PixelArtImportFixer.LoadSingle(spritePath);
        sr.sortingOrder = order;
        return go;
    }

    private static void MakeCheckpoint(Transform parent, Vector3 pos)
    {
        string dir = Pack + "Items/Checkpoints/Checkpoint/";
        GameObject go = Prop(parent, "Checkpoint", dir + "Checkpoint (No Flag).png", pos, 4);
        BoxCollider2D c = go.AddComponent<BoxCollider2D>();
        c.isTrigger = true;
        c.size = new Vector2(1.2f, 3f);
        c.offset = new Vector2(0f, 1.5f);

        CheckpointFlag f = go.AddComponent<CheckpointFlag>();
        f.noFlag = PixelArtImportFixer.LoadSingle(dir + "Checkpoint (No Flag).png");
        f.flagOutFrames = PixelArtImportFixer.LoadFrames(dir + "Checkpoint (Flag Out) (64x64).png");
        f.flagIdleFrames = PixelArtImportFixer.LoadFrames(dir + "Checkpoint (Flag Idle)(64x64).png");
    }

    private static void MakeTrampoline(Transform parent, Vector3 pos, float power)
    {
        string dir = Pack + "Traps/Trampoline/";
        GameObject go = Prop(parent, "Trampoline", dir + "Idle.png", pos, 6);
        BoxCollider2D c = go.AddComponent<BoxCollider2D>();
        c.isTrigger = true;
        c.size = new Vector2(1.7f, 0.7f);
        c.offset = new Vector2(0f, 0.45f);

        Trampoline t = go.AddComponent<Trampoline>();
        t.bouncePower = power;
        t.idle = PixelArtImportFixer.LoadSingle(dir + "Idle.png");
        t.jumpFrames = PixelArtImportFixer.LoadFrames(dir + "Jump (28x28).png");
    }

    private static void MakeSpikeHead(Transform parent, Vector3 pos, SpikeHead.Axis axis,
                                      float distance, float speed)
    {
        string dir = Pack + "Traps/Spike Head/";
        GameObject go = Prop(parent, "Spike_Head", dir + "Idle.png", pos, 8);
        go.tag = "Obstacle";

        BoxCollider2D c = go.AddComponent<BoxCollider2D>();
        c.isTrigger = true;
        c.size = new Vector2(2.6f, 2.5f);

        SpikeHead s = go.AddComponent<SpikeHead>();
        s.axis = axis;
        s.distance = distance;
        s.speed = speed;
        s.idle = PixelArtImportFixer.LoadSingle(dir + "Idle.png");
        s.blink = PixelArtImportFixer.LoadFrames(dir + "Blink (54x52).png");
        s.hitTop = PixelArtImportFixer.LoadFrames(dir + "Top Hit (54x52).png");
        s.hitBottom = PixelArtImportFixer.LoadFrames(dir + "Bottom Hit (54x52).png");
        s.hitLeft = PixelArtImportFixer.LoadFrames(dir + "Left Hit (54x52).png");
        s.hitRight = PixelArtImportFixer.LoadFrames(dir + "Right Hit (54x52).png");
    }

    private static void MakeSaw(Transform parent, Vector3 pos, Saw.Axis axis,
                                float distance, float speed)
    {
        string dir = Pack + "Traps/Saw/";
        GameObject go = new GameObject("Saw");
        go.transform.SetParent(parent);
        go.transform.position = pos;
        go.tag = "Obstacle";

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        Sprite[] frames = PixelArtImportFixer.LoadFrames(dir + "On (38x38).png");
        if (frames.Length > 0) sr.sprite = frames[0];
        sr.sortingOrder = 8;

        CircleCollider2D c = go.AddComponent<CircleCollider2D>();
        c.isTrigger = true;
        c.radius = 1.0f;

        Saw s = go.AddComponent<Saw>();
        s.axis = axis;
        s.distance = distance;
        s.speed = speed;
        s.frames = frames;
    }

    private static void MakeSpikes(Transform parent, Vector3 pos)
    {
        GameObject go = Prop(parent, "Spikes", Pack + "Traps/Spikes/Idle.png", pos, 7);
        go.tag = "Obstacle";
        BoxCollider2D c = go.AddComponent<BoxCollider2D>();
        c.isTrigger = true;
        c.size = new Vector2(0.95f, 0.4f);
        c.offset = new Vector2(0f, 0.2f);
    }

    private static void MakeBox(Transform parent, string kind, Vector3 pos)
    {
        GameObject go = Prop(parent, kind, Pack + "Items/Boxes/" + kind + "/Idle.png", pos, 6);
        BoxCollider2D c = go.AddComponent<BoxCollider2D>();
        c.size = new Vector2(1.7f, 1.45f);
        c.offset = new Vector2(0f, 0.73f);
        go.layer = LayerMask.NameToLayer("Ground");
        go.tag = "Ground";
    }

    private static void MakeFruit(Transform parent, string kind, Vector3 pos)
    {
        GameObject go = new GameObject("Fruit_" + kind);
        go.transform.SetParent(parent);
        go.transform.position = pos;

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = 10;

        CircleCollider2D c = go.AddComponent<CircleCollider2D>();
        c.isTrigger = true;
        c.radius = 0.55f;

        // The pack draws fruit in a 32x32 frame, the same size as the player; scale it down
        // so a pickup does not read as heavy as the character.
        go.transform.localScale = Vector3.one * 0.8f;

        Fruit f = go.AddComponent<Fruit>();
        f.idleFrames = PixelArtImportFixer.LoadFrames(Pack + "Items/Fruits/" + kind + ".png");
        f.collectedFrames = PixelArtImportFixer.LoadFrames(Pack + "Items/Fruits/Collected.png");
        if (f.idleFrames.Length > 0) sr.sprite = f.idleFrames[0];
    }

    // ==================================================================== UI

    private static GameManager BuildUI(Player player, GameObject hitPrefab)
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        GameObject canvasGo = new GameObject("Canvas");
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        GameObject esGo = new GameObject("EventSystem");
        esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
        esGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

        // ---------------- HUD ----------------
        // Face icon + the practice-PDF legacy HP label.
        Image face = MakeImage(canvasGo.transform, "Image", new Vector2(0f, 1f),
                               new Vector2(60f, -55f), new Vector2(72f, 72f));
        Sprite[] idle = PixelArtImportFixer.LoadFrames(Frog + "Idle (32x32).png");
        if (idle.Length > 0) face.sprite = idle[0];
        face.preserveAspect = true;

        Text hp = MakeText(canvasGo.transform, "Text (Legacy)", "HP :3", font, 40,
                           TextAnchor.MiddleLeft, new Vector2(0f, 1f),
                           new Vector2(230f, -55f), new Vector2(260f, 60f));

        Image[] hearts = new Image[3];
        Sprite heartFull = MakeHeartSprite(true);
        Sprite heartEmpty = MakeHeartSprite(false);
        for (int i = 0; i < 3; i++)
        {
            hearts[i] = MakeImage(canvasGo.transform, "Heart" + i, new Vector2(0f, 1f),
                                  new Vector2(115f + i * 54f, -112f), new Vector2(48f, 48f));
            hearts[i].sprite = heartFull;
            hearts[i].preserveAspect = true;
        }

        Image fruitIcon = MakeImage(canvasGo.transform, "FruitIcon", new Vector2(1f, 1f),
                                    new Vector2(-250f, -58f), new Vector2(60f, 60f));
        Sprite[] apple = PixelArtImportFixer.LoadFrames(Pack + "Items/Fruits/Apple.png");
        if (apple.Length > 0) fruitIcon.sprite = apple[0];
        fruitIcon.preserveAspect = true;

        Text fruitText = MakeText(canvasGo.transform, "FruitText", "0 / 0", font, 40,
                                  TextAnchor.MiddleLeft, new Vector2(1f, 1f),
                                  new Vector2(-140f, -58f), new Vector2(200f, 60f));

        Text timer = MakeText(canvasGo.transform, "TimerText", "00:00.00", font, 34,
                              TextAnchor.MiddleRight, new Vector2(1f, 1f),
                              new Vector2(-190f, -116f), new Vector2(300f, 50f));
        timer.color = new Color(1f, 1f, 1f, 0.75f);

        Text toast = MakeText(canvasGo.transform, "ToastText", "", font, 52,
                              TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f),
                              new Vector2(0f, 220f), new Vector2(1200f, 90f));
        toast.color = new Color(1f, 0.92f, 0.4f);
        AddShadow(toast, 5f);

        // ---------------- GameOver panel (practice PDF structure) ----------------
        GameObject gameOver = new GameObject("GameOver", typeof(RectTransform));
        gameOver.transform.SetParent(canvasGo.transform, false);
        Stretch(gameOver.GetComponent<RectTransform>());

        Image dim = MakeImage(gameOver.transform, "Image", Vector2.zero, Vector2.zero, Vector2.zero);
        Stretch(dim.rectTransform);
        dim.color = new Color(0f, 0f, 0f, 210f / 255f);   // practice PDF value
        dim.sprite = null;

        Text over = MakeText(gameOver.transform, "Text (Legacy)", "GAMEOVER", font, 130,
                             TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f),
                             new Vector2(0f, 150f), new Vector2(1400f, 200f));
        AddShadow(over, 10f);

        Text retry = MakeText(gameOver.transform, "Text (Legacy) Retry", "Retry?", font, 75,
                              TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f),
                              new Vector2(0f, -10f), new Vector2(800f, 120f));

        GameObject btnGo = new GameObject("Button (Legacy)", typeof(RectTransform));
        btnGo.transform.SetParent(gameOver.transform, false);
        RectTransform btnRt = btnGo.GetComponent<RectTransform>();
        btnRt.anchorMin = btnRt.anchorMax = new Vector2(0.5f, 0.5f);
        btnRt.anchoredPosition = new Vector2(0f, -170f);
        btnRt.sizeDelta = new Vector2(360f, 110f);
        Image btnImg = btnGo.AddComponent<Image>();
        btnImg.color = Color.white;
        Button btn = btnGo.AddComponent<Button>();
        btn.targetGraphic = btnImg;

        Text btnLabel = MakeText(btnGo.transform, "Text (Legacy)", "RETRY", font, 48,
                                 TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f),
                                 Vector2.zero, new Vector2(360f, 110f));
        btnLabel.color = Color.black;

        // Practice PDF: On Click -> Character -> Player.Retry_Button()
        UnityEventTools.AddPersistentListener(btn.onClick,
            new UnityEngine.Events.UnityAction(player.Retry_Button));

        gameOver.SetActive(false);

        // ---------------- Clear panel ----------------
        GameObject clear = new GameObject("ClearPanel", typeof(RectTransform));
        clear.transform.SetParent(canvasGo.transform, false);
        Stretch(clear.GetComponent<RectTransform>());

        Image clearDim = MakeImage(clear.transform, "Image", Vector2.zero, Vector2.zero, Vector2.zero);
        Stretch(clearDim.rectTransform);
        clearDim.color = new Color(0.03f, 0.06f, 0.12f, 0.82f);

        Text clearTitle = MakeText(clear.transform, "Title", "LEVEL CLEAR!", font, 120,
                                   TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f),
                                   new Vector2(0f, 210f), new Vector2(1600f, 190f));
        clearTitle.color = new Color(1f, 0.85f, 0.25f);
        AddShadow(clearTitle, 10f);

        Text clearStats = MakeText(clear.transform, "Stats", "", font, 52,
                                   TextAnchor.UpperCenter, new Vector2(0.5f, 0.5f),
                                   new Vector2(0f, -20f), new Vector2(900f, 260f));
        clearStats.lineSpacing = 1.35f;

        GameObject clearBtnGo = new GameObject("PlayAgain", typeof(RectTransform));
        clearBtnGo.transform.SetParent(clear.transform, false);
        RectTransform cbRt = clearBtnGo.GetComponent<RectTransform>();
        cbRt.anchorMin = cbRt.anchorMax = new Vector2(0.5f, 0.5f);
        cbRt.anchoredPosition = new Vector2(0f, -290f);
        cbRt.sizeDelta = new Vector2(420f, 110f);
        Image cbImg = clearBtnGo.AddComponent<Image>();
        cbImg.color = Color.white;
        Button cbBtn = clearBtnGo.AddComponent<Button>();
        cbBtn.targetGraphic = cbImg;
        Text cbLabel = MakeText(clearBtnGo.transform, "Text (Legacy)", "PLAY AGAIN", font, 44,
                                TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f),
                                Vector2.zero, new Vector2(420f, 110f));
        cbLabel.color = Color.black;
        UnityEventTools.AddPersistentListener(cbBtn.onClick,
            new UnityEngine.Events.UnityAction(player.Retry_Button));

        clear.SetActive(false);

        // ---------------- Pause panel ----------------
        GameObject pause = new GameObject("PausePanel", typeof(RectTransform));
        pause.transform.SetParent(canvasGo.transform, false);
        Stretch(pause.GetComponent<RectTransform>());
        Image pauseDim = MakeImage(pause.transform, "Image", Vector2.zero, Vector2.zero, Vector2.zero);
        Stretch(pauseDim.rectTransform);
        pauseDim.color = new Color(0f, 0f, 0f, 0.6f);
        Text pauseTitle = MakeText(pause.transform, "Title", "PAUSED", font, 110,
                                   TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f),
                                   new Vector2(0f, 60f), new Vector2(1400f, 180f));
        AddShadow(pauseTitle, 8f);
        Text pauseHint = MakeText(pause.transform, "Hint",
                                  "ESC  resume        R  restart", font, 40,
                                  TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f),
                                  new Vector2(0f, -80f), new Vector2(1400f, 90f));
        pauseHint.color = new Color(1f, 1f, 1f, 0.8f);
        pause.SetActive(false);

        // ---------------- Controls hint + fade ----------------
        Text controls = MakeText(canvasGo.transform, "Controls",
                                 "ARROWS  move      SPACE  jump      ESC  pause      R  restart",
                                 font, 30, TextAnchor.LowerCenter, new Vector2(0.5f, 0f),
                                 new Vector2(0f, 40f), new Vector2(1600f, 60f));
        controls.color = new Color(1f, 1f, 1f, 0.45f);

        Image fade = MakeImage(canvasGo.transform, "Fade", Vector2.zero, Vector2.zero, Vector2.zero);
        Stretch(fade.rectTransform);
        fade.color = Color.black;
        fade.raycastTarget = false;

        // ---------------- Wire it together ----------------
        GameObject gmGo = new GameObject("GameManager");
        GameManager gm = gmGo.AddComponent<GameManager>();
        gm.hpText = hp;
        gm.gameOverObj = gameOver;
        gm.hearts = hearts;
        gm.heartFull = heartFull;
        gm.heartEmpty = heartEmpty;
        gm.fruitText = fruitText;
        gm.timerText = timer;
        gm.toastText = toast;
        gm.clearPanel = clear;
        gm.clearStatsText = clearStats;
        gm.pausePanel = pause;
        gm.fadeImage = fade;

        gmGo.AddComponent<AudioManagerProc>();
        gmGo.AddComponent<Juice>();

        player.Hp_Text = hp;
        player.GameOverObj = gameOver;
        player.Hit_Prefab = hitPrefab;
        return gm;
    }

    // ---------------- small UI helpers ----------------

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static Image MakeImage(Transform parent, string name, Vector2 anchor,
                                   Vector2 pos, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        Image img = go.AddComponent<Image>();
        img.raycastTarget = false;
        return img;
    }

    private static Text MakeText(Transform parent, string name, string content, Font font,
                                 int size, TextAnchor align, Vector2 anchor,
                                 Vector2 pos, Vector2 rect)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = rect;

        Text t = go.AddComponent<Text>();
        t.text = content;
        t.font = font;
        t.fontSize = size;
        t.alignment = align;
        t.color = Color.white;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    private static void AddShadow(Text t, float distance)
    {
        Shadow s = t.gameObject.AddComponent<Shadow>();
        s.effectColor = new Color(0f, 0f, 0f, 0.85f);
        s.effectDistance = new Vector2(distance, -distance);   // practice PDF: 10, -10
    }

    /// <summary>
    /// The asset pack has no heart icon, so draw a 9x8 pixel-art heart and save it as a sprite.
    /// </summary>
    private static Sprite MakeHeartSprite(bool filled)
    {
        const int W = 9, H = 8;
        string path = "Assets/Sprites/UI_Heart_" + (filled ? "Full" : "Empty") + ".png";
        System.IO.Directory.CreateDirectory("Assets/Sprites");

        // rows top -> bottom; 1 = body, 2 = highlight, 0 = empty
        int[,] shape =
        {
            { 0,1,1,0,0,0,1,1,0 },
            { 1,2,2,1,0,1,1,1,1 },
            { 1,2,1,1,1,1,1,1,1 },
            { 1,1,1,1,1,1,1,1,1 },
            { 0,1,1,1,1,1,1,1,0 },
            { 0,0,1,1,1,1,1,0,0 },
            { 0,0,0,1,1,1,0,0,0 },
            { 0,0,0,0,1,0,0,0,0 },
        };

        Texture2D tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        Color body = filled ? new Color32(220, 45, 70, 255) : new Color32(70, 74, 96, 255);
        Color hi = filled ? new Color32(255, 122, 140, 255) : new Color32(96, 100, 124, 255);

        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int v = shape[H - 1 - y, x];   // texture origin is bottom-left
                tex.SetPixel(x, y, v == 0 ? Color.clear : (v == 2 ? hi : body));
            }
        tex.Apply();

        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti != null)
        {
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.filterMode = FilterMode.Point;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.spritePixelsPerUnit = PixelArtImportFixer.TargetPPU;
            ti.mipmapEnabled = false;
            ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // ==================================================================== project settings

    private static void EnsureTag(string tag)
    {
        SerializedObject so = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty tags = so.FindProperty("tags");
        for (int i = 0; i < tags.arraySize; i++)
            if (tags.GetArrayElementAtIndex(i).stringValue == tag) return;

        tags.InsertArrayElementAtIndex(tags.arraySize);
        tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
        so.ApplyModifiedProperties();
    }

    /// <summary>Find or create a user layer and return its index.</summary>
    private static int EnsureLayer(string layer)
    {
        SerializedObject so = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty layers = so.FindProperty("layers");

        for (int i = 0; i < layers.arraySize; i++)
            if (layers.GetArrayElementAtIndex(i).stringValue == layer) return i;

        // 0-7 are reserved by Unity.
        for (int i = 8; i < layers.arraySize; i++)
        {
            SerializedProperty e = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(e.stringValue))
            {
                e.stringValue = layer;
                so.ApplyModifiedProperties();
                return i;
            }
        }
        Debug.LogError("[LevelBuilder] No free layer slot for " + layer);
        return 0;
    }
}
