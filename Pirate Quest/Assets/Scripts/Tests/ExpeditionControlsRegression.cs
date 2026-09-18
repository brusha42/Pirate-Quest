using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public static class ExpeditionControlsRegression
{
    public sealed class Result
    {
        public int Checks, RelayJumps;
        public bool ExclusiveControls, RepeatHasNoBoost, DistinctRingBoosts, GroundRecharges;
        public bool ThreeRingAirborne, ThreeRingLanding, HeldMouseDoesNotReattach, StateRestored;
        public bool HudClicksBlocked, EquipmentClickBlocked, AcknowledgementBlocked;
        public string Error;
        public bool Success => string.IsNullOrEmpty(Error) && ExclusiveControls && RepeatHasNoBoost &&
            DistinctRingBoosts && GroundRecharges && ThreeRingAirborne && ThreeRingLanding && HeldMouseDoesNotReattach &&
            HudClicksBlocked && EquipmentClickBlocked && AcknowledgementBlocked && StateRestored;
        public override string ToString() => $"success={Success}, checks={Checks}, exclusive={ExclusiveControls}, " +
            $"repeat={RepeatHasNoBoost}, distinct={DistinctRingBoosts}, ground={GroundRecharges}, " +
            $"relay={ThreeRingAirborne}/{RelayJumps}/{ThreeRingLanding}, held={HeldMouseDoesNotReattach}, " +
            $"hud={HudClicksBlocked}/{EquipmentClickBlocked}/{AcknowledgementBlocked}, restored={StateRestored}, error={Error}";
    }

    public static IEnumerator Run(PlayerMovement player, Action<Result> completed)
    {
        var result = new Result();
        var pending = new Stack<IEnumerator>();
        pending.Push(RunFixture(player, result));
        float deadline = Time.realtimeSinceStartup + 60f;
        try
        {
            while (pending.Count > 0)
            {
                object current = null;
                bool failed = false;
                try
                {
                    if (Time.realtimeSinceStartup > deadline)
                        throw new TimeoutException("Input fixture exceeded its 60-second realtime bound.");
                    IEnumerator iterator = pending.Peek();
                    if (!iterator.MoveNext())
                    {
                        pending.Pop();
                        (iterator as IDisposable)?.Dispose();
                        continue;
                    }
                    current = iterator.Current;
                }
                catch (Exception exception)
                {
                    AppendError(result, "Fixture exception: " + exception.GetType().Name + ": " + exception.Message);
                    failed = true;
                }
                if (failed) break;
                if (current is IEnumerator child) pending.Push(child);
                else yield return current;
            }
        }
        finally
        {
            while (pending.Count > 0)
            {
                try { (pending.Pop() as IDisposable)?.Dispose(); }
                catch (Exception exception) { AppendError(result, "Fixture disposal: " + exception.Message); }
            }
            Debug.Log("PIRATE_EXPEDITION_CONTROLS " + result + " virtualInputSystemEvents=True nativeKeyboardProof=False directAttach=False directLaunch=False fullRouteProof=False");
            completed?.Invoke(result);
        }
    }

    private static void AppendError(Result result, string detail)
    {
        result.Error = string.IsNullOrEmpty(result.Error) ? detail : result.Error + "; " + detail;
    }

    private static IEnumerator RunFixture(PlayerMovement player, Result result)
    {
        PlayerGrapple grapple = player != null ? player.GetComponent<PlayerGrapple>() : null;
        PlayerAbilities abilities = player != null ? player.GetComponent<PlayerAbilities>() : null;
        PlayerLife life = player != null ? player.GetComponent<PlayerLife>() : null;
        PlayerInput input = player != null ? player.GetComponent<PlayerInput>() : null;
        if (!PirateFrontEnd.IsAutomationRun || grapple == null || abilities == null || life == null ||
            input == null || !input.isActiveAndEnabled || input.actions == null ||
            !player.ControlsEnabled || Time.timeScale != 1f || life.IsRespawning)
        {
            result.Error = "Requires an isolated explicit automation run with a live unpaused player.";
            yield break;
        }
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        Vector2 savedPosition = body.position, savedVelocity = body.linearVelocity;
        RigidbodyConstraints2D savedConstraints = body.constraints;
        PirateUpgrade[] savedUpgrades = abilities.CaptureProgression();
        string savedScheme = input.currentControlScheme;
        var savedDevices = new InputDevice[input.devices.Count];
        for (int i = 0; i < savedDevices.Length; i++) savedDevices[i] = input.devices[i];
        bool savedAutoSwitch = input.neverAutoSwitchControlSchemes;
        bool savedUserValid = input.user.valid;
        bool savedInputActive = input.inputIsActive;
        Keyboard savedCurrentKeyboard = Keyboard.current;
        Mouse savedCurrentMouse = Mouse.current;
        InputActionAsset savedActions = input.actions;
        InputActionMap savedActionMap = input.currentActionMap;
        InputBinding? savedBindingMask = savedActions.bindingMask;
        var savedActionStates = new Dictionary<InputAction, bool>();
        foreach (InputAction action in savedActions) savedActionStates.Add(action, action.enabled);
        bool savedProtection = life.IsExitProtected;
        bool savedAcknowledgement = PirateTestModalAcknowledger.SuspendAutomaticAcknowledgement;
        PirateHUD hud = PirateHUD.Instance;
        var objects = new List<GameObject>();
        Keyboard keyboard = null;
        Mouse mouse = null;
        int swings = 0;
        Action onSwing = () => swings++;
        Vector2 origin = new Vector2(11200f, 11200f);
        Vector2 pointer = Vector2.zero;
        var step = new WaitForFixedUpdate();
        void Check(bool condition, string detail)
        {
            result.Checks++;
            if (!condition) AppendError(result, detail);
        }
        void Cleanup(Action action, string detail)
        {
            try { action(); }
            catch (Exception exception) { AppendError(result, detail + ": " + exception.Message); }
        }
        void RestoreActionStates()
        {
            foreach (var state in savedActionStates)
                if (state.Value) state.Key.Enable(); else state.Key.Disable();
        }
        HookAnchor Ring(string name, Vector2 position)
        {
            var obj = new GameObject("Input relay fixture - " + name);
            objects.Add(obj); obj.transform.position = position;
            HookAnchor anchor = obj.AddComponent<HookAnchor>(); anchor.Configure(true);
            return anchor;
        }
        IEnumerator Send(bool right = false, bool left = false, params Key[] keys)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            MouseState state = new MouseState { position = pointer };
            state = state.WithButton(MouseButton.Right, right).WithButton(MouseButton.Left, left);
            InputSystem.QueueStateEvent(mouse, state);
            yield return null;
            if (Time.timeScale > 0f) yield return step;
            else yield return null;
        }
        Vector2 ScreenPoint(float x, float y)
        {
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            return new Vector2((Screen.width - 1280f * scale) * .5f + x * scale,
                Screen.height - (Screen.height - 720f * scale) * .5f - y * scale);
        }
        void Place(Vector2 position)
        {
            player.ResetMotion(); body.position = position; player.transform.position = position;
            Physics2D.SyncTransforms();
        }
        try
        {
            player.ClearAutomationInputOverride(); grapple.ClearAutomationInputOverride();
            abilities.ClearAutomationInputOverride(); abilities.ResetProgression();
            abilities.Apply(PirateUpgrade.Hook2, false); abilities.Apply(PirateUpgrade.Saber1, false);
            life.SetExitProtected(true);
            keyboard = InputSystem.AddDevice<Keyboard>(); mouse = InputSystem.AddDevice<Mouse>();
            keyboard.MakeCurrent(); mouse.MakeCurrent();
            input.neverAutoSwitchControlSchemes = true;
            if (!input.user.valid)
            {
                input.enabled = false;
                input.enabled = true;
                if (input.actions != savedActions)
                    throw new InvalidOperationException("PlayerInput recreated the cached production action asset.");
                RestoreActionStates();
            }
            if (!input.user.valid)
                throw new InvalidOperationException("PlayerInput did not initialize its user after virtual devices were added.");
            input.SwitchCurrentControlScheme("Keyboard&Mouse", keyboard, mouse);
            Check(input.user.valid && input.currentControlScheme == "Keyboard&Mouse" && input.devices.Count == 2 &&
                input.devices[0] != input.devices[1] &&
                (input.devices[0] == keyboard || input.devices[1] == keyboard) &&
                (input.devices[0] == mouse || input.devices[1] == mouse) && savedActions.FindAction("Jump", true).enabled,
                "Virtual devices were not paired to the live production Jump action.");
            Debug.Log($"PIRATE_EXPEDITION_INPUT_SETUP initialUserValid={savedUserValid} pairedUserValid={input.user.valid} " +
                $"devices={input.devices.Count} scheme={input.currentControlScheme} lifecycleInitialization={!savedUserValid}");
            abilities.SaberSwung += onSwing;
            body.constraints = RigidbodyConstraints2D.FreezeAll;
            Place(origin);
            HookAnchor first = Ring("first charge", origin + Vector2.up * 4f);
            HookAnchor second = Ring("second charge", origin + new Vector2(.8f, 7.4f));
            second.gameObject.SetActive(false);

            yield return Send(false, false, Key.J);
            bool jIgnored = swings == 0 && !grapple.IsAttached;
            yield return Send(false, false, Key.F);
            bool fIgnored = swings == 0 && !grapple.IsAttached;
            yield return Send(false, false, Key.E);
            bool eIgnored = swings == 0 && !grapple.IsAttached;
            yield return Send(false, true);
            bool leftWorks = swings == 1 && !grapple.IsAttached;
            yield return Send(true);
            bool rightWorks = grapple.CurrentAnchor == first;
            yield return Send();
            bool rightRelease = !grapple.IsAttached;
            result.ExclusiveControls = jIgnored && fIgnored && eIgnored && leftWorks && rightWorks && rightRelease;
            Check(jIgnored, "J still operates combat/grapple."); Check(eIgnored, "E still attaches.");
            Check(fIgnored, "F still operates combat/grapple."); Check(leftWorks, "LMB failed its actual mouse combat consumer or attached the hook.");
            Check(rightWorks && rightRelease, "RMB press/release failed its actual mouse consumer.");

            for (int i = 0; i < 20; i++) yield return step;
            pointer = ScreenPoint(72f, 653f);
            yield return Send(true, true);
            result.HudClicksBlocked = swings == 1 && !grapple.IsAttached && PirateHUD.PointerBlocksWorld;
            Check(result.HudClicksBlocked, "Movement HUD click leaked a saber swing or hook attachment.");
            yield return Send();
            if (hud != null)
            {
                PirateTestModalAcknowledger.SuspendAutomaticAcknowledgement = true;
                life.SetExitProtected(false);
                pointer = ScreenPoint(1216f, 217f);
                yield return Send(true, true);
                result.EquipmentClickBlocked = hud.IsUpgradeOpen && hud.DisplayedUpgrade == PirateUpgrade.Hook2 &&
                    Time.timeScale == 0f && swings == 1 && !grapple.IsAttached;
                Check(result.EquipmentClickBlocked, "Actual equipment click did not open its card without combat/grapple underneath.");
                hud.DismissUpgrade();
                result.AcknowledgementBlocked = !hud.IsUpgradeOpen && PirateHUD.ConsumedInputThisFrame &&
                    !abilities.TryAttack() && swings == 1;
                Check(result.AcknowledgementBlocked, "The acknowledgement frame allowed an immediate saber action.");
                life.SetExitProtected(true);
                PirateTestModalAcknowledger.SuspendAutomaticAcknowledgement = savedAcknowledgement;
            }
            else Check(false, "Input fixture requires the initialized gameplay HUD for click interception checks.");
            pointer = Vector2.zero;
            yield return Send();

            body.constraints = savedConstraints;
            Place(origin);
            yield return Send(true);
            yield return Send(true, false, Key.Space);
            Check(!grapple.IsAttached && grapple.SpentAnchorCount == 1 && body.linearVelocity.y > 16f,
                "First ring failed its actual Space boost.");
            yield return Send(true);
            result.HeldMouseDoesNotReattach = !grapple.IsAttached && body.linearVelocity.y > 14f;
            Check(result.HeldMouseDoesNotReattach, "Held RMB reattached or Space release cut the ring tap impulse.");
            yield return Send();
            yield return Send(true);
            bool repeatedAnchor = grapple.CurrentAnchor == first && !grapple.IsAnchorJumpAvailable(first);
            float repeatBefore = body.linearVelocity.y;
            yield return Send(true, false, Key.Space);
            result.RepeatHasNoBoost = repeatedAnchor && !grapple.IsAttached && grapple.SpentAnchorCount == 1 &&
                body.linearVelocity.y <= repeatBefore + .05f;
            Check(result.RepeatHasNoBoost, "Regrabbing the same airborne ring restored a free upward impulse.");
            second.gameObject.SetActive(true);
            yield return Send();
            yield return Send(true);
            bool selectedFresh = grapple.CurrentAnchor == second;
            yield return Send(true, false, Key.Space);
            result.DistinctRingBoosts = selectedFresh && grapple.SpentAnchorCount == 2 && body.linearVelocity.y > 16f;
            Check(result.DistinctRingBoosts, "A distinct higher ring did not receive its own boost/selection.");
            yield return Send();

            var floor = new GameObject("Input relay fixture - real floor"); objects.Add(floor);
            for (int i = 0; i < 32; i++) if ((player.GroundLayer.value & (1 << i)) != 0) { floor.layer = i; break; }
            floor.transform.position = origin - Vector2.up;
            floor.AddComponent<BoxCollider2D>().size = new Vector2(12f, 1f);
            Physics2D.SyncTransforms();
            for (int i = 0; i < 220 && !player.IsGrounded; i++) yield return step;
            yield return step;
            result.GroundRecharges = player.IsGrounded && grapple.SpentAnchorCount == 0 && grapple.IsAnchorJumpAvailable(first);
            Check(result.GroundRecharges, "A genuine enabled floor contact did not recharge rings.");
            first.gameObject.SetActive(false); second.gameObject.SetActive(false);

            Place(origin + Vector2.up * .65f);
            HookAnchor[] relay = { Ring("relay 1", origin + new Vector2(0f, 2.6f)),
                Ring("relay 2", origin + new Vector2(.8f, 6f)), Ring("relay 3", origin + new Vector2(0f, 9.4f)) };
            var finish = new GameObject("Input relay fixture - upper landing"); objects.Add(finish);
            finish.layer = floor.layer; finish.transform.position = origin + Vector2.up * 10.65f;
            BoxCollider2D finishCollider = finish.AddComponent<BoxCollider2D>();
            finishCollider.size = new Vector2(3f, .3f); finishCollider.usedByEffector = true;
            PlatformEffector2D oneWay = finish.AddComponent<PlatformEffector2D>();
            oneWay.useOneWay = true; oneWay.useOneWayGrouping = true;
            Physics2D.SyncTransforms();
            for (int i = 0; i < 15 && !player.IsGrounded; i++) yield return step;
            bool remainedAirborne = true;
            for (int ring = 0; ring < relay.Length; ring++)
            {
                if (ring > 0)
                {
                    float grabHeight = relay[ring].transform.position.y - 1.7f;
                    for (int i = 0; i < 70 && body.position.y < grabHeight; i++)
                    {
                        yield return step; remainedAirborne &= !player.IsGrounded;
                        if (body.linearVelocity.y < -.5f) break;
                    }
                }
                yield return Send(true);
                Check(grapple.CurrentAnchor == relay[ring], "Relay selected the wrong/unreachable ring " + ring);
                yield return Send(true, false, Key.Space);
                bool boosted = !grapple.IsAttached && !grapple.IsAnchorJumpAvailable(relay[ring]) && body.linearVelocity.y > 16f;
                if (boosted) result.RelayJumps++;
                Check(boosted, "Relay ring failed a physical tap boost " + ring);
                remainedAirborne &= !player.IsGrounded;
                yield return Send();
            }
            result.ThreeRingAirborne = result.RelayJumps == 3 && remainedAirborne && body.position.y > origin.y + 7.5f;
            Check(result.ThreeRingAirborne, "Three distinct rings did not chain without an intervening ground contact.");

            int spentBeforePause = grapple.SpentAnchorCount;
            player.SetModalInputBlocked(true); Time.timeScale = 0f;
            yield return null; yield return null;
            Check(spentBeforePause == 3 && grapple.SpentAnchorCount == spentBeforePause, "Modal pause replenished ring charges.");
            Time.timeScale = 1f; player.SetModalInputBlocked(false);
            for (int i = 0; i < 150 && !player.IsGrounded; i++) yield return step;
            result.ThreeRingLanding = player.IsGrounded && body.position.y > origin.y + 10.8f;
            Check(result.ThreeRingLanding, "Three-ring route did not land on its +10.8 m upper support.");
            player.ResetMotion();
            Check(grapple.SpentAnchorCount == 0 && !grapple.IsAttached && body.gravityScale > 0f,
                "Reset left spent charges, rope or gravity behind.");
        }
        finally
        {
            if (hud != null) hud.ClearUpgrade();
            PirateTestModalAcknowledger.SuspendAutomaticAcknowledgement = savedAcknowledgement;
            Time.timeScale = 1f; player.SetModalInputBlocked(false);
            abilities.SaberSwung -= onSwing;
            foreach (GameObject obj in objects) if (obj != null) { obj.SetActive(false); UnityEngine.Object.Destroy(obj); }
            Cleanup(() => {
                if (savedUserValid && !string.IsNullOrEmpty(savedScheme)) input.SwitchCurrentControlScheme(savedScheme, savedDevices);
                else if (savedUserValid)
                {
                    input.user.UnpairDevices();
                    input.user.ActivateControlScheme((string)null);
                    foreach (InputDevice device in savedDevices)
                        UnityEngine.InputSystem.Users.InputUser.PerformPairingWithDevice(device, input.user);
                }
                else if (!savedUserValid && input.user.valid) input.user.UnpairDevicesAndRemoveUser();
            }, "Restore paired user");
            Cleanup(() => { if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard); }, "Remove fixture keyboard");
            Cleanup(() => { if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse); }, "Remove fixture mouse");
            Cleanup(() => {
                foreach (InputDevice device in savedDevices) if (device.added) device.MakeCurrent();
                if (savedCurrentKeyboard != null && savedCurrentKeyboard.added) savedCurrentKeyboard.MakeCurrent();
                if (savedCurrentMouse != null && savedCurrentMouse.added) savedCurrentMouse.MakeCurrent();
                input.neverAutoSwitchControlSchemes = savedAutoSwitch;
                input.currentActionMap = savedActionMap;
                if (savedInputActive) input.ActivateInput(); else input.DeactivateInput();
                savedActions.bindingMask = savedBindingMask;
                RestoreActionStates();
            }, "Restore input actions");
            Cleanup(() => {
                abilities.RestoreProgression(savedUpgrades); abilities.ClearAutomationInputOverride();
                grapple.ClearAutomationInputOverride(); player.ClearAutomationInputOverride(); player.ResetMotion();
                body.constraints = savedConstraints; body.position = savedPosition; player.transform.position = savedPosition;
                body.linearVelocity = savedVelocity; life.SetExitProtected(savedProtection); Physics2D.SyncTransforms();
            }, "Restore player state");
            result.StateRestored = body.position == savedPosition && body.constraints == savedConstraints &&
                player.ControlsEnabled && Time.timeScale == 1f && !grapple.IsAttached && grapple.SpentAnchorCount == 0 &&
                input.user.valid == savedUserValid && input.inputIsActive == savedInputActive &&
                input.currentControlScheme == savedScheme && input.devices.Count == savedDevices.Length &&
                (keyboard == null || !keyboard.added) && (mouse == null || !mouse.added);
            foreach (InputDevice device in savedDevices)
            {
                bool paired = false;
                foreach (InputDevice restored in input.devices) paired |= device == restored;
                result.StateRestored &= paired;
            }
            foreach (var state in savedActionStates) result.StateRestored &= state.Key.enabled == state.Value;
            Check(result.StateRestored, "Fixture did not restore movement/time/device state.");
        }
    }
}
