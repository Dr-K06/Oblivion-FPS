#if UNITY_EDITOR
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Oblivion - OblivionTuning (script de EDITOR)
/// Ajusta a jogabilidade da cena atual com um clique:
///   - mais luz (lanterna mais forte, luz ambiente bem fraquinha, brilho ao redor do jogador)
///   - sanidade durando ~6 minutos só com o dreno passivo
///   - bateria da lanterna durando mais e recarregando mais rápido
///   - gatilhos de caminho errado tirando menos sanidade
///   - efeito escuro de tela menos pesado
///
/// COMO USAR: coloque em Assets/Editor/ e rode  Oblivion > Ajustar Jogabilidade
/// Pode rodar quantas vezes quiser. Se gerar o labirinto de novo, rode outra vez.
/// </summary>
public static class OblivionTuning
{
    // ══ Mexa nestes números se quiser afinar ═════════════════════════════
    // Sanidade
    const float SanityMinutes       = 6f;    // tempo até zerar SÓ com o dreno passivo
    const float TrapDrain           = 5f;    // perda por gatilho de caminho errado (era 8)
    const float OverlayMaxAlpha     = 0.25f; // escurecimento da tela com sanidade baixa (era 0.4)

    // Lanterna
    const float FlashIntensity      = 3.5f;  // era 2
    const float FlashRange          = 25f;   // era 15
    const float FlashSpotAngle      = 65f;   // era 45
    const float BatteryDrainPerSec  = 0.6f;  // era 4  (dura ~166 s ligada)
    const float BatteryRechargeSec  = 3f;    // era 1.5 (recarrega desligada)

    // Luz geral
    static readonly Color AmbientColor = new Color(0.10f, 0.10f, 0.12f); // era preto
    const float GlowIntensity       = 0.6f;  // brilho suave ao redor do jogador
    const float GlowRange           = 8f;

    [MenuItem("Oblivion/Ajustar Jogabilidade")]
    public static void Apply()
    {
        var report = new StringBuilder();

        // ── Sanidade ─────────────────────────────────────────────────────
        var sanity = Object.FindObjectOfType<SanitySystem>();
        if (sanity != null)
        {
            float drain = 100f / (SanityMinutes * 60f);
            Set(sanity, "passiveDrainPerSecond", drain);
            report.AppendLine("• Sanidade: " + drain.ToString("0.000") + "/s (~" + SanityMinutes + " min sem gatilhos)");
        }
        else report.AppendLine("• SanitySystem não encontrado (pulado)");

        int traps = 0;
        foreach (var t in Object.FindObjectsOfType<WrongPathTrigger>())
        {
            Set(t, "sanityDrainAmount", TrapDrain);
            traps++;
        }
        report.AppendLine("• Gatilhos de caminho errado: " + traps + " (cada um tira " + TrapDrain + ")");

        var hud = Object.FindObjectOfType<SanityHUD>();
        if (hud != null) Set(hud, "maxOverlayAlpha", OverlayMaxAlpha);

        // ── Lanterna ─────────────────────────────────────────────────────
        var flash = Object.FindObjectOfType<Flashlight>();
        if (flash != null)
        {
            Set(flash, "drainPerSecond", BatteryDrainPerSec);
            Set(flash, "rechargePerSecondWhenOff", BatteryRechargeSec);

            var l = flash.GetComponent<Light>();
            if (l != null)
            {
                l.intensity = FlashIntensity;
                l.range = FlashRange;
                l.spotAngle = FlashSpotAngle;
                EditorUtility.SetDirty(l);
            }
            report.AppendLine("• Lanterna mais forte; bateria dura ~" + Mathf.RoundToInt(100f / BatteryDrainPerSec) + " s");
        }
        else report.AppendLine("• Flashlight não encontrada (pulado)");

        // ── Luz geral ────────────────────────────────────────────────────
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = AmbientColor;

        var player = GameObject.Find("Player");
        if (player != null)
        {
            var glowT = player.transform.Find("PlayerGlow");
            GameObject glow = glowT != null ? glowT.gameObject : null;
            if (glow == null)
            {
                glow = new GameObject("PlayerGlow");
                glow.transform.SetParent(player.transform, false);
                glow.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            }
            var gl = glow.GetComponent<Light>();
            if (gl == null) gl = glow.AddComponent<Light>();
            gl.type = LightType.Point;
            gl.color = new Color(1f, 0.9f, 0.75f);
            gl.intensity = GlowIntensity;
            gl.range = GlowRange;
            gl.shadows = LightShadows.None;
            EditorUtility.SetDirty(gl);
            report.AppendLine("• Luz ambiente fraca + brilho ao redor do jogador");
        }
        else report.AppendLine("• Player não encontrado (brilho pulado)");

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        EditorUtility.DisplayDialog("Oblivion - Jogabilidade ajustada", report.ToString(), "OK");
    }

    // Campos [SerializeField] privados: preenchidos por reflexão.
    static void Set(object target, string fieldName, object value)
    {
        var f = target.GetType().GetField(fieldName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f == null)
        {
            Debug.LogWarning("[OblivionTuning] Campo não encontrado: " + target.GetType().Name + "." + fieldName);
            return;
        }
        f.SetValue(target, value);
        var uo = target as UnityEngine.Object;
        if (uo != null) EditorUtility.SetDirty(uo);
    }
}
#endif
