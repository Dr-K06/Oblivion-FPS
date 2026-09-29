using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Oblivion - MapSystem
/// Dois mapas desenhados por código (sem câmera extra):
///
///  1) MAPA DE MEMÓRIA (minimapa no canto, sempre visível): mostra só o que o
///     jogador já visitou. As áreas vão se apagando com o tempo, e quanto menor
///     a sanidade, mais rápido elas somem. Voltar a um lugar "relembra" a área.
///
///  2) MAPA COMPLETO (tecla Tab): abre o labirinto inteiro, com a saída marcada.
///     Pode custar sanidade por segundo enquanto estiver aberto
///     (campo "Full Map Drain Per Second"; 0 = grátis, bom para testes).
///
/// Os dados do labirinto (mazeRows) e as referências de UI são preenchidos pelos
/// menus do editor:  Oblivion > Adicionar Mapa  e  Oblivion > Gerar Labirinto.
/// </summary>
public class MapSystem : MonoBehaviour
{
    [Header("Dados do labirinto (preenchidos pelo editor)")]
    [SerializeField] private string[] mazeRows;   // '#' parede, '.' vazio, 'S' início, 'E' saída
    [SerializeField] private float cellSize = 4f;
    [SerializeField] private int centerCol = 6;

    [Header("UI (preenchida pelo editor)")]
    [SerializeField] private GameObject miniMapRoot;
    [SerializeField] private RawImage miniMapImage;
    [SerializeField] private GameObject fullMapRoot;
    [SerializeField] private RawImage fullMapImage;
    [SerializeField] private Transform player;

    [Header("Mapa Completo (Tab)")]
    [SerializeField] private KeyCode fullMapKey = KeyCode.Tab;
    [Tooltip("Sanidade perdida por segundo enquanto o mapa completo está aberto. 0 = grátis.")]
    [SerializeField] private float fullMapDrainPerSecond = 0f;

    [Header("Mapa de Memória")]
    [Tooltip("Quantas células ao redor do jogador são reveladas.")]
    [SerializeField] private int revealRadius = 1;
    [Tooltip("Velocidade base com que o mapa é esquecido (por segundo). 0.004 = ~250 s.")]
    [SerializeField] private float baseDecayPerSecond = 0.004f;
    [Tooltip("Com sanidade em 0, o esquecimento fica (1 + este valor) vezes mais rápido.")]
    [SerializeField] private float lowSanityDecayMultiplier = 15f;

    private const int PixelsPerCell = 8;
    private const float RedrawInterval = 0.1f;

    private int rows, cols;
    private bool[,] wall;
    private bool[,] isExit;
    private bool[,] visited;
    private float[,] memory;

    private Texture2D miniTex, fullTex;
    private Color[] buffer;
    private bool fullOpen;
    private float redrawTimer;

    // ── Chamado pelo editor ──────────────────────────────────────────────
    public void SetMazeData(string[] rowsData, float cell, int center)
    {
        mazeRows = rowsData;
        cellSize = cell;
        centerCol = center;
    }

    public void Configure(GameObject miniRoot, RawImage miniImage, GameObject fullRoot, RawImage fullImage, Transform playerTransform)
    {
        miniMapRoot = miniRoot;
        miniMapImage = miniImage;
        fullMapRoot = fullRoot;
        fullMapImage = fullImage;
        player = playerTransform;
    }

    // ── Ciclo de vida ────────────────────────────────────────────────────
    void Start()
    {
        if (!ParseMaze())
        {
            Debug.LogWarning("[MapSystem] Sem dados do labirinto. Rode: Oblivion > Gerar Labirinto.");
            if (miniMapRoot != null) miniMapRoot.SetActive(false);
            if (fullMapRoot != null) fullMapRoot.SetActive(false);
            enabled = false;
            return;
        }

        if (player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }

        int w = cols * PixelsPerCell;
        int h = rows * PixelsPerCell;
        buffer = new Color[w * h];
        miniTex = NewTexture(w, h);
        fullTex = NewTexture(w, h);

        if (miniMapImage != null) miniMapImage.texture = miniTex;
        if (fullMapImage != null) fullMapImage.texture = fullTex;

        fullOpen = false;
    }

    void OnDestroy()
    {
        if (miniTex != null) Destroy(miniTex);
        if (fullTex != null) Destroy(fullTex);
    }

    void Update()
    {
        if (player == null) return;

        bool playing = GameController.Instance == null
            || GameController.Instance.State == GameController.GameState.Playing;

        // Fora do jogo (menu, game over, vitória): esconde os mapas
        if (!playing)
        {
            fullOpen = false;
            if (miniMapRoot != null && miniMapRoot.activeSelf) miniMapRoot.SetActive(false);
            if (fullMapRoot != null && fullMapRoot.activeSelf) fullMapRoot.SetActive(false);
            return;
        }

        if (Input.GetKeyDown(fullMapKey))
        {
            fullOpen = !fullOpen;
            redrawTimer = 0f; // redesenha já
        }

        // Só um dos dois fica visível
        if (miniMapRoot != null && miniMapRoot.activeSelf == fullOpen) miniMapRoot.SetActive(!fullOpen);
        if (fullMapRoot != null && fullMapRoot.activeSelf != fullOpen) fullMapRoot.SetActive(fullOpen);

        UpdateMemory();

        if (fullOpen && fullMapDrainPerSecond > 0f && SanitySystem.Instance != null)
            SanitySystem.Instance.DrainSanity(fullMapDrainPerSecond * Time.deltaTime);

        redrawTimer -= Time.deltaTime;
        if (redrawTimer <= 0f)
        {
            redrawTimer = RedrawInterval;
            if (fullOpen) Draw(fullTex, true);
            else          Draw(miniTex, false);
        }
    }

    // ── Memória ──────────────────────────────────────────────────────────
    void UpdateMemory()
    {
        float sanity = SanitySystem.Instance != null ? SanitySystem.Instance.GetSanityNormalized() : 1f;
        float decay = baseDecayPerSecond * (1f + (1f - sanity) * lowSanityDecayMultiplier) * Time.deltaTime;

        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                memory[r, c] = Mathf.Max(0f, memory[r, c] - decay);

        int pr, pc;
        GetPlayerCell(out pr, out pc);

        for (int dr = -revealRadius; dr <= revealRadius; dr++)
        {
            for (int dc = -revealRadius; dc <= revealRadius; dc++)
            {
                int r = pr + dr, c = pc + dc;
                if (r < 0 || r >= rows || c < 0 || c >= cols) continue;
                memory[r, c] = 1f;
                if (!wall[r, c]) visited[r, c] = true;
            }
        }
    }

    void GetPlayerCell(out int r, out int c)
    {
        Vector3 p = player.position;
        c = Mathf.Clamp(Mathf.RoundToInt(p.x / cellSize) + centerCol, 0, cols - 1);
        r = Mathf.Clamp((rows - 1) - Mathf.RoundToInt(p.z / cellSize), 0, rows - 1);
    }

    // ── Desenho ──────────────────────────────────────────────────────────
    void Draw(Texture2D tex, bool full)
    {
        int w = tex.width;
        int h = tex.height;

        for (int r = 0; r < rows; r++)
        {
            int ty0 = (rows - 1 - r) * PixelsPerCell; // linha 0 do mapa = topo da textura
            for (int c = 0; c < cols; c++)
            {
                Color col = CellColor(r, c, full);
                int tx0 = c * PixelsPerCell;
                for (int py = 0; py < PixelsPerCell; py++)
                    for (int px = 0; px < PixelsPerCell; px++)
                        buffer[(ty0 + py) * w + tx0 + px] = col;
            }
        }

        DrawPlayerMarker(w, h);

        tex.SetPixels(buffer);
        tex.Apply(false);
    }

    Color CellColor(int r, int c, bool full)
    {
        float m = full ? 1f : memory[r, c];
        if (m <= 0.01f) return Color.clear;

        Color col;
        if (isExit[r, c])       col = new Color(0.25f, 1f, 0.55f);
        else if (wall[r, c])    col = full ? new Color(0.50f, 0.50f, 0.50f) : new Color(0.72f, 0.72f, 0.72f);
        else if (full)          col = visited[r, c] ? new Color(0.24f, 0.32f, 0.44f) : new Color(0.10f, 0.10f, 0.10f);
        else                    col = new Color(0.14f, 0.16f, 0.20f);

        col.a = (full ? 0.96f : 0.92f) * m;
        return col;
    }

    void DrawPlayerMarker(int w, int h)
    {
        Vector3 p = player.position;
        float tx = (p.x / cellSize + centerCol + 0.5f) * PixelsPerCell;
        float ty = (p.z / cellSize + 0.5f) * PixelsPerCell;
        Color body = new Color(1f, 0.25f, 0.2f, 1f);

        Dot(w, h, tx, ty, 2, body);

        // Pontinhos na direção em que o jogador está olhando
        Vector3 f = player.forward;
        for (int i = 1; i <= 3; i++)
            Dot(w, h, tx + f.x * i * 2.5f, ty + f.z * i * 2.5f, 1, body);
    }

    void Dot(int w, int h, float cx, float cy, int radius, Color col)
    {
        int ix = Mathf.RoundToInt(cx);
        int iy = Mathf.RoundToInt(cy);
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                int x = ix + dx, y = iy + dy;
                if (x < 0 || x >= w || y < 0 || y >= h) continue;
                buffer[y * w + x] = col;
            }
        }
    }

    // ── Dados ────────────────────────────────────────────────────────────
    bool ParseMaze()
    {
        if (mazeRows == null || mazeRows.Length == 0) return false;

        rows = mazeRows.Length;
        cols = mazeRows[0].Length;
        wall    = new bool[rows, cols];
        isExit  = new bool[rows, cols];
        visited = new bool[rows, cols];
        memory  = new float[rows, cols];

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols && c < mazeRows[r].Length; c++)
            {
                char ch = mazeRows[r][c];
                wall[r, c]   = ch == '#';
                isExit[r, c] = ch == 'E';
            }
        }
        return true;
    }

    Texture2D NewTexture(int w, int h)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        t.filterMode = FilterMode.Point;
        t.wrapMode = TextureWrapMode.Clamp;
        return t;
    }
}
