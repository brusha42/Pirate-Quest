using System;
using System.Collections;
using UnityEngine;

public static class BrineCrawlerRegression
{
    public sealed class Result
    {
        public bool Artwork, IdleSafe, Warning, AttackKills, SaberKills, SaberBlockedByWall;
        public bool DefeatSafe, ResetWorks, PatrolBounded, AttackBounded, Cooldown, StableBodyAndFeet;
        public bool FirstHitSurvives, SecondHitSurvives, OneHitPerSwing, DefeatCallbackOnce;
        public float WarningSeconds, AttackInterval;
        public int SeenFrames;
        public string Error;
        public bool Success => string.IsNullOrEmpty(Error) && Artwork && IdleSafe && Warning && AttackKills &&
            SaberKills && SaberBlockedByWall && DefeatSafe && ResetWorks && PatrolBounded && AttackBounded &&
            Cooldown && StableBodyAndFeet && FirstHitSurvives && SecondHitSurvives && OneHitPerSwing &&
            DefeatCallbackOnce && SeenFrames == 15;
        public override string ToString() => $"success={Success}, art={Artwork}/{SeenFrames}, idle={IdleSafe}, " +
            $"warning={Warning}/{WarningSeconds:F3}s, kill={AttackKills}, saber={SaberKills}/{SaberBlockedByWall}, " +
            $"defeatSafe={DefeatSafe}, reset={ResetWorks}, edges={PatrolBounded}/{AttackBounded}, " +
            $"threeHits={FirstHitSurvives}/{SecondHitSurvives}/{SaberKills}, oncePerSwing={OneHitPerSwing}, callbackOnce={DefeatCallbackOnce}, " +
            $"cooldown={Cooldown}/{AttackInterval:F3}s, fixedBodyFeet={StableBodyAndFeet}, error={Error}";
    }

    public static IEnumerator Run(PlayerMovement player, Action<Result> completed)
    {
        var result = new Result();
        PlayerLife life = player != null ? player.GetComponent<PlayerLife>() : null;
        PlayerAbilities abilities = player != null ? player.GetComponent<PlayerAbilities>() : null;
        if (life == null || abilities == null || !player.ControlsEnabled)
        {
            result.Error = "Live production player components are required.";
            completed?.Invoke(result);
            yield break;
        }
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        Collider2D capsule = player.GetComponent<Collider2D>();
        PlayerGrapple grapple = player.GetComponent<PlayerGrapple>();
        Vector2 savedPosition = body.position, savedVelocity = body.linearVelocity;
        Vector3 savedCheckpoint = life.CheckpointPosition;
        PirateUpgrade[] savedUpgrades = abilities.CaptureProgression();
        int savedDeaths = life.DeathCount;
        bool savedControls = player.ControlsEnabled;
        float savedScale = Time.timeScale, savedCapture = Time.captureDeltaTime;
        Vector2 origin = new Vector2(7200f, 7200f);
        var floorObject = new GameObject("Crawler regression support");
        for (int i = 0; i < 32; i++) if ((player.GroundLayer.value & (1 << i)) != 0) { floorObject.layer = i; break; }
        floorObject.transform.position = origin - Vector2.up * 0.5f;
        BoxCollider2D floor = floorObject.AddComponent<BoxCollider2D>();
        floor.size = new Vector2(20f, 1f);
        var wallObject = new GameObject("Crawler saber regression wall");
        wallObject.layer = floorObject.layer;
        wallObject.transform.position = origin + new Vector2(1.7f, 1f);
        BoxCollider2D wall = wallObject.AddComponent<BoxCollider2D>();
        wall.size = new Vector2(0.1f, 2f);
        wall.enabled = false;
        BrineCrawler crawler = null;
        WaitForFixedUpdate step = new WaitForFixedUpdate();
        Vector2 initialBodySize = Vector2.zero;
        int swings = 0, defeatCallbacks = 0;
        bool oneHitPerSwing = true;
        Action onSwing = () => swings++;

        IEnumerator PlacePlayer(float x)
        {
            if (crawler != null) crawler.enabled = false;
            if (life.IsRespawning) life.RespawnImmediately();
            abilities.ResetTransientState();
            player.ResetMotion();
            player.SetControlsEnabled(true);
            player.ClearAutomationInputOverride();
            player.SetAutomationInputOverride(Vector2.zero);
            abilities.ClearAutomationInputOverride();
            grapple?.SetAutomationInputOverride(false);
            body.position = origin + new Vector2(x, capsule.bounds.extents.y + 0.08f);
            player.transform.position = body.position;
            life.SetCheckpoint(body.position);
            Physics2D.SyncTransforms();
            for (int i = 0; i < 30; i++) yield return step;
        }
        void Restart(PlayerLife target)
        {
            crawler.enabled = true;
            crawler.ResetEncounter();
            crawler.SetTarget(target);
            Physics2D.SyncTransforms();
        }
        void ObserveArt()
        {
            result.SeenFrames |= 1 << (int)crawler.CurrentFrame;
            result.StableBodyAndFeet &= crawler.BodySize == initialBodySize &&
                Mathf.Abs(crawler.OpaqueWorldBounds.yMin - crawler.SupportBounds.yMax) < 0.012f &&
                crawler.Renderer.transform.localScale == Vector3.one;
        }
        bool EntireArtworkOnSupport() => crawler.OpaqueWorldBounds.xMin >= crawler.SupportBounds.xMin - 0.012f &&
            crawler.OpaqueWorldBounds.xMax <= crawler.SupportBounds.xMax + 0.012f;
        IEnumerator FaceRightAndSlash()
        {
            player.SetAutomationInputOverride(Vector2.right);
            yield return null;
            yield return step;
            player.SetAutomationInputOverride(Vector2.zero);
            int healthBefore = crawler.Health;
            int swingsBefore = swings;
            abilities.SetAutomationAttackPressed();
            yield return null;
            for (int i = 0; i < 18; i++)
            {
                yield return step;
                oneHitPerSwing &= healthBefore - crawler.Health <= 1;
            }
            oneHitPerSwing &= swings == swingsBefore + 1;
        }

        try
        {
            Time.timeScale = 1f;
            Time.captureDeltaTime = Time.fixedDeltaTime;
            abilities.SaberSwung += onSwing;
            yield return PlacePlayer(2.8f);
            crawler = BrineCrawler.Install(null, origin + Vector2.right * 2.8f,
                new Rect(origin.x - 10f, origin.y - 1f, 20f, 1f), -1);
            result.Artwork = crawler.IsConfigured && crawler.Renderer.sprite != null &&
                crawler.Renderer.sprite.texture.width > 100;
            if (!crawler.IsConfigured)
            {
                result.Error = "Run BrineCrawlerArtImporter.ImportAndVerify before the runtime fixture.";
            }
            else
            {
                initialBodySize = crawler.BodySize;
                result.StableBodyAndFeet = true;
                Restart(null);
                int deaths = life.DeathCount;
                bool overlapped = false;
                for (int i = 0; i < 12; i++)
                {
                    yield return step;
                    overlapped |= capsule.Distance(crawler.GetComponent<Collider2D>()).isOverlapped;
                    ObserveArt();
                }
                result.IdleSafe = overlapped && life.DeathCount == deaths && !crawler.IsAttacking;

                yield return PlacePlayer(0f);
                Restart(life);
                for (int i = 0; i < 10 && !crawler.IsWindingUp; i++) yield return step;
                bool warned = crawler.IsWindingUp;
                float warningStarted = Time.fixedTime;
                deaths = life.DeathCount;
                for (int i = 0; i < 45 && crawler.IsWindingUp; i++) { ObserveArt(); yield return step; }
                result.WarningSeconds = Time.fixedTime - warningStarted;
                result.Warning = warned && crawler.IsAttacking && life.DeathCount == deaths &&
                    result.WarningSeconds >= BrineCrawler.WindupDuration - Time.fixedDeltaTime * 2f;
                ObserveArt();
                for (int i = 0; i < 30 && life.DeathCount == deaths; i++) { yield return step; ObserveArt(); }
                result.AttackKills = life.DeathCount == deaths + 1 && crawler.AttacksStarted == 1;
                crawler.enabled = false;

                yield return PlacePlayer(1.2f);
                abilities.Apply(PirateUpgrade.Saber1, false);
                Restart(null);
                crawler.SetDefeatCallback(() => defeatCallbacks++);
                wall.enabled = true;
                Physics2D.SyncTransforms();
                yield return FaceRightAndSlash();
                result.SaberBlockedByWall = !crawler.IsDefeated && crawler.Health == BrineCrawler.MaximumHealth &&
                    defeatCallbacks == 0;
                wall.enabled = false;
                yield return PlacePlayer(1.2f);
                Restart(null);
                yield return FaceRightAndSlash();
                result.FirstHitSurvives = crawler.Health == 2 && !crawler.IsDefeated && defeatCallbacks == 0;
                yield return FaceRightAndSlash();
                result.SecondHitSurvives = crawler.Health == 1 && !crawler.IsDefeated && defeatCallbacks == 0;
                yield return FaceRightAndSlash();
                result.SaberKills = crawler.Health == 0 && crawler.IsDefeated && !crawler.IsAttacking &&
                    !crawler.GetComponent<Collider2D>().enabled;
                result.OneHitPerSwing = oneHitPerSwing;
                for (int i = 0; i < 5; i++) crawler.Strike();
                result.DefeatCallbackOnce = defeatCallbacks == 1 && crawler.Health == 0;
                deaths = life.DeathCount;
                for (int i = 0; i < 15; i++) yield return step;
                result.DefeatSafe = life.DeathCount == deaths && !crawler.IsAttacking;
                Restart(null);
                result.ResetWorks = crawler.Health == BrineCrawler.MaximumHealth && !crawler.IsDefeated &&
                    !crawler.IsAttacking && crawler.AttacksStarted == 0 && defeatCallbacks == 1 &&
                    crawler.GetComponent<Collider2D>().enabled && crawler.GetComponent<Rigidbody2D>().simulated &&
                    crawler.Renderer.enabled && crawler.Renderer.color == Color.white;

                yield return PlacePlayer(8f);
                crawler.transform.position = origin + Vector2.right * 0.8f;
                crawler.Configure(new Rect(origin.x - 2.2f, origin.y - 1f, 4.4f, 1f), 1);
                Restart(null);
                result.PatrolBounded = true;
                for (int i = 0; i < 340; i++)
                {
                    yield return step;
                    result.PatrolBounded &= EntireArtworkOnSupport();
                    ObserveArt();
                }
                yield return PlacePlayer(3.3f);
                Restart(life);
                result.AttackBounded = true;
                float firstAttack = -1f, secondAttack = -1f;
                for (int i = 0; i < 230 && secondAttack < 0f; i++)
                {
                    yield return step;
                    ObserveArt();
                    result.AttackBounded &= EntireArtworkOnSupport();
                    if (crawler.AttacksStarted >= 1 && firstAttack < 0f) firstAttack = Time.fixedTime;
                    if (crawler.AttacksStarted >= 2) secondAttack = Time.fixedTime;
                }
                result.AttackInterval = secondAttack - firstAttack;
                result.AttackBounded &= firstAttack >= 0f && life.DeathCount == deaths;
                result.Cooldown = secondAttack >= 0f && result.AttackInterval >=
                    BrineCrawler.RecoveryDuration + BrineCrawler.WindupDuration - Time.fixedDeltaTime * 2f;
            }
        }
        finally
        {
            abilities.SaberSwung -= onSwing;
            Time.timeScale = 1f;
            if (crawler != null) { crawler.gameObject.SetActive(false); UnityEngine.Object.Destroy(crawler.gameObject); }
            floorObject.SetActive(false);
            wallObject.SetActive(false);
            UnityEngine.Object.Destroy(floorObject);
            UnityEngine.Object.Destroy(wallObject);
            life.SetCheckpoint(savedCheckpoint);
            if (life.IsRespawning) life.RespawnImmediately();
            life.SetDeathCount(savedDeaths);
            abilities.RestoreProgression(savedUpgrades);
            abilities.ClearAutomationInputOverride();
            player.ClearAutomationInputOverride();
            grapple?.ClearAutomationInputOverride();
            player.ResetMotion();
            player.SetControlsEnabled(savedControls);
            body.position = savedPosition;
            player.transform.position = savedPosition;
            body.linearVelocity = savedVelocity;
            Physics2D.SyncTransforms();
            Time.captureDeltaTime = savedCapture;
            Time.timeScale = savedScale;
        }
        Debug.Log("PIRATE_BRINE_CRAWLER_REGRESSION " + result + " fixtureRelocation=True fullRouteProof=False");
        completed?.Invoke(result);
    }
}
