using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public sealed class PirateFrontEnd : MonoBehaviour
{
    private enum Page { Hidden, Main, Pause, Settings, ConfirmNew, Briefing }
    public static PirateFrontEnd Instance { get; private set; }
    public static bool IsAutomationRun => Environment.GetCommandLineArgs().Any(argument =>
        argument.StartsWith("-pirateQuest",StringComparison.OrdinalIgnoreCase));
    public bool IsBlocking => page != Page.Hidden;
    public bool HasPlayableRun { get; private set; }
    public bool IsBriefingOpen => page == Page.Briefing;
    public static bool IsMenuRegression => Environment.GetCommandLineArgs().Contains("-pirateMenuTest");
    public string RegressionPage => IsMenuRegression ? page.ToString() : string.Empty;
    private const string FullScreenPref = "PirateQuest.FullScreen";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplyShippingDisplayBeforeScene()
    {
        if (IsAutomationRun || IsMenuRegression || PirateSaveRestartProbe.IsRequested) return;
        ApplySavedDisplayMode();
    }

    private PirateGameFlow flow;
    private Page page = Page.Main;
    private Page returnPage;
    private int selected;
    private int buttonCount;
    private bool activateRequested;
    private PirateSaveData saved;
    private string notification;
    private string seedText = "20260918";
    private GUIStyle title;
    private GUIStyle subtitle;
    private GUIStyle label;
    private GUIStyle small;
    private GUIStyle button;
    private GUIStyle field;
    private PirateAnimationLibrary animationLibrary;
    private Texture2D menuBackground;
    private bool pendingBriefing;
    private float briefingOpenedAt;
    private int briefingOpenedFrame;
    public int RegressionRepaints { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetDomain() => Instance = null;

    public static void Bind(PirateGameFlow newFlow)
    {
        if (IsAutomationRun) return;
        if (Instance == null)
        {
            GameObject menu = new GameObject("Pirate Front End");
            DontDestroyOnLoad(menu);
            Instance = menu.AddComponent<PirateFrontEnd>();
        }
        Instance.flow = newFlow;
        PirateSaveStore.TryLoad(out Instance.saved);
        if (Instance.pendingBriefing)
        {
            Instance.pendingBriefing = false;
            Instance.Show(Page.Briefing);
        }
        else if (Instance.HasPlayableRun) Instance.Show(Page.Hidden);
        else Instance.Show(Page.Main);
    }

    private void Update()
    {
        if (PirateSaveRestartProbe.IsRequested) return;
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || flow == null || flow.IsTransitioning) return;
        if (page == Page.Briefing)
        {
            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame ||
                keyboard.escapeKey.wasPressedThisFrame) AcknowledgeBriefing();
            return;
        }
        if (page == Page.Hidden && PirateHUD.ConsumeUpgradeInput()) return;
        if (keyboard.escapeKey.wasPressedThisFrame)
            HandleEscape();
        if (page == Page.Hidden)
        {
            if (keyboard.backspaceKey.wasPressedThisFrame) ReturnToCheckpoint();
            if (keyboard.rKey.wasPressedThisFrame || keyboard.nKey.wasPressedThisFrame)
            {
                seedText = keyboard.nKey.wasPressedThisFrame ? unchecked(Environment.TickCount*397).ToString() : PirateCampaignSession.Seed.ToString();
                returnPage = Page.Pause; Show(Page.ConfirmNew);
            }
            return;
        }
        if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.tabKey.wasPressedThisFrame)
            selected = (selected + 1) % Mathf.Max(1,buttonCount);
        if (keyboard.upArrowKey.wasPressedThisFrame)
            selected = (selected + Mathf.Max(1,buttonCount) - 1) % Mathf.Max(1,buttonCount);
        if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) activateRequested = true;
        if (page == Page.Settings && selected < 2)
        {
            float direction = keyboard.rightArrowKey.wasPressedThisFrame ? 1f : keyboard.leftArrowKey.wasPressedThisFrame ? -1f : 0f;
            if (direction != 0f)
            {
                if (selected == 0) PirateAudio.MusicVolume = Mathf.Clamp01(PirateAudio.MusicVolume+direction*.05f);
                else PirateAudio.SfxVolume = Mathf.Clamp01(PirateAudio.SfxVolume+direction*.05f);
                PlayerPrefs.Save();
            }
        }
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused && HasPlayableRun && page == Page.Hidden && !IsMenuRegression && !IsAutomationRun)
        {
            notification = "Paused while the window is inactive.";
            Show(Page.Pause);
        }
    }

    private void Show(Page next)
    {
        PirateHUD.ConsumeFrameInput();
        page = next; selected = 0; activateRequested = false;
        if (next == Page.Briefing)
        { briefingOpenedAt = Time.unscaledTime; briefingOpenedFrame = Time.frameCount; }
        flow?.SetMenuBlocked(next != Page.Hidden);
        if (next == Page.Main) PirateSaveStore.TryLoad(out saved);
        ApplyMenuMusic(next);
    }

    private void ApplyMenuMusic(Page next)
    {
        if (next == Page.Pause || next == Page.Hidden)
            PirateAudio.PlayGameplay();
        else if (next == Page.Settings && returnPage == Page.Pause)
            PirateAudio.PlayGameplay();
        else
            PirateAudio.PlayMenu();
    }

    private void OnGUI()
    {
        if (PirateSaveRestartProbe.IsRequested) return;
        if (page == Page.Hidden || flow == null) return;
        EnsureStyles();
        if (Event.current.type == EventType.Repaint) RegressionRepaints++;
        float scale = Mathf.Min(Screen.width/1280f,Screen.height/720f);
        Matrix4x4 previousMatrix = GUI.matrix;
        Color previousColor = GUI.color;
        GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width-1280f*scale)*.5f,(Screen.height-720f*scale)*.5f),Quaternion.identity,Vector3.one*scale);
        GUI.color = new Color(.025f,.055f,.085f,.98f);
        GUI.DrawTexture(new Rect(-1000,-1000,3280,2720),Texture2D.whiteTexture);
        if (menuBackground == null) menuBackground = Resources.Load<Texture2D>("PirateMenuBackground");
        if (menuBackground != null)
        {
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(0,0,1280,720),menuBackground,ScaleMode.ScaleAndCrop);
            GUI.color = new Color(.02f,.045f,.07f,.82f);
            GUI.DrawTexture(new Rect(42,57,581,600),Texture2D.whiteTexture);
        }
        GUI.color = new Color(.67f,.47f,.20f,.75f);
        GUI.DrawTexture(new Rect(58,55,1164,2),Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(58,656,1164,2),Texture2D.whiteTexture);
        GUI.color = Color.white;
        if (menuBackground == null) DrawPortrait();
        GUI.Label(new Rect(74,75,550,70),"PIRATE QUEST",title);
        GUI.Label(new Rect(77,665,1100,25),page == Page.Briefing ? "" :
            "↑ / ↓ / Tab  Select     Enter  Confirm     Esc  Back",small);
        if (!string.IsNullOrWhiteSpace(notification)) GUI.Label(new Rect(80,579,520,63),notification,small);

        buttonCount = 0;
        switch (page)
        {
            case Page.Main: DrawMain(); break;
            case Page.Pause: DrawPause(); break;
            case Page.Settings: DrawSettings(); break;
            case Page.ConfirmNew: DrawNewConfirmation(); break;
            case Page.Briefing: DrawBriefing(); break;
        }
        activateRequested = false;
        GUI.color = previousColor;
        GUI.matrix = previousMatrix;
    }

    private void DrawMain()
    {
        GUI.Label(new Rect(80,191,520,43),"EXPEDITION LOG",subtitle);
        if (saved != null)
        {
            GUI.Label(new Rect(80,232,520,51),$"{CampaignChapter.Titles[saved.chapter]}\nSeed {saved.seed} · Deaths {saved.deaths}",small);
            if (Button(294,"Continue")) ContinueSaved();
        }
        else GUI.Label(new Rect(80,239,520,42),"A letter. A tower. One way up.",label);
        if (Button(saved != null ? 353 : 294,"New expedition"))
            OpenNewConfirmation();
        if (Button(saved != null ? 412 : 353,"Settings")) OpenSettings();
        if (Button(saved != null ? 471 : 412,"Quit")) Quit();
    }

    private void DrawPause()
    {
        GUI.Label(new Rect(80,191,520,43),"PAUSED",subtitle);
        if (Button(254,"Resume")) ResumeRun();
        if (Button(313,"Checkpoint  [Backspace]")) ReturnToCheckpoint();
        if (Button(372,"Settings")) OpenSettings();
        if (Button(431,"Save and main menu"))
            SaveAndMainMenu();
        if (Button(490,"Save and quit")) Quit();
    }

    private void DrawSettings()
    {
        GUI.Label(new Rect(80,191,520,43),"SETTINGS",subtitle);
        GUI.Label(new Rect(80,251,520,28),(selected == 0 ? "› " : "")+"Music  "+Mathf.RoundToInt(PirateAudio.MusicVolume*100f)+"%",label);
        float music = GUI.HorizontalSlider(new Rect(87,288,505,25),PirateAudio.MusicVolume,0f,1f);
        GUI.Label(new Rect(80,331,520,28),(selected == 1 ? "› " : "")+"Sound  "+Mathf.RoundToInt(PirateAudio.SfxVolume*100f)+"%",label);
        float effects = GUI.HorizontalSlider(new Rect(87,368,505,25),PirateAudio.SfxVolume,0f,1f);
        if (!Mathf.Approximately(music,PirateAudio.MusicVolume) || !Mathf.Approximately(effects,PirateAudio.SfxVolume))
        {
            PirateAudio.MusicVolume = music; PirateAudio.SfxVolume = effects; PlayerPrefs.Save();
        }
        buttonCount = 2;
        if (Button(421,Screen.fullScreen ? "Display: fullscreen" : "Display: windowed"))
        {
            bool full = !Screen.fullScreen;
            ApplyDisplay(full);
            PlayerPrefs.SetInt(FullScreenPref,full ? 1 : 0); PlayerPrefs.Save();
        }
        if (Button(480,"Back")) Show(returnPage);
        GUI.Label(new Rect(80,543,520,50),"↑ / ↓  Select    ← / →  Volume\nSettings save automatically.",small);
    }

    private void DrawNewConfirmation()
    {
        GUI.Label(new Rect(80,191,520,43),"NEW EXPEDITION",subtitle);
        GUI.Label(new Rect(80,246,520,88),saved != null || HasPlayableRun
            ? "Start a fresh run. Your current run will be kept in a backup file."
            : "Climb from the docks. Find new gear along the way. The seed changes the tower layout.",label);
        GUI.Label(new Rect(80,352,160,32),"Map seed",label);
        seedText = GUI.TextField(new Rect(250,349,340,38),seedText,12,field);
        if (Button(421,"Set sail")) NewRun();
        if (Button(480,"Back")) Show(returnPage);
    }

    private void DrawBriefing()
    {
        GUI.Label(new Rect(80,191,452,43),"THE CAPTAIN'S LETTER",subtitle);
        Sprite letter = PirateTreasureArtLibrary.Load()?.Get(PirateTreasureKind.Letter);
        if (letter != null)
        {
            Rect source = letter.textureRect;
            float ratio = source.width/source.height;
            float width = ratio >= 1f ? 48f : 48f*ratio;
            float height = ratio >= 1f ? 48f/ratio : 48f;
            GUI.DrawTextureWithTexCoords(new Rect(546f+(48f-width)*.5f,185f+(48f-height)*.5f,width,height),
                letter.texture,new Rect(source.x/letter.texture.width,source.y/letter.texture.height,
                    source.width/letter.texture.width,source.height/letter.texture.height),true);
        }
        string[] lines = {
            "You are the captain's courier.",
            "Take his letter to the Pirate King at the top of the tower.",
            "The Black Tide is rising. Only the king can close the sea gates."
        };
        for (int i = 0; i < lines.Length; i++)
            GUI.Label(new Rect(80,250 + i*58,520,53),lines[i],label);
        bool previousEnabled = GUI.enabled;
        GUI.enabled = previousEnabled && CanAcknowledgeBriefing;
        if (Button(468,"Let's go")) AcknowledgeBriefing();
        GUI.enabled = previousEnabled;
    }

    private bool CanAcknowledgeBriefing => page == Page.Briefing && !pendingBriefing && flow != null &&
        !flow.IsTransitioning && Time.frameCount > briefingOpenedFrame && Time.unscaledTime - briefingOpenedAt >= .15f;

    private void AcknowledgeBriefing()
    {
        if (!CanAcknowledgeBriefing) return;
        bool previousSeen = PirateCampaignSession.IntroSeen;
        PirateCampaignSession.IntroSeen = true;
        if (!flow.SaveCampaign())
        {
            PirateCampaignSession.IntroSeen = previousSeen;
            notification = "Could not save the new run: " + PirateSaveStore.LastError;
            return;
        }
        notification = null;
        Show(Page.Hidden);
    }

    private bool Button(float y,string text)
    {
        int index = buttonCount++;
        bool focused = selected == index;
        Color previous = GUI.backgroundColor;
        GUI.backgroundColor = focused ? new Color(.77f,.56f,.27f) : new Color(.22f,.29f,.35f);
        GUI.SetNextControlName("pirate-menu-"+index);
        bool clicked = GUI.Button(new Rect(80,y,520,43),(focused ? "›  " : "   ")+text,button);
        GUI.backgroundColor = previous;
        if (focused && activateRequested) { clicked = true; activateRequested = false; }
        return clicked;
    }

    private void NewRun()
    {
        if (page != Page.ConfirmNew || flow == null || flow.IsTransitioning) return;
        if (!int.TryParse(seedText,out int seed)) { notification = "Enter a whole-number seed from -2147483648 to 2147483647."; return; }
        if (!PirateSaveStore.ArchiveBeforeNewRun()) { notification = "Could not back up the previous run: "+PirateSaveStore.LastError; return; }
        HasPlayableRun = true; notification = null; pendingBriefing = true;
        Show(Page.Briefing);
        flow.RestartWithSeed(seed);
        if (flow != null && !flow.IsTransitioning)
        { pendingBriefing = false; Show(Page.Briefing); }
    }

    private void HandleEscape()
    {
        if (page == Page.Briefing) AcknowledgeBriefing();
        else if (page == Page.Hidden) Show(Page.Pause);
        else if (page == Page.Pause) ResumeRun();
        else if (page == Page.Settings || page == Page.ConfirmNew) Show(returnPage);
    }

    private void ResumeRun() { if (page == Page.Pause) Show(Page.Hidden); }

    private void ReturnToCheckpoint()
    {
        flow.RestartFromCheckpoint();
        if (page == Page.Pause) Show(Page.Hidden);
    }

    private void OpenSettings() { returnPage = page; Show(Page.Settings); }
    private void OpenNewConfirmation()
    {
        returnPage = page; seedText = "20260918"; Show(Page.ConfirmNew);
    }
    private void SaveAndMainMenu()
    {
        if (flow.SaveCampaign()) { notification = "Saved at your last lantern."; Show(Page.Main); }
        else notification = "Could not save. The game will stay open: "+PirateSaveStore.LastError;
    }

    public void PerformRegressionAction(string action)
    {
        if (!IsMenuRegression) throw new InvalidOperationException("Menu actions require -pirateMenuTest.");
        switch (action)
        {
            case "settings": OpenSettings(); break;
            case "back": HandleEscape(); break;
            case "new": OpenNewConfirmation(); break;
            case "confirm-new": NewRun(); break;
            case "pause": HandleEscape(); break;
            case "resume": ResumeRun(); break;
            case "checkpoint": ReturnToCheckpoint(); break;
            case "main": SaveAndMainMenu(); break;
            case "continue": ContinueSaved(); break;
            case "acknowledge-briefing": AcknowledgeBriefing(); break;
            default: throw new ArgumentException("Unknown test menu action.",nameof(action));
        }
    }

    public void SetRegressionSeed(int seed)
    {
        if (!IsMenuRegression || page != Page.ConfirmNew)
            throw new InvalidOperationException("Setting a fixture seed requires the real new-game confirmation page in -pirateMenuTest.");
        seedText = seed.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private void ContinueSaved()
    {
        if (!PirateSaveStore.TryLoad(out PirateSaveData data)) { notification = "Could not read the save. Previous files are unchanged."; return; }
        HasPlayableRun = true; notification = null; pendingBriefing = false;
        PirateCampaignSession.Load(data);
        Show(Page.Hidden);
        SceneManager.LoadScene(CampaignChapter.SceneNames[data.chapter]);
    }

    private void Quit()
    {
        if (HasPlayableRun && flow != null && !flow.SaveCampaign())
        {
            notification = "Could not save. Quit cancelled: "+PirateSaveStore.LastError;
            return;
        }
        Application.Quit();
    }

    private void OnApplicationQuit()
    {
        if (HasPlayableRun && flow != null) flow.SaveCampaign();
        if (PirateSaveRestartProbe.IsRequested)
        {
            PirateSaveRestartProbe.AfterRealQuitAutosave();
            return;
        }
        PlayerPrefs.Save();
    }

    private void DrawPortrait()
    {
        if (animationLibrary == null) animationLibrary = Resources.Load<PirateAnimationLibrary>(PirateAnimationLibrary.ResourceName);
        PirateAnimationClip clip = animationLibrary?.GetClip(PlayerVisualAnimator.AnimationState.Idle);
        Sprite pirate = clip != null ? clip.GetFrame(clip.GetFrameIndex(Time.unscaledTime)) : null;
        if (pirate == null) return;
        Rect textureRect = pirate.textureRect;
        Rect uv = new Rect(textureRect.x/pirate.texture.width,textureRect.y/pirate.texture.height,
            textureRect.width/pirate.texture.width,textureRect.height/pirate.texture.height);
        float ratio = textureRect.width/textureRect.height;
        GUI.DrawTextureWithTexCoords(new Rect(945-360f*ratio*.5f,235,360f*ratio,360f),pirate.texture,uv,true);
        Sprite banner = PirateWorldArt.GetSprite(PirateArtKind.Banner);
        if (banner == null) return;
        Rect source = banner.textureRect;
        GUI.DrawTextureWithTexCoords(new Rect(1114,267,79,181),banner.texture,new Rect(source.x/banner.texture.width,
            source.y/banner.texture.height,source.width/banner.texture.width,source.height/banner.texture.height),true);
    }

    private static void ApplySavedDisplayMode()
    {
        ApplyDisplay(PlayerPrefs.GetInt(FullScreenPref,1) == 1);
    }

    private static void ApplyDisplay(bool full)
    {
        if (!full)
        {
            Screen.fullScreenMode = FullScreenMode.Windowed;
            return;
        }
        int width = Display.main.systemWidth > 0 ? Display.main.systemWidth : Screen.currentResolution.width;
        int height = Display.main.systemHeight > 0 ? Display.main.systemHeight : Screen.currentResolution.height;
        Screen.SetResolution(width, height, FullScreenMode.FullScreenWindow);
    }

    private void EnsureStyles()
    {
        if (title != null) return;
        if (!IsMenuRegression) ApplySavedDisplayMode();
        title = new GUIStyle(GUI.skin.label) { fontSize = 49,fontStyle = FontStyle.Bold,normal = {textColor = new Color(.95f,.77f,.43f)} };
        subtitle = new GUIStyle(GUI.skin.label) { fontSize = 23,fontStyle = FontStyle.Bold,normal = {textColor = new Color(.87f,.75f,.51f)} };
        label = new GUIStyle(GUI.skin.label) { fontSize = 21,wordWrap = true,normal = {textColor = new Color(.86f,.89f,.89f)} };
        small = new GUIStyle(label) { fontSize = 17,normal = {textColor = new Color(.60f,.69f,.73f)} };
        KeepLabelStatesIdentical(title);
        KeepLabelStatesIdentical(subtitle);
        KeepLabelStatesIdentical(label);
        KeepLabelStatesIdentical(small);
        button = new GUIStyle(GUI.skin.button) { fontSize = 20,alignment = TextAnchor.MiddleLeft,padding = new RectOffset(15,15,5,5) };
        field = new GUIStyle(GUI.skin.textField) { fontSize = 23,padding = new RectOffset(9,9,4,4) };
    }

    public static void KeepLabelStatesIdentical(GUIStyle style)
    {
        Color colour = style.normal.textColor;
        foreach (GUIStyleState state in new[] { style.normal, style.hover, style.active, style.focused,
            style.onNormal, style.onHover, style.onActive, style.onFocused })
        {
            state.textColor = colour;
            state.background = null;
        }
    }
}
