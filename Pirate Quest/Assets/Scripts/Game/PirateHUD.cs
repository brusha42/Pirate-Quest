using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
public sealed class PirateHUD : MonoBehaviour
{
    public const string SaberShortcut = "LMB";
    public const string GrappleShortcut = "RMB";
    public const string UpgradeAcknowledgement = "Got it";
    public const string DropShortcut = "S + SPACE  Drop";
    public static PirateHUD Instance { get; private set; }
    public bool IsUpgradeOpen { get; private set; }
    public bool CanAcceptGameplayInput => !IsUpgradeOpen && !ConsumedInputThisFrame;
    public static bool ConsumedInputThisFrame => consumedFrame == Time.frameCount;
    public static bool PointerBlocksWorld => Instance != null && Instance.initialized &&
        Instance.presentationVisible && Mouse.current != null && Instance.ContainsHudPoint(PointerInHud());
    public PirateUpgrade? DisplayedUpgrade => IsUpgradeOpen ? currentUpgrade : (PirateUpgrade?)null;
    public int PendingUpgradeCount => pending.Count + (IsUpgradeOpen ? 1 : 0);

    private static int consumedFrame = -1;
    private readonly Queue<PirateUpgrade> pending = new Queue<PirateUpgrade>();
    private PlayerMovement movement;
    private PlayerAbilities abilities;
    private PlayerLife life;
    private PlayerGrapple grapple;
    private Action<bool> onModalBlocked;
    private int chapter;
    private bool initialized;
    private bool presentationVisible = true;
    private PirateUpgrade currentUpgrade;
    private float openedAt;
    private int openedFrame;
    private bool currentIsReview;
    private string message;
    private string pressureStatus;
    private float pressureUrgency;
    private GUIStyle heading, small, tiny, stateText, centered, key, body, dialogBody, dialogAction,
        dialogTitle, button, closeButton;
    private bool treasureVisible;
    private int treasureBanked, treasureCarried, treasureRecovered, treasureTotal;
    private double runSeconds;

    private static readonly Color Ink = new Color(.025f, .047f, .065f, .91f);
    private static readonly Color Border = new Color(.45f, .36f, .22f, .88f);
    private static readonly Color Gold = new Color(.95f, .77f, .43f);
    private static readonly Color Text = new Color(.90f, .91f, .88f);
    private static readonly Color Muted = new Color(.57f, .65f, .67f);
    private static readonly Color Teal = new Color(.38f, .85f, .79f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetDomain() { Instance = null; consumedFrame = -1; }

    public void Initialize(PlayerMovement player, PlayerAbilities equipment, PlayerLife playerLife,
        int chapterIndex, Action<bool> modalBlocked)
    {
        Instance = this;
        movement = player;
        abilities = equipment;
        life = playerLife;
        grapple = player != null ? player.GetComponent<PlayerGrapple>() : null;
        chapter = Mathf.Clamp(chapterIndex, 0, CampaignChapter.Titles.Length - 1);
        onModalBlocked = modalBlocked;
        initialized = movement != null && abilities != null && life != null;
    }

    public void SetMessage(string text) => message = text;
    public static void ConsumeFrameInput() => consumedFrame = Time.frameCount;
    public void SetPresentationVisible(bool visible) => presentationVisible = visible;
    public void SetPressureStatus(string text, float urgency = 0f)
    { pressureStatus = text; pressureUrgency = Mathf.Clamp01(urgency); }
    public void SetTreasureStatus(int banked, int carried, int recovered, int total)
    {
        treasureVisible = true;
        treasureBanked = Mathf.Max(0, banked);
        treasureCarried = Mathf.Max(0, carried);
        treasureRecovered = Mathf.Max(0, recovered);
        treasureTotal = Mathf.Max(0, total);
    }
    public void SetRunTime(double seconds) => runSeconds = Math.Max(0d, seconds);

    public void ShowUpgrade(PirateUpgrade upgrade)
    {
        if (!initialized || (IsUpgradeOpen && currentUpgrade == upgrade) || pending.Contains(upgrade)) return;
        if (IsUpgradeOpen) { pending.Enqueue(upgrade); return; }
        currentUpgrade = upgrade;
        IsUpgradeOpen = true;
        openedAt = Time.unscaledTime;
        openedFrame = consumedFrame = Time.frameCount;
        currentIsReview = false;
        onModalBlocked?.Invoke(true);
    }

    public bool OpenEquipmentDescription(PirateUpgrade upgrade)
    {
        if (!initialized || !presentationVisible || IsUpgradeOpen || life == null ||
            life.IsRespawning || life.IsExitProtected || !abilities.Has(upgrade) ||
            (PirateFrontEnd.Instance != null && PirateFrontEnd.Instance.IsBlocking)) return false;
        ShowUpgrade(upgrade);
        currentIsReview = IsUpgradeOpen;
        return IsUpgradeOpen && currentUpgrade == upgrade;
    }

    public void DismissUpgrade()
    {
        if (!IsUpgradeOpen) return;
        consumedFrame = Time.frameCount;
        if (pending.Count > 0)
        {
            currentUpgrade = pending.Dequeue();
            openedAt = Time.unscaledTime;
            openedFrame = Time.frameCount;
            currentIsReview = false;
            return;
        }
        IsUpgradeOpen = false;
        onModalBlocked?.Invoke(false);
    }

    public void ClearUpgrade()
    {
        pending.Clear();
        if (!IsUpgradeOpen) return;
        IsUpgradeOpen = false;
        consumedFrame = Time.frameCount;
        onModalBlocked?.Invoke(false);
    }

    public static bool ConsumeUpgradeInput()
    {
        if (ConsumedInputThisFrame) return true;
        PirateHUD hud = Instance;
        if (hud == null || !hud.IsUpgradeOpen) return false;
        if (!hud.presentationVisible) return true;
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && Time.unscaledTime - hud.openedAt >= .15f &&
            (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame ||
             keyboard.escapeKey.wasPressedThisFrame)) hud.DismissUpgrade();
        return true;
    }

    private void Update()
    {
        if (initialized && presentationVisible && !IsUpgradeOpen && !ConsumedInputThisFrame &&
            (PirateFrontEnd.Instance == null || !PirateFrontEnd.Instance.IsBlocking) &&
            Mouse.current != null && (Mouse.current.leftButton.wasPressedThisFrame ||
                Mouse.current.rightButton.wasPressedThisFrame))
        {
            Vector2 point = PointerInHud();
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                for (int slot = 0; slot < 4; slot++)
                {
                    PirateUpgrade? upgrade = EquipmentUpgrade(slot);
                    if (upgrade.HasValue && EquipmentRect(slot).Contains(point))
                    {
                        OpenEquipmentDescription(upgrade.Value);
                        break;
                    }
                }
            }
            if (ContainsHudPoint(point)) ConsumeFrameInput();
        }
        if (initialized && PirateFrontEnd.Instance == null) ConsumeUpgradeInput();
    }

    private static Rect EquipmentRect(int slot) => new Rect(1176, 186 + slot * 69, 80, 62);
    private Rect TimerRect() => treasureVisible ? new Rect(844, 63, 160, 45) : new Rect(844, 23, 160, 45);

    private bool ContainsHudPoint(Vector2 point)
    {
        if (IsUpgradeOpen) return true;
        if (new Rect(24, 23, 292, 45).Contains(point) || new Rect(1150, 23, 106, 34).Contains(point) ||
            new Rect(588, 674, 104, 25).Contains(point)) return true;
        if (treasureVisible && new Rect(844, 23, 294, 34).Contains(point)) return true;
        if (TimerRect().Contains(point)) return true;
        if (!string.IsNullOrWhiteSpace(pressureStatus) && new Rect(336, 23, 488, 34).Contains(point)) return true;
        for (int slot = 0; slot < 4; slot++)
            if (EquipmentRect(slot).Contains(point)) return true;
        if (abilities.IsScouting)
            return new Rect(24, 639, 136, 45).Contains(point) || new Rect(1148, 639, 108, 45).Contains(point);
        if (new Rect(24, 639, 340, 45).Contains(point)) return true;
        float x = 1256;
        int rightCards = (abilities.HasParrot ? 1 : 0) + (abilities.SaberLevel > 0 ? 1 : 0) +
            (abilities.HookLevel > 0 ? 1 : 0);
        for (int slot = 0; slot < rightCards; slot++)
        {
            x -= 108;
            if (new Rect(x, 639, 108, 45).Contains(point)) return true;
            x -= 8;
        }
        return false;
    }

    private PirateUpgrade? EquipmentUpgrade(int slot)
    {
        switch (slot)
        {
            case 0: return abilities.HookLevel >= 2 ? PirateUpgrade.Hook2 :
                abilities.HookLevel == 1 ? PirateUpgrade.Hook1 : (PirateUpgrade?)null;
            case 1: return abilities.LegLevel >= 3 ? PirateUpgrade.DoubleJump :
                abilities.LegLevel == 2 ? PirateUpgrade.SpringLeg : (PirateUpgrade?)null;
            case 2: return abilities.SaberLevel >= 2 ? PirateUpgrade.Saber2 :
                abilities.SaberLevel == 1 ? PirateUpgrade.Saber1 : (PirateUpgrade?)null;
            case 3: return abilities.HasParrot ? PirateUpgrade.Parrot : (PirateUpgrade?)null;
            default: return null;
        }
    }

    private static Vector2 PointerInHud()
    {
        float scale = Mathf.Max(.001f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
        Vector2 mouse = Mouse.current.position.ReadValue();
        return new Vector2((mouse.x - (Screen.width - 1280f * scale) * .5f) / scale,
            (Screen.height - mouse.y - (Screen.height - 720f * scale) * .5f) / scale);
    }

    private void OnDisable() => ClearUpgrade();
    private void OnDestroy()
    {
        onModalBlocked = null;
        if (Instance == this) Instance = null;
    }

    private void OnGUI()
    {
        if (!initialized || !presentationVisible ||
            (PirateFrontEnd.Instance != null && PirateFrontEnd.Instance.IsBlocking)) return;
        EnsureStyles();
        Matrix4x4 previousMatrix = GUI.matrix;
        Color previousColor = GUI.color;
        Color previousBackground = GUI.backgroundColor;
        int previousDepth = GUI.depth;
        float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
        GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1280f * scale) * .5f,
            (Screen.height - 720f * scale) * .5f), Quaternion.identity, Vector3.one * scale);
        GUI.depth = IsUpgradeOpen ? -80 : -10;
        GUI.color = Color.white;
        DrawLocation();
        DrawEquipment();
        DrawControls();
        DrawMessage();
        if (IsUpgradeOpen) DrawUpgrade();
        GUI.depth = previousDepth;
        GUI.backgroundColor = previousBackground;
        GUI.color = previousColor;
        GUI.matrix = previousMatrix;
    }

    private void DrawLocation()
    {
        Panel(new Rect(24, 23, 292, 45));
        Fill(new Rect(24, 23, 3, 45), Gold);
        Label(new Rect(35, 27, 270, 13), "CHAPTER " + (chapter + 1).ToString("00") + " / 04", tiny, Muted);
        Label(new Rect(35, 41, 270, 22), CampaignChapter.Titles[chapter], heading, Text);
        Panel(new Rect(1150, 23, 106, 34));
        DrawSkull(new Rect(1160, 31, 18, 18), Muted);
        Label(new Rect(1183, 29, 65, 21), life.DeathCount.ToString(), centered, Text);
        if (treasureVisible)
        {
            Panel(new Rect(844, 23, 294, 34));
            DrawTreasureIcon(new Rect(854, 28, 22, 23));
            Label(new Rect(883, 29, 245, 21), ((long)treasureBanked + treasureCarried) +
                "  ·  Found " + treasureRecovered + "/" + treasureTotal, centered, Gold);
        }
        Rect timer = TimerRect();
        Panel(timer);
        Fill(new Rect(timer.x, timer.y, 3, timer.height), Gold);
        Label(new Rect(timer.x + 10, timer.y + 4, timer.width - 16, 13), "TIME", tiny, Muted);
        Label(new Rect(timer.x + 10, timer.y + 18, timer.width - 16, 22),
            PirateScorePresentation.FormatTime(runSeconds), heading, Gold);
        if (!string.IsNullOrWhiteSpace(pressureStatus))
        {
            Panel(new Rect(336, 23, 488, 34));
            Label(new Rect(346, 29, 468, 21), pressureStatus, centered,
                Color.Lerp(Teal, new Color(1f, .43f, .28f), pressureUrgency));
        }
    }

    private void DrawEquipment()
    {
        EquipmentCard(0, PirateArtKind.HookUpgrade, "HOOK", abilities.HookLevel,
            abilities.HookLevel == 1 ? "CHAIN" : "RING", abilities.HookLevel > 0,
            grapple != null && grapple.IsAttached);
        EquipmentCard(1, abilities.LegLevel >= 3 ? PirateArtKind.DoubleJumpLeg : PirateArtKind.SpringLeg,
            "LEG", abilities.LegLevel,
            abilities.LegLevel == 1 ? "WOOD" : abilities.SpringAvailable ? "READY" : "SPENT", true,
            abilities.LegLevel >= 2 && abilities.SpringAvailable, abilities.LegLevel == 1);
        EquipmentCard(2, abilities.SaberLevel >= 2 ? PirateArtKind.SaberUpgrade : PirateArtKind.Saber,
            "SABER", abilities.SaberLevel, abilities.SaberLevel >= 2 ? "SLIDE" : "HIT",
            abilities.SaberLevel > 0, abilities.IsAttacking || abilities.IsSpikeSliding);
        EquipmentCard(3, abilities.IsScouting ? PirateArtKind.ParrotFlap : PirateArtKind.Parrot,
            "PARROT", abilities.HasParrot ? 1 : 0, abilities.IsScouting ? "FLY" : "READY",
            abilities.HasParrot, abilities.IsScouting);
    }

    private void EquipmentCard(int slot, PirateArtKind art, string name, int level, string state,
        bool owned, bool active, bool woodenOnly = false)
    {
        Rect rect = EquipmentRect(slot);
        float y = rect.y;
        Panel(rect);
        bool hovered = !IsUpgradeOpen && EquipmentUpgrade(slot).HasValue &&
            Mouse.current != null && rect.Contains(PointerInHud());
        if (hovered) Fill(new Rect(rect.x + rect.width - 2, rect.y, 2, rect.height), Gold);
        Fill(new Rect(rect.x, rect.y, 2, rect.height), active ? Teal : owned ? Gold : Border);
        DrawSprite(art, new Rect(rect.x + 6, y + 6, 32, 36), owned ? Color.white : new Color(.35f, .39f, .40f), woodenOnly);
        Label(new Rect(rect.x + 42, y + 7, 32, 20), owned ? Roman(level) : "-", centered, owned ? Gold : Muted);
        Label(new Rect(rect.x + 4, y + 45, 72, 13), name, tiny, owned ? Text : Muted);
        if (owned) Label(new Rect(rect.x + 39, y + 29, 38, 12), state, stateText, active ? Teal : Muted);
    }

    private void DrawControls()
    {
        Keyboard keyboard = Keyboard.current;
        Panel(new Rect(588, 674, 104, 25));
        Label(new Rect(593, 677, 94, 19), "ESC   Pause", centered, Text);
        if (abilities.IsScouting)
        {
            Control(24, "W A S D", "Fly", keyboard != null &&
                (keyboard.wKey.isPressed || keyboard.aKey.isPressed || keyboard.sKey.isPressed || keyboard.dKey.isPressed), 136);
            Control(1148, "Q", "Return", keyboard != null && keyboard.qKey.isPressed);
            return;
        }
        Control(24, "A / D", "Move", keyboard != null && (keyboard.aKey.isPressed || keyboard.dKey.isPressed),
            detail: DropShortcut);
        Control(140, "SPACE", grapple != null && grapple.IsAttached ? "Launch" : "Jump", keyboard != null && keyboard.spaceKey.isPressed);
        Control(256, "SHIFT", "Dash", movement.IsDashing);

        float x = 1256;
        if (abilities.HasParrot) { x -= 108; Control(x, "Q", "Scout", false); x -= 8; }
        if (abilities.SaberLevel > 0)
        {
            x -= 108;
            Control(x, SaberShortcut, "Saber", abilities.IsAttacking,
                detail: abilities.SaberLevel >= 2 ? "S + A / D  Slide" : null);
            x -= 8;
        }
        if (abilities.HookLevel > 0)
        {
            x -= 108;
            Control(x, GrappleShortcut, grapple != null && grapple.IsAttached ? "Hold" : "Grapple",
                grapple != null && grapple.IsAttached);
        }
    }

    private void Control(float x, string shortcut, string action, bool pressed, float width = 108f, string detail = null)
    {
        Panel(new Rect(x, 639, width, 45));
        Fill(new Rect(x, 639, width, 2), pressed ? Teal : Border);
        if (string.IsNullOrEmpty(detail))
        {
            Label(new Rect(x + 6, 645, width - 12, 19), shortcut, key, pressed ? Teal : Gold);
            Label(new Rect(x + 6, 666, width - 12, 14), action, centered, Text);
        }
        else
        {
            Label(new Rect(x + 6, 642, width - 12, 18), shortcut, key, pressed ? Teal : Gold);
            Label(new Rect(x + 6, 660, width - 12, 11), action, tiny, Text);
            Label(new Rect(x + 4, 671, width - 8, 12), detail, tiny, Muted);
        }
    }

    private void DrawMessage()
    {
        if (string.IsNullOrWhiteSpace(message) || IsUpgradeOpen) return;
        float height = Mathf.Clamp(body.CalcHeight(new GUIContent(message), 526f) + 16f, 54f, 112f);
        Rect rect = new Rect(360, 606 - height, 560, height);
        Panel(rect);
        Fill(new Rect(rect.x, rect.y, 2, rect.height), Teal);
        Label(new Rect(rect.x + 17, rect.y + 8, rect.width - 34, rect.height - 16), message, body, Text);
    }

    private void DrawUpgrade()
    {
        UpgradeInfo info = Describe(currentUpgrade);
        UpgradeLayout layout = MeasureUpgrade(info);
        Fill(new Rect(-1280, -720, 3840, 2160), new Color(.012f, .022f, .035f, .76f));
        Fill(new Rect(layout.Panel.x - 6, layout.Panel.y - 3, layout.Panel.width + 12,
            layout.Panel.height + 12), new Color(0f, 0f, 0f, .34f));
        Panel(layout.Panel);
        Fill(new Rect(layout.Panel.x, layout.Panel.y, layout.Panel.width, 3), Gold);
        Label(new Rect(layout.Panel.x + 24, layout.Panel.y + 23, 518, 18),
            currentIsReview ? "GEAR GUIDE" : "NEW GEAR", small, Gold);
        Label(layout.Title, info.Title, dialogTitle, Text);
        DrawSprite(info.Art, layout.Icon, Color.white);
        Label(layout.Description, info.Description, dialogBody, Text);
        for (int i = 0; i < info.Keys.Length; i++)
        {
            Fill(layout.Keys[i], new Color(.11f, .16f, .18f));
            Label(layout.Keys[i], info.Keys[i], key, Gold);
            Label(layout.Actions[i], info.Actions[i], dialogAction, Text);
        }
        Fill(layout.Separator, Border);
        Label(layout.Footnote, "Paused. Close this card when ready.", small, Muted);
        bool previousEnabled = GUI.enabled;
        GUI.enabled = previousEnabled && Time.frameCount > openedFrame && Time.unscaledTime - openedAt >= .15f;
        GUI.backgroundColor = new Color(.65f, .48f, .25f);
        bool close = GUI.Button(layout.Acknowledge, UpgradeAcknowledgement, button);
        GUI.backgroundColor = new Color(.13f, .18f, .21f);
        close |= GUI.Button(layout.Close, "X", closeButton);
        GUI.enabled = previousEnabled;
        if (close) DismissUpgrade();
    }

    private sealed class UpgradeLayout
    {
        public Rect Panel, Title, Icon, Description, Separator, Footnote, Acknowledge, Close;
        public Rect[] Keys, Actions;
    }

    private UpgradeLayout MeasureUpgrade(UpgradeInfo info)
    {
        const float textWidth = 394f;
        float titleHeight = Mathf.Max(34f, dialogTitle.CalcHeight(new GUIContent(info.Title), 536f));
        float contentY = 53f + titleHeight + 18f;
        float descriptionHeight = Mathf.Max(62f, dialogBody.CalcHeight(new GUIContent(info.Description), textWidth));
        float keyWidth = 90f;
        foreach (string shortcut in info.Keys)
            keyWidth = Mathf.Max(keyWidth, key.CalcSize(new GUIContent(shortcut)).x + 18f);
        float actionWidth = textWidth - keyWidth - 12f;
        float rowsY = contentY + descriptionHeight + 18f;
        var rowHeights = new float[info.Keys.Length];
        float contentBottom = rowsY;
        for (int i = 0; i < rowHeights.Length; i++)
        {
            rowHeights[i] = Mathf.Max(28f, dialogAction.CalcHeight(new GUIContent(info.Actions[i]), actionWidth) + 6f);
            contentBottom += rowHeights[i] + (i + 1 < rowHeights.Length ? 6f : 0f);
        }
        contentBottom = Mathf.Max(contentBottom, contentY + 152f);
        float separatorY = contentBottom + 20f;
        float panelHeight = separatorY + 94f;
        float top = 360f - panelHeight * .5f;
        var layout = new UpgradeLayout {
            Panel = new Rect(340, top, 600, panelHeight),
            Title = new Rect(364, top + 53f, 536, titleHeight),
            Icon = new Rect(369, top + contentY, 121, 152),
            Description = new Rect(516, top + contentY, textWidth, descriptionHeight),
            Separator = new Rect(364, top + separatorY, 552, 1),
            Footnote = new Rect(364, top + separatorY + 12f, 552, 20),
            Acknowledge = new Rect(508, top + separatorY + 45f, 264, 36),
            Close = new Rect(901, top + 20f, 24, 24),
            Keys = new Rect[info.Keys.Length], Actions = new Rect[info.Keys.Length]
        };
        float rowY = top + rowsY;
        for (int i = 0; i < info.Keys.Length; i++)
        {
            layout.Keys[i] = new Rect(516, rowY, keyWidth, rowHeights[i]);
            layout.Actions[i] = new Rect(516 + keyWidth + 12f, rowY, actionWidth, rowHeights[i]);
            rowY += rowHeights[i] + 6f;
        }
        return layout;
    }

    public bool VerifyUpgradeLayout(PirateUpgrade upgrade, out string details)
    {
        if (Event.current == null || !HasCompleteUpgradeDescription(upgrade))
        { details = "An OnGUI pass and a valid upgrade are required."; return false; }
        EnsureStyles();
        UpgradeInfo info = Describe(upgrade);
        UpgradeLayout layout = MeasureUpgrade(info);
        bool valid = layout.Panel.yMin >= 20f && layout.Panel.yMax <= 700f &&
            layout.Title.yMax + 17f <= layout.Description.yMin &&
            layout.Description.yMax + 17f <= layout.Keys[0].yMin &&
            layout.Acknowledge.yMax < layout.Panel.yMax;
        for (int i = 0; i < info.Keys.Length; i++)
        {
            valid &= key.CalcSize(new GUIContent(info.Keys[i])).x + 16f <= layout.Keys[i].width &&
                dialogAction.CalcHeight(new GUIContent(info.Actions[i]), layout.Actions[i].width) <= layout.Actions[i].height &&
                layout.Keys[i].xMax + 11f <= layout.Actions[i].xMin && layout.Actions[i].xMax <= layout.Panel.xMax - 24f &&
                layout.Actions[i].yMax < layout.Separator.yMin;
            if (i > 0) valid &= layout.Actions[i - 1].yMax + 5f <= layout.Actions[i].yMin;
        }
        details = $"upgrade={upgrade}, valid={valid}, panelHeight={layout.Panel.height:F1}, " +
            $"rows={info.Keys.Length}, keyWidth={layout.Keys[0].width:F1}, measuredFont=True, screenshotProof=False";
        return valid;
    }

    public bool VerifyControlLayout(out string details)
    {
        if (Event.current == null)
        { details = "Control layout requires an OnGUI pass."; return false; }
        EnsureStyles();
        float dropWidth = tiny.CalcSize(new GUIContent(DropShortcut)).x;
        float slideWidth = tiny.CalcSize(new GUIContent("S + A / D  Slide")).x;
        bool valid = dropWidth <= 100f && slideWidth <= 100f &&
            tiny.CalcHeight(new GUIContent(DropShortcut), 100f) <= 12f &&
            key.CalcSize(new GUIContent(SaberShortcut)).x <= 96f &&
            key.CalcSize(new GUIContent(GrappleShortcut)).x <= 96f;
        details = $"valid={valid}, dropWidth={dropWidth:F1}, slideWidth={slideWidth:F1}, " +
            "detailWidth=100, cardHeight=45, dropInsideMovement=True, measuredFont=True";
        return valid;
    }

    private sealed class UpgradeInfo
    {
        public string Title, Description;
        public PirateArtKind Art;
        public string[] Keys, Actions;
        public UpgradeInfo(string title, PirateArtKind art, string description, string[] keys, string[] actions)
        { Title = title; Art = art; Description = description; Keys = keys; Actions = actions; }
    }

    public static bool HasCompleteUpgradeDescription(PirateUpgrade upgrade)
    {
        if (!Enum.IsDefined(typeof(PirateUpgrade), upgrade)) return false;
        UpgradeInfo info = Describe(upgrade);
        if (string.IsNullOrWhiteSpace(info.Title) || string.IsNullOrWhiteSpace(info.Description) ||
            info.Keys == null || info.Actions == null || info.Keys.Length == 0 || info.Keys.Length != info.Actions.Length)
            return false;
        for (int i = 0; i < info.Keys.Length; i++)
            if (string.IsNullOrWhiteSpace(info.Keys[i]) || string.IsNullOrWhiteSpace(info.Actions[i])) return false;
        return true;
    }

    public static string GetUpgradeKeyLabel(PirateUpgrade upgrade, int index)
    {
        if (!Enum.IsDefined(typeof(PirateUpgrade), upgrade)) return string.Empty;
        string[] keys = Describe(upgrade).Keys;
        return index >= 0 && index < keys.Length ? keys[index] : string.Empty;
    }

    public static string GetUpgradeActionLabel(PirateUpgrade upgrade, int index)
    {
        if (!Enum.IsDefined(typeof(PirateUpgrade), upgrade)) return string.Empty;
        string[] actions = Describe(upgrade).Actions;
        return index >= 0 && index < actions.Length ? actions[index] : string.Empty;
    }

    private static UpgradeInfo Describe(PirateUpgrade upgrade)
    {
        switch (upgrade)
        {
            case PirateUpgrade.Hook1: return new UpgradeInfo("Hook I: chains", PirateArtKind.HookUpgrade,
                "Grab a nearby hanging chain. Swing over gaps, then jump off.",
                new[] { GrappleShortcut, "A / D", "SPACE" }, new[] { "Hold to grab a chain", "Swing", "Jump off" });
            case PirateUpgrade.Hook2: return new UpgradeInfo("Hook II: rings", PirateArtKind.HookUpgrade,
                "Reach distant rings with a clear path. Each new ring gives one air boost. Land to recharge. Release RMB before the next grab.",
                new[] { GrappleShortcut, "A / D", "SPACE" }, new[] { "Hold to grab a ring", "Swing", "Boost from a new ring" });
            case PirateUpgrade.SpringLeg: return new UpgradeInfo("Leg II: spring", PirateArtKind.SpringLeg,
                "Land on spikes for one automatic bounce. Touch safe ground to recharge. Spikes still hurt from the side.",
                new[] { "SPIKES", "GROUND" }, new[] { "Land on top to bounce", "Recharge on safe ground" });
            case PirateUpgrade.DoubleJump: return new UpgradeInfo("Leg III: double jump", PirateArtKind.DoubleJumpLeg,
                "Jump once more in the air. Delay the second jump to reach higher ledges.",
                new[] { "SPACE", "SPACE" }, new[] { "Jump from the ground", "Press again in the air" });
            case PirateUpgrade.Saber1: return new UpgradeInfo("Saber I: strike", PirateArtKind.Saber,
                "Cut ropes and fight monsters. Swing to escape a rope snare. Face your target before striking.",
                new[] { SaberShortcut, "A / D" }, new[] { "Strike in front of you", "Face your target" });
            case PirateUpgrade.Saber2: return new UpgradeInfo("Saber II: spike slide", PirateArtKind.SaberUpgrade,
                "Slide safely over spikes. Keep moving to gain speed, then jump farther.",
                new[] { "S + A / D", "SPACE" }, new[] { "Slide over spike tips", "Jump out of a slide" });
            default: return new UpgradeInfo("Parrot: scout", PirateArtKind.Parrot,
                "Explore dark rooms from the air. Your pirate stays behind and can still be hurt. Find a safe perch first.",
                new[] { "Q", "W A S D", "Q" }, new[] { "Send the parrot", "Fly and light the way", "Return to the pirate" });
        }
    }

    private static string Roman(int level) => level >= 3 ? "III" : level == 2 ? "II" : "I";

    private static void DrawSkull(Rect rect, Color color)
    {
        float unit = rect.width / 9f;
        Fill(new Rect(rect.x + unit, rect.y, unit * 7f, unit * 6f), color);
        Fill(new Rect(rect.x + unit * 2f, rect.y + unit * 6f, unit * 5f, unit * 2f), color);
        Fill(new Rect(rect.x + unit * 2f, rect.y + unit * 2f, unit * 2f, unit * 2f), Ink);
        Fill(new Rect(rect.x + unit * 5f, rect.y + unit * 2f, unit * 2f, unit * 2f), Ink);
        Fill(new Rect(rect.x + unit * 4f, rect.y + unit * 4f, unit, unit * 2f), Ink);
        Fill(new Rect(rect.x + unit * 3f, rect.y + unit * 7f, unit, unit), Ink);
        Fill(new Rect(rect.x + unit * 5f, rect.y + unit * 7f, unit, unit), Ink);
    }

    private static void DrawTreasureIcon(Rect target)
    {
        Sprite sprite = PirateTreasureArtLibrary.Load()?.Get(PirateTreasureKind.Doubloon);
        if (sprite == null) return;
        Rect source = sprite.textureRect;
        float scale = Mathf.Min(target.width / source.width, target.height / source.height);
        Rect destination = new Rect(target.center.x - source.width * scale * .5f,
            target.center.y - source.height * scale * .5f, source.width * scale, source.height * scale);
        GUI.DrawTextureWithTexCoords(destination, sprite.texture, new Rect(source.x / sprite.texture.width,
            source.y / sprite.texture.height, source.width / sprite.texture.width, source.height / sprite.texture.height), true);
    }

    private static void Fill(Rect rect, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }

    private static void Panel(Rect rect)
    {
        Fill(rect, Border);
        Fill(new Rect(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2), Ink);
    }

    private static void Label(Rect rect, string text, GUIStyle style, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.Label(rect, text, style);
        GUI.color = previous;
    }

    private static void DrawSprite(PirateArtKind kind, Rect target, Color tint, bool woodenOnly = false)
    {
        PirateWorldArtLibrary library = PirateWorldArt.Library;
        Sprite sprite = library != null ? library.Get(kind) : null;
        if (sprite == null) return;
        Rect bounds = library.GetOpaqueBounds(kind);
        Rect pixels = new Rect(sprite.rect.x + sprite.pivot.x + bounds.x * sprite.pixelsPerUnit,
            sprite.rect.y + sprite.pivot.y + bounds.y * sprite.pixelsPerUnit,
            bounds.width * sprite.pixelsPerUnit, bounds.height * sprite.pixelsPerUnit);
        if (woodenOnly) { pixels.yMin += pixels.height * .48f; }
        float scale = Mathf.Min(target.width / pixels.width, target.height / pixels.height);
        Rect destination = new Rect(target.center.x - pixels.width * scale * .5f,
            target.center.y - pixels.height * scale * .5f, pixels.width * scale, pixels.height * scale);
        Color previous = GUI.color;
        GUI.color = tint;
        GUI.DrawTextureWithTexCoords(destination, sprite.texture, new Rect(pixels.x / sprite.texture.width,
            pixels.y / sprite.texture.height, pixels.width / sprite.texture.width, pixels.height / sprite.texture.height), true);
        GUI.color = previous;
    }

    private void EnsureStyles()
    {
        if (heading != null) return;
        heading = StaticStyle(16, TextAnchor.MiddleLeft, true);
        small = StaticStyle(12, TextAnchor.MiddleLeft);
        tiny = StaticStyle(9, TextAnchor.MiddleCenter);
        stateText = StaticStyle(8, TextAnchor.MiddleCenter);
        centered = StaticStyle(12, TextAnchor.MiddleCenter);
        key = StaticStyle(14, TextAnchor.MiddleCenter, true);
        body = StaticStyle(18, TextAnchor.MiddleLeft);
        body.wordWrap = true;
        dialogBody = StaticStyle(18, TextAnchor.UpperLeft);
        dialogBody.wordWrap = true;
        dialogAction = StaticStyle(14, TextAnchor.MiddleLeft);
        dialogAction.wordWrap = true;
        dialogTitle = StaticStyle(27, TextAnchor.MiddleLeft, true);
        dialogTitle.wordWrap = true;
        button = new GUIStyle(GUI.skin.button) { fontSize = 18, fontStyle = FontStyle.Bold };
        closeButton = new GUIStyle(GUI.skin.button) { fontSize = 13, padding = new RectOffset(0, 0, 0, 0) };
    }

    private static GUIStyle StaticStyle(int size, TextAnchor alignment, bool bold = false)
    {
        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = size,
            alignment = alignment,
            fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
            clipping = TextClipping.Clip,
            padding = new RectOffset(0, 0, 0, 0),
            normal = { textColor = Color.white }
        };
        PirateFrontEnd.KeepLabelStatesIdentical(style);
        return style;
    }
}
