#if UNITY_EDITOR
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Oblivion - OblivionMazeBuilder v2 (script de EDITOR)
/// Gera o labirinto (13 x 11 células de 4 m), move o Player para o início,
/// posiciona a saída e cria os WrongPathTrigger nos becos sem saída.
/// As paredes, o chão e o teto recebem materiais "sujos" gerados por código
/// (manchas, limo, escorridos e normal map), salvos em Assets/Materials.
///
/// COMO USAR: coloque em Assets/Editor/ e rode  Oblivion > Gerar Labirinto
/// (a cena precisa já ter sido montada por "Montar Cena Completa").
/// Pode rodar de novo: ele apaga o labirinto anterior e gera outro.
/// Menu extra:  Oblivion > Regenerar Texturas Sujas  (refaz só as texturas).
///
/// MAPA (visto de cima; o início S fica embaixo e o jogador olha para "cima"):
///   #############
///   #......X#**E#      # parede     * caminho certo
///   #.#######*###      S início     E saída
///   #......X#*#X#      X gatilho de caminho errado (perde sanidade)
///   #.#.#####*#.#
///   #.#.#*****.X#
///   #.#.#*#####.#
///   #.#X#*#X..#.#
///   #.###*###.#.#
///   #..X.S#X....#
///   #############
/// </summary>
public static class OblivionMazeBuilder
{
    // ── Ajustes rápidos ──────────────────────────────────────────────────
    const float Cell = 4f;       // largura do corredor (metros). Era 3.
    const float WallH = 3.5f;    // altura das paredes
    const float TrapDrain = 8f;  // sanidade perdida por gatilho
    const int WallVariants = 3;  // quantas texturas diferentes de parede
    const int TexSize = 512;     // resolução das texturas geradas

    const int Rows = 11;
    const int Cols = 13;
    const int CenterCol = 6;
    const string MatFolder = "Assets/Materials";

    // Grade de SALAS 5 x 6 (linha, coluna): cada linha liga duas salas por uma passagem.
    // Sala (i,j) vira a célula (2i+1, 2j+1); a passagem fica na célula do meio.
    static readonly int[,] Edges =
    {
        // caminho certo: início (4,2) -> saída (0,5)
        {4,2,3,2},{3,2,2,2},{2,2,2,3},{2,3,2,4},{2,4,1,4},{1,4,0,4},{0,4,0,5},
        // ramo da esquerda (longo, sem saída)
        {4,2,4,1},{4,1,4,0},{4,0,3,0},{3,0,2,0},{2,0,1,0},{1,0,0,0},{0,0,0,1},{0,1,0,2},{0,2,0,3},
        // ramo do meio-esquerda
        {1,0,1,1},{1,1,1,2},{1,2,1,3},{1,1,2,1},{2,1,3,1},
        // ramo da direita
        {2,4,2,5},{2,5,1,5},{2,5,3,5},{3,5,4,5},{4,5,4,4},{4,4,4,3},{4,4,3,4},{3,4,3,3}
    };

    // Salas (linha, coluna) que recebem WrongPathTrigger (becos e desvios errados)
    static readonly int[,] Traps =
    {
        {4,1},{0,3},{3,1},{1,3},{1,5},{4,3},{3,3},{2,5}
    };

    // ═════════════════════════════════════════════════════════════════════
    [MenuItem("Oblivion/Gerar Labirinto")]
    public static void Build()
    {
        var player = GameObject.Find("Player");
        if (player == null)
        {
            EditorUtility.DisplayDialog("Oblivion",
                "Não achei o Player. Rode primeiro: Oblivion > Montar Cena Completa.", "OK");
            return;
        }

        // Limpa o corredor de teste e labirintos anteriores
        DestroyIfExists("Greybox");
        DestroyIfExists("Maze");

        Material[] wallMats = GetWallMaterials(false);
        Material floorMat = GetFloorMaterial(false);

        var root = new GameObject("Maze").transform;

        // Mapa de células abertas
        bool[,] open = new bool[Rows, Cols];
        for (int i = 0; i < 5; i++)
            for (int j = 0; j < 6; j++)
                open[2 * i + 1, 2 * j + 1] = true;
        for (int e = 0; e < Edges.GetLength(0); e++)
            open[Edges[e, 0] + Edges[e, 2] + 1, Edges[e, 1] + Edges[e, 3] + 1] = true;

        // Paredes: um cubo por célula (a textura fica com escala uniforme).
        // Células de parede que não encostam em nenhum espaço aberto são puladas.
        var rng = new System.Random(7);
        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < Cols; c++)
            {
                if (open[r, c] || !TouchesOpen(open, r, c)) continue;

                var wall = MakeCube("Wall_r" + r + "_c" + c, root,
                    new Vector3((c - CenterCol) * Cell, WallH / 2f, (Rows - 1 - r) * Cell),
                    new Vector3(Cell, WallH, Cell));
                wall.GetComponent<Renderer>().sharedMaterial = wallMats[rng.Next(wallMats.Length)];
                wall.transform.rotation = Quaternion.Euler(0f, 90f * rng.Next(4), 0f);
            }
        }

        // Chão e teto (planos com uma repetição da textura por célula)
        float midZ = (Rows - 1) / 2f * Cell;
        Vector3 planeScale = new Vector3(Cols * Cell / 10f, 1f, Rows * Cell / 10f);

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.SetParent(root);
        floor.transform.position = new Vector3(0f, 0f, midZ);
        floor.transform.localScale = planeScale;
        floor.GetComponent<Renderer>().sharedMaterial = floorMat;

        var ceiling = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ceiling.name = "Ceiling";
        ceiling.transform.SetParent(root);
        ceiling.transform.position = new Vector3(0f, WallH, midZ);
        ceiling.transform.rotation = Quaternion.Euler(180f, 0f, 0f); // vira para baixo
        ceiling.transform.localScale = planeScale;
        ceiling.GetComponent<Renderer>().sharedMaterial = floorMat;

        // Gatilhos de caminho errado
        for (int t = 0; t < Traps.GetLength(0); t++)
        {
            int ri = Traps[t, 0], ci = Traps[t, 1];
            var go = new GameObject("WrongPath_" + ri + "_" + ci);
            go.transform.SetParent(root);
            go.transform.position = CellPos(2 * ri + 1, 2 * ci + 1) + Vector3.up * 1.2f;
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(Cell * 0.8f, 2.4f, Cell * 0.8f);
            box.isTrigger = true;
            var trig = go.AddComponent<WrongPathTrigger>();
            Set(trig, "sanityDrainAmount", TrapDrain);
        }

        // Player no início (sala 4,2 -> célula 9,5), olhando para +Z (passagem aberta)
        player.transform.position = CellPos(9, 5) + new Vector3(0f, 0.1f, 0f);
        player.transform.rotation = Quaternion.identity;

        // Saída na sala (0,5) -> célula (1,11)
        PlaceExit(CellPos(1, 11) + new Vector3(0f, 1.4f, 0f));

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Selection.activeGameObject = root.gameObject;
        EditorUtility.DisplayDialog("Oblivion",
            "Labirinto gerado e cena salva!\n\n" +
            "Aperte Play e clique em INICIAR.\n" +
            "As caixas laranja na aba Scene são os gatilhos de caminho errado.", "OK");
    }

    [MenuItem("Oblivion/Regenerar Texturas Sujas")]
    public static void RegenerateTextures()
    {
        GetWallMaterials(true);
        GetFloorMaterial(true);
        Debug.Log("[Oblivion] Texturas sujas regeneradas em " + MatFolder);
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Labirinto - helpers
    // ═════════════════════════════════════════════════════════════════════
    static Vector3 CellPos(int r, int c)
    {
        return new Vector3((c - CenterCol) * Cell, 0f, (Rows - 1 - r) * Cell);
    }

    static bool TouchesOpen(bool[,] open, int r, int c)
    {
        for (int dr = -1; dr <= 1; dr++)
        {
            for (int dc = -1; dc <= 1; dc++)
            {
                int rr = r + dr, cc = c + dc;
                if (rr >= 0 && rr < Rows && cc >= 0 && cc < Cols && open[rr, cc]) return true;
            }
        }
        return false;
    }

    static GameObject MakeCube(string name, Transform parent, Vector3 pos, Vector3 scale)
    {
        var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
        c.name = name;
        c.transform.SetParent(parent);
        c.transform.position = pos;
        c.transform.localScale = scale;
        return c;
    }

    static void PlaceExit(Vector3 pos)
    {
        var exit = GameObject.Find("MazeExit");
        if (exit == null)
        {
            exit = GameObject.CreatePrimitive(PrimitiveType.Cube);
            exit.name = "MazeExit";
            var me = exit.AddComponent<MazeExit>();

            var lg = new GameObject("ExitLight");
            lg.transform.SetParent(exit.transform, false);
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.intensity = 2f;
            l.color = new Color(0.25f, 1f, 0.55f);

            Set(me, "exitLight", l);
            Set(me, "exitRenderers", new Renderer[] { exit.GetComponent<Renderer>() });
        }

        exit.transform.position = pos;
        exit.transform.rotation = Quaternion.identity;
        exit.transform.localScale = new Vector3(Cell * 0.6f, 2.8f, Cell * 0.6f);

        var light = exit.GetComponentInChildren<Light>();
        if (light != null)
        {
            light.transform.localPosition = Vector3.zero;
            light.range = 9f;
        }
    }

    static void DestroyIfExists(string name)
    {
        var go = GameObject.Find(name);
        if (go != null) UnityEngine.Object.DestroyImmediate(go);
    }

    // Campos [SerializeField] privados: preenchidos por reflexão.
    static void Set(object target, string fieldName, object value)
    {
        var f = target.GetType().GetField(fieldName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f == null)
        {
            Debug.LogWarning("[OblivionMazeBuilder] Campo não encontrado: " + target.GetType().Name + "." + fieldName);
            return;
        }
        f.SetValue(target, value);
        var uo = target as UnityEngine.Object;
        if (uo != null) EditorUtility.SetDirty(uo);
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Materiais e texturas sujas (100% geradas por código)
    // ═════════════════════════════════════════════════════════════════════
    static Material[] GetWallMaterials(bool force)
    {
        var mats = new Material[WallVariants];
        for (int i = 0; i < WallVariants; i++)
            mats[i] = GetOrCreateMaterial("Wall_Dirty_" + i, i + 1, false, force);
        return mats;
    }

    static Material GetFloorMaterial(bool force)
    {
        return GetOrCreateMaterial("Floor_Dirty", 100, true, force);
    }

    static Material GetOrCreateMaterial(string name, int seed, bool floor, bool force)
    {
        if (!AssetDatabase.IsValidFolder(MatFolder))
            AssetDatabase.CreateFolder("Assets", "Materials");

        string matPath    = MatFolder + "/" + name + ".mat";
        string albedoPath = MatFolder + "/" + name + "_albedo.png";
        string normalPath = MatFolder + "/" + name + "_normal.png";

        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat != null && !force) return mat;

        try
        {
            EditorUtility.DisplayProgressBar("Oblivion", "Gerando textura suja: " + name, 0.5f);

            Texture2D albedo, normal;
            GenerateTextures(seed, floor, out albedo, out normal);
            System.IO.File.WriteAllBytes(albedoPath, albedo.EncodeToPNG());
            System.IO.File.WriteAllBytes(normalPath, normal.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(albedo);
            UnityEngine.Object.DestroyImmediate(normal);

            AssetDatabase.ImportAsset(albedoPath);
            AssetDatabase.ImportAsset(normalPath);

            var albedoImp = AssetImporter.GetAtPath(albedoPath) as TextureImporter;
            if (albedoImp != null)
            {
                albedoImp.anisoLevel = 4;
                albedoImp.SaveAndReimport();
            }
            var normalImp = AssetImporter.GetAtPath(normalPath) as TextureImporter;
            if (normalImp != null)
            {
                normalImp.textureType = TextureImporterType.NormalMap;
                normalImp.anisoLevel = 4;
                normalImp.SaveAndReimport();
            }

            var albedoTex = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
            var normalTex = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);

            if (mat == null)
            {
                mat = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.mainTexture = albedoTex;
            mat.SetTexture("_BumpMap", normalTex);
            mat.EnableKeyword("_NORMALMAP");
            mat.SetFloat("_BumpScale", 1f);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Glossiness", floor ? 0.22f : 0.08f);
            // chão/teto: uma repetição da textura por célula do labirinto
            mat.mainTextureScale = floor ? new Vector2(Cols, Rows) : Vector2.one;

            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
        return mat;
    }

    static void GenerateTextures(int seed, bool floor, out Texture2D albedo, out Texture2D normal)
    {
        int n = TexSize;
        float off = seed * 17.31f;

        Color baseCol  = floor ? new Color(0.30f, 0.29f, 0.27f) : new Color(0.42f, 0.40f, 0.35f);
        Color stainCol = new Color(0.20f, 0.14f, 0.08f);  // marrom de infiltração
        Color mossCol  = new Color(0.15f, 0.23f, 0.11f);  // limo esverdeado

        var pixels = new Color[n * n];
        var height = new float[n * n];

        for (int y = 0; y < n; y++)
        {
            float v = (float)y / n;
            for (int x = 0; x < n; x++)
            {
                float u = (float)x / n;

                float h      = Fbm(u, v, 4, off, 4f);          // rugosidade do reboco
                float stain  = Fbm(u, v, 3, off + 50f, 2f);    // manchas grandes
                float blot   = TileNoise(u, v, 10f, 10f, off + 90f);  // pontinhos escuros
                float streak = floor ? 0f : TileNoise(u, v, 26f, 2f, off + 130f); // escorridos verticais

                Color c = baseCol * Mathf.Lerp(0.55f, 1.25f, h);

                // manchas marrons
                float stainMask = Mathf.SmoothStep(0.48f, 0.72f, stain);
                c = Color.Lerp(c, stainCol * Mathf.Lerp(0.7f, 1.2f, h), stainMask * 0.75f);

                if (!floor)
                {
                    // escorridos verticais
                    c *= 1f - 0.45f * Mathf.SmoothStep(0.60f, 0.85f, streak);

                    // sujeira e limo perto do chão (v = 0 é a base da parede)
                    float low = 1f - Mathf.SmoothStep(0f, 0.4f, v);
                    c = Color.Lerp(c, mossCol * Mathf.Lerp(0.6f, 1.2f, h),
                        low * 0.55f * Mathf.SmoothStep(0.35f, 0.70f, stain));
                    c *= 1f - 0.40f * low;
                }

                // pontos escuros
                c *= 1f - 0.5f * Mathf.SmoothStep(0.68f, 0.85f, blot);

                c.a = 1f;
                pixels[y * n + x] = c;
                height[y * n + x] = h * 0.8f + streak * 0.2f;
            }
        }

        // Normal map a partir do mapa de altura (com wrap, para repetir sem emenda)
        float strength = floor ? 18f : 25f;
        var nm = new Color[n * n];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float hl = height[y * n + (x - 1 + n) % n];
                float hr = height[y * n + (x + 1) % n];
                float hd = height[((y - 1 + n) % n) * n + x];
                float hu = height[((y + 1) % n) * n + x];
                Vector3 nv = new Vector3((hl - hr) * strength, (hd - hu) * strength, 1f).normalized;
                nm[y * n + x] = new Color(nv.x * 0.5f + 0.5f, nv.y * 0.5f + 0.5f, nv.z * 0.5f + 0.5f, 1f);
            }
        }

        albedo = new Texture2D(n, n, TextureFormat.RGBA32, false);
        albedo.SetPixels(pixels);
        albedo.Apply();

        normal = new Texture2D(n, n, TextureFormat.RGBA32, false, true);
        normal.SetPixels(nm);
        normal.Apply();
    }

    // Ruído Perlin que se repete sem emenda (mistura 4 cópias deslocadas).
    static float TileNoise(float u, float v, float fx, float fy, float off)
    {
        float a = Mathf.PerlinNoise(u * fx + off,          v * fy + off);
        float b = Mathf.PerlinNoise((u - 1f) * fx + off,   v * fy + off);
        float c = Mathf.PerlinNoise(u * fx + off,          (v - 1f) * fy + off);
        float d = Mathf.PerlinNoise((u - 1f) * fx + off,   (v - 1f) * fy + off);
        return a * (1f - u) * (1f - v) + b * u * (1f - v) + c * (1f - u) * v + d * u * v;
    }

    static float Fbm(float u, float v, int octaves, float off, float baseFreq)
    {
        float sum = 0f, amp = 0.5f, norm = 0f, f = baseFreq;
        for (int i = 0; i < octaves; i++)
        {
            sum += TileNoise(u, v, f, f, off + i * 13.7f) * amp;
            norm += amp;
            amp *= 0.5f;
            f *= 2f;
        }
        float r = sum / norm;
        return Mathf.Clamp01((r - 0.5f) * 1.9f + 0.5f); // devolve contraste
    }
}
#endif
