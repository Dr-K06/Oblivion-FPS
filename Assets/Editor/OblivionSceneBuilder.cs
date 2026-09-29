#if UNITY_EDITOR
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Oblivion - OblivionSceneBuilder (script de EDITOR)
/// Monta a cena FPS inteira com um clique: greybox, managers, Player, câmera,
/// lanterna, HUD, menu inicial, painel de vitória e saída do labirinto.
///
/// COMO USAR:
/// 1. Coloque este arquivo em Assets/Editor/ (crie a pasta "Editor" se não existir).
/// 2. Abra a cena FPS_Prototype.
/// 3. Menu: Oblivion > Montar Cena Completa.
/// </summary>
public static class OblivionSceneBuilder
{
    const string DefaultScenePath = "Assets/FPS_Prototype.unity";

    [MenuItem("Oblivion/Montar Cena Completa")]
    public static void Build()
    {
        if (GameObject.Find("Player") != null || GameObject.Find("GameManager") != null)
        {
            EditorUtility.DisplayDialog("Oblivion",
                "A cena já parece montada (existe Player ou GameManager).\n" +
                "Apague esses objetos antes de rodar de novo.", "OK");
            return;
        }

        Camera cam = SetupEnvironment();

        if (GameObject.Find("Floor") == null) BuildGreybox();

        GameController gc = BuildManagers();
        GameObject player = BuildPlayer(cam);
        Flashlight flashlight = BuildFlashlight(cam);
        BuildExit();
        BuildHUD(gc, flashlight, cam);

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        string path = string.IsNullOrEmpty(scene.path) ? DefaultScenePath : scene.path;
        EditorSceneManager.SaveScene(scene, path);
        AddToBuildSettings(path);

        Selection.activeGameObject = player;
        EditorUtility.DisplayDialog("Oblivion",
            "Cena montada e salva!\n\nAperte Play, clique em INICIAR e teste:\n" +
            "WASD = andar | Shift = correr | F = lanterna | Mouse = olhar", "OK");
    }

    // ── Ambiente ─────────────────────────────────────────────────────────
    static Camera SetupEnvironment()
    {
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = Color.black;

        Camera cam = Camera.main;
        if (cam == null)
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            cam = go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();
        }
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.nearClipPlane = 0.05f;
        return cam;
    }

    // ── Greybox: corredor 3 m x 12 m, paredes de 3 m ─────────────────────
    static void BuildGreybox()
    {
        var root = new GameObject("Greybox").transform;

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.SetParent(root);
        floor.transform.position = Vector3.zero;
        floor.transform.localScale = new Vector3(0.4f, 1f, 1.3f); // 4 x 13 m

        MakeCube("Wall_L",     root, new Vector3(-1.75f, 1.5f, 0f),    new Vector3(0.5f, 3f, 12f));
        MakeCube("Wall_R",     root, new Vector3( 1.75f, 1.5f, 0f),    new Vector3(0.5f, 3f, 12f));
        MakeCube("Wall_End",   root, new Vector3(0f, 1.5f,  6.25f),    new Vector3(3.5f, 3f, 0.5f));
        MakeCube("Wall_Start", root, new Vector3(0f, 1.5f, -6.25f),    new Vector3(3.5f, 3f, 0.5f));
        MakeCube("Ceiling",    root, new Vector3(0f, 3.25f, 0f),       new Vector3(4f, 0.5f, 13f));
    }

    static void MakeCube(string name, Transform parent, Vector3 pos, Vector3 scale)
    {
        var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
        c.name = name;
        c.transform.SetParent(parent);
        c.transform.position = pos;
        c.transform.localScale = scale;
    }

    // ── Managers ─────────────────────────────────────────────────────────
    static GameController BuildManagers()
    {
        var gm = new GameObject("GameManager");
        var gc = gm.AddComponent<GameController>();
        gm.AddComponent<SanitySystem>();

        var am = new GameObject("AudioManager");
        am.AddComponent<global::AudioManager>();
        return gc;
    }

    // ── Player + câmera ──────────────────────────────────────────────────
    static GameObject BuildPlayer(Camera cam)
    {
        var player = new GameObject("Player");
        player.tag = "Player";
        player.transform.position = new Vector3(0f, 0.1f, -4.5f);

        var cc = player.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.4f;
        cc.center = new Vector3(0f, 0.9f, 0f);
        player.AddComponent<PlayerController>();

        cam.transform.SetParent(player.transform, false);
        cam.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        cam.transform.localRotation = Quaternion.identity;

        var look = cam.gameObject.AddComponent<MouseLook>();
        Set(look, "playerBody", player.transform);

        var fx = cam.gameObject.AddComponent<CameraEffects>();
        Set(fx, "playerController", cc);

        return player;
    }

    // ── Lanterna ─────────────────────────────────────────────────────────
    static Flashlight BuildFlashlight(Camera cam)
    {
        var go = new GameObject("Flashlight");
        go.transform.SetParent(cam.transform, false);

        var l = go.AddComponent<Light>();
        l.type = LightType.Spot;
        l.range = 15f;
        l.spotAngle = 45f;
        l.intensity = 2f;
        l.color = new Color(1f, 0.95f, 0.8f);
        l.shadows = LightShadows.Soft;

        return go.AddComponent<Flashlight>();
    }

    // ── Saída do labirinto ───────────────────────────────────────────────
    static void BuildExit()
    {
        var exit = GameObject.CreatePrimitive(PrimitiveType.Cube);
        exit.name = "MazeExit";
        exit.transform.position = new Vector3(0f, 1.25f, 5.65f);
        exit.transform.localScale = new Vector3(2f, 2.5f, 0.3f);
        var me = exit.AddComponent<MazeExit>();

        var lg = new GameObject("ExitLight");
        lg.transform.SetParent(exit.transform, false);
        lg.transform.localPosition = new Vector3(0f, 0f, -4f);
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point;
        l.range = 6f;
        l.intensity = 2f;
        l.color = new Color(0.25f, 1f, 0.55f);

        Set(me, "exitLight", l);
        Set(me, "exitRenderers", new Renderer[] { exit.GetComponent<Renderer>() });
    }

    // ── HUD, menu e painel de vitória ────────────────────────────────────
    static void BuildHUD(GameController gc, Flashlight flashlight, Camera cam)
    {
        if (UnityEngine.Object.FindObjectOfType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        // O GameController procura EXATAMENTE por "GameCanvas" e "SanityBarBG".
        var canvasGO = new GameObject("GameCanvas", typeof(RectTransform));
        canvasGO.layer = LayerMask.NameToLayer("UI");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();
        Transform cv = canvasGO.transform;

        // Overlay preto fullscreen (alpha 0, não bloqueia cliques)
        var overlayRT = NewUI("ScreenOverlay", cv);
        Stretch(overlayRT);
        Image overlay = AddImage(overlayRT, new Color(0f, 0f, 0f, 0f), false);

        // Mira (ponto branco central)
        var crossRT = NewUI("Crosshair", cv);
        Anchor(crossRT, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(6f, 6f));
        AddImage(crossRT, Color.white, false);

        // Barra de sanidade
        var sanityBG = NewUI("SanityBarBG", cv);
        Anchor(sanityBG, new Vector2(0f, 0f), new Vector2(30f, 30f), new Vector2(320f, 26f));
        AddImage(sanityBG, new Color(0.08f, 0.08f, 0.08f, 0.85f), false);
        var sanityFillRT = NewUI("SanityBar", sanityBG);
        Stretch(sanityFillRT, 3f);
        Image sanityFill = AddFilledImage(sanityFillRT, Color.green);

        // Barra de bateria da lanterna
        var batteryBG = NewUI("BatteryBarBG", cv);
        Anchor(batteryBG, new Vector2(0f, 0f), new Vector2(30f, 66f), new Vector2(320f, 16f));
        AddImage(batteryBG, new Color(0.08f, 0.08f, 0.08f, 0.85f), false);
        var batteryFillRT = NewUI("BatteryBar", batteryBG);
        Stretch(batteryFillRT, 2f);
        Image batteryFill = AddFilledImage(batteryFillRT, Color.yellow);

        // Scripts de HUD ficam num objeto próprio (não em SanityBarBG, que é
        // desativado pelo GameController durante o menu).
        var hudRT = NewUI("HUD", cv);
        var sanityHUD = hudRT.gameObject.AddComponent<SanityHUD>();
        Set(sanityHUD, "sanityBar", sanityFill);
        Set(sanityHUD, "barGradient", MakeGradient(Color.red, Color.yellow, Color.green));
        Set(sanityHUD, "screenOverlay", overlay);
        Set(sanityHUD, "mainCamera", cam);

        var flashHUD = hudRT.gameObject.AddComponent<FlashlightHUD>();
        Set(flashHUD, "flashlight", flashlight);
        Set(flashHUD, "batteryBar", batteryFill);
        Set(flashHUD, "barGradient",
            MakeGradient(Color.red, new Color(1f, 0.6f, 0.1f), new Color(1f, 0.95f, 0.7f)));

        // Menu inicial / Game Over
        var menuRT = NewUI("MainMenuPanel", cv);
        Stretch(menuRT);
        AddImage(menuRT, new Color(0f, 0f, 0f, 0.92f), true);

        var title = NewText("Title", menuRT, "OBLIVION", 90, TextAnchor.MiddleCenter);
        Anchor(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 140f), new Vector2(1000f, 130f));
        var subtitle = NewText("Subtitle", menuRT, "", 36, TextAnchor.MiddleCenter);
        Anchor(subtitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(1000f, 60f));

        Text startLabel;
        Button startBtn = NewButton("StartButton", menuRT, "INICIAR", new Vector2(0f, -70f), new Vector2(300f, 70f), out startLabel);
        UnityEventTools.AddPersistentListener(startBtn.onClick, gc.StartGame);

        // Painel de vitória
        var winRT = NewUI("WinPanel", cv);
        Stretch(winRT);
        AddImage(winRT, new Color(0f, 0.08f, 0.03f, 0.92f), true);
        var winTitle = NewText("WinTitle", winRT, "VOCÊ ESCAPOU", 80, TextAnchor.MiddleCenter);
        winTitle.color = new Color(0.4f, 1f, 0.6f);
        Anchor(winTitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 100f), new Vector2(1200f, 120f));
        Text winLabel;
        Button winBtn = NewButton("RestartButton", winRT, "REINICIAR", new Vector2(0f, -50f), new Vector2(300f, 70f), out winLabel);
        UnityEventTools.AddPersistentListener(winBtn.onClick, gc.RestartGame);

        // Liga tudo no GameController
        Set(gc, "mainMenuPanel", menuRT.gameObject);
        Set(gc, "mainMenuTitleText", title);
        Set(gc, "mainMenuSubtitleText", subtitle);
        Set(gc, "mainMenuButtonText", startLabel);
        Set(gc, "winPanel", winRT.gameObject);
        winRT.gameObject.SetActive(false);
    }

    // ── Helpers de UI ────────────────────────────────────────────────────
    static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static void Stretch(RectTransform rt, float margin = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(margin, margin);
        rt.offsetMax = new Vector2(-margin, -margin);
    }

    static void Anchor(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static Image AddImage(RectTransform rt, Color color, bool raycast)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = raycast;
        return img;
    }

    // Image "Filled" precisa de um sprite para o fillAmount funcionar.
    static Image AddFilledImage(RectTransform rt, Color color)
    {
        var img = AddImage(rt, color, false);
        img.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        img.fillOrigin = (int)Image.OriginHorizontal.Left;
        img.fillAmount = 1f;
        return img;
    }

    static Text NewText(string name, Transform parent, string content, int size, TextAnchor anchor)
    {
        var rt = NewUI(name, parent);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.text = content;
        t.fontSize = size;
        t.alignment = anchor;
        t.color = Color.white;
        t.raycastTarget = false;
        return t;
    }

    static Button NewButton(string name, Transform parent, string label, Vector2 pos, Vector2 size, out Text labelText)
    {
        var rt = NewUI(name, parent);
        Anchor(rt, new Vector2(0.5f, 0.5f), pos, size);
        var img = AddImage(rt, new Color(0.85f, 0.85f, 0.85f, 1f), true);
        img.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        img.type = Image.Type.Sliced;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;

        labelText = NewText("Text", rt, label, 30, TextAnchor.MiddleCenter);
        labelText.color = Color.black;
        Stretch(labelText.rectTransform);
        return btn;
    }

    static Gradient MakeGradient(Color a, Color b, Color c)
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 0.5f), new GradientColorKey(c, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }

    // Os campos dos scripts são [SerializeField] privados: preenchemos por reflexão.
    static void Set(object target, string fieldName, object value)
    {
        var f = target.GetType().GetField(fieldName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f == null)
        {
            Debug.LogWarning("[OblivionSceneBuilder] Campo não encontrado: " + target.GetType().Name + "." + fieldName);
            return;
        }
        f.SetValue(target, value);
        var uo = target as UnityEngine.Object;
        if (uo != null) EditorUtility.SetDirty(uo);
    }

    static void AddToBuildSettings(string path)
    {
        var list = EditorBuildSettings.scenes.ToList();
        if (!list.Any(s => s.path == path))
        {
            list.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
#endif
