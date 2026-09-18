using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public static class PirateTreasureRegression
{
    public static bool Verify(out string detail)
    {
        int checks = 0, layouts = 0, items = 0, negative = 0;
        int minCoins = int.MaxValue, minGems = int.MaxValue, minRelics = int.MaxValue;
        int maxCoins = 0, maxGems = 0, maxRelics = 0;
        var campaignCounts = new Dictionary<int, int>();
        void Check(bool condition, string message)
        {
            checks++; if (!condition) throw new InvalidOperationException(message);
        }
        try
        {
            int[] seeds = { 20260918, 42, 0, 1, 7, 91, 113, 512, 999, 2026 };
            foreach (int seed in seeds) for (int chapter = 0; chapter < 4; chapter++)
            {
                CampaignLayout layout = CampaignLayout.Create(seed, chapter, 14f, 34.335f, .02f, PirateMovementProfile.ForChapter(chapter));
                string signature = layout.Signature();
                var first = PirateTreasureLayout.Create(layout);
                var repeated = PirateTreasureLayout.Create(CampaignLayout.Create(seed, chapter, 14f, 34.335f, .02f, PirateMovementProfile.ForChapter(chapter)));
                campaignCounts[seed] = (campaignCounts.TryGetValue(seed, out int existing) ? existing : 0) + first.Count;
                Check(layout.GeometryValid, "Invalid source layout " + seed + "/" + chapter);
                Check(layout.Signature() == signature, "Treasure placement mutated geometry.");
                Check(first.Count == repeated.Count && first.Zip(repeated, (a,b) => a.Key == b.Key && a.Kind == b.Kind &&
                    a.Position == b.Position && a.RouteNode == b.RouteNode && a.Support == b.Support && a.Optional == b.Optional).All(x=>x),
                    "Treasure keys/positions were not deterministic.");
                Check(first.Select(x=>x.Key).Distinct().Count() == first.Count, "Duplicate treasure keys.");
                int coins = first.Count(x=>x.Kind == PirateTreasureKind.Doubloon);
                int gems = first.Count(x=>x.Kind == PirateTreasureKind.Gem);
                int relics = first.Count(x=>x.Kind == PirateTreasureKind.Relic);
                minCoins = Math.Min(minCoins,coins); maxCoins = Math.Max(maxCoins,coins);
                minGems = Math.Min(minGems,gems); maxGems = Math.Max(maxGems,gems);
                minRelics = Math.Min(minRelics,relics); maxRelics = Math.Max(maxRelics,relics);
                Check(coins >= 10 && gems >= 1 && relics >= 1 && relics <= 3 + layout.SecretCaches.Count, "Missing meaningful reward categories.");
                for (int i = 0; i < first.Count; i++)
                {
                    PirateTreasureLayout.Item item = first[i];
                    Check(PirateTreasureLedger.IsValidKey(item.Key) && item.RouteNode >= 0 && item.RouteNode < layout.Route.Count,
                        "Invalid receipt or approach node.");
                    Check(PirateTreasureLayout.Clear(layout,item.Position,item.CacheId >= 0 ? .3f : .8f), "Treasure inside architecture/occupied spawn.");
                    Check(item.Support >= 0 && item.Support < layout.Platforms.Count &&
                        Mathf.Abs(item.Position.y-layout.Platforms[item.Support].SurfaceY-(item.Optional ? .72f : .67f))<.015f,
                        "Treasure has no matching support surface.");
                    for (int j = i+1; j < first.Count; j++)
                        Check(Vector2.Distance(item.Position,first[j].Position) >= .7f, "Overlapping treasure pickups.");
                    if (item.Kind == PirateTreasureKind.Gem && item.CacheId < 0)
                        Check(PirateTreasureLayout.IsGemLanding(layout.Route[item.RouteNode].Action), "Gem is not at a completed ability landing.");
                    if (!item.Optional) continue;
                    if (item.CacheId >= 0)
                    {
                        CampaignLayout.SecretCache cache = layout.SecretCaches.Find(candidate => candidate.Id == item.CacheId);
                        Check(cache != null && cache.Support == item.Support && cache.RewardBounds.Contains(item.Position) &&
                            cache.ChamberBounds.Contains(item.Position) &&
                            layout.HasSafeCacheReturn(cache), "Cache treasure lost its bounded chamber or return certificate.");
                        continue;
                    }
                    Check(PirateTreasureLayout.HasSafeWalkReturn(layout,item.RouteNode,item.Support,item.Position), "Relic has no supported static return corridor.");
                    Check(Mathf.Abs(item.Position.x-layout.Route[item.RouteNode].FeetPosition.x)>=PirateTreasureLayout.MinimumRelicDetour &&
                        PirateTreasureLayout.DistanceToRoute(layout,item.Position-Vector2.up*.72f)>=PirateTreasureLayout.MinimumRelicRouteSeparation,
                        "Relic is on the main route instead of a meaningful short detour.");
                }
                PirateTreasureLayout.Item sample = first.First(x=>x.Optional && x.CacheId < 0);
                Vector2 middle = (layout.Route[sample.RouteNode].FeetPosition + sample.Position-Vector2.up*.72f)*.5f;
                int oldWalls = layout.Solids.Count;
                layout.Solids.Add(new RectInt(Mathf.FloorToInt(middle.x),Mathf.FloorToInt(middle.y),1,2));
                bool blocked = !PirateTreasureLayout.HasSafeWalkReturn(layout,sample.RouteNode,sample.Support,sample.Position);
                layout.Solids.RemoveAt(oldWalls);
                Check(blocked, "Return proof ignored an injected solid wall."); negative++;
                Check(!PirateTreasureLayout.HasSafeWalkReturn(layout,sample.RouteNode,sample.Support,sample.Position+Vector2.up),
                    "Return proof accepted an unsupported airborne relic."); negative++;
                Check(layout.Signature() == signature, "Negative fixture retained its blocker.");
                foreach (CampaignLayout.SecretCache cache in layout.SecretCaches)
                {
                    Check(layout.HasSafeCacheReturn(cache), "Secret cache return is invalid.");
                    Check(first.Count(item => item.CacheId == cache.Id) == 6, "Secret cache lost one of its six certified reward slots.");
                    Check(cache.Bounds == cache.RewardBounds && cache.ChamberBounds.height >= 1.1f &&
                        cache.RewardBounds.xMin >= cache.ChamberBounds.xMin && cache.RewardBounds.xMax <= cache.ChamberBounds.xMax &&
                        cache.RewardBounds.yMin == cache.ChamberBounds.yMin && cache.RewardBounds.yMax <= cache.ChamberBounds.yMax + .001f,
                        "Treasure area is outside the actual capsule-sized chamber.");
                    if (cache.Concealed)
                    {
                        Check(cache.Kind != CampaignLayout.SecretEntranceKind.OpenBranch && cache.CoverBounds.width > 0f &&
                            cache.EntranceBounds.width >= .58f, "Concealed chamber has no explicit entrance/facade contract.");
                        foreach (CampaignLayout.RouteNode routeNode in layout.Route)
                            Check(!cache.CoverBounds.Overlaps(new Rect(routeNode.FeetPosition.x - .29f, routeNode.FeetPosition.y + .035f, .58f, 1.04f)),
                                "Secret facade hides the main-route capsule.");
                    }
                    Vector2 target = cache.ReturnPath[cache.ReturnPath.Count - 1];
                    int before = layout.Solids.Count;
                    layout.Solids.Add(new RectInt(Mathf.FloorToInt(target.x), Mathf.FloorToInt(target.y), 1, 2));
                    Check(!layout.HasSafeCacheReturn(cache), "Cache return certificate ignored an injected wall.");
                    layout.Solids.RemoveAt(before); negative++;
                    Check(layout.Signature() == signature, "Cache negative fixture retained its blocker.");
                }
                int concealed = layout.SecretCaches.Count(cache => cache.Concealed);
                Check(concealed >= CampaignLayout.MinimumConcealedCaches && concealed <= CampaignLayout.MaximumConcealedCaches &&
                    layout.SecretCaches.Count <= CampaignLayout.MaximumSecretCaches, "Concealed cache budget is not met.");
                Check(layout.SecretCaches.Select(cache => cache.Id).SequenceEqual(Enumerable.Range(0, layout.SecretCaches.Count)),
                    "Cache identities are not a deterministic contiguous accepted-candidate sequence.");
                if (chapter < 2)
                {
                    int firstProtection = layout.Spawns.Where(spawn => spawn.Kind == CampaignLayout.SpawnKind.Upgrade &&
                        (spawn.Ability == PirateUpgrade.SpringLeg || spawn.Ability == PirateUpgrade.Saber2))
                        .Select(spawn => spawn.NodeIndex).DefaultIfEmpty(layout.Route.Count).Min();
                    var early = layout.Spawns.Where(spawn => spawn.Kind == CampaignLayout.SpawnKind.Spikes && spawn.Text == "early-pit").ToArray();
                    Check(early.Length > 0 && early.All(spawn => spawn.NodeIndex < firstProtection && spawn.Size.x < 5.9f),
                        "No genuine narrow spike pits before the first protective ability.");
                }
                layouts++; items += first.Count;
            }
            foreach (int count in campaignCounts.Values)
                Check(count <= PirateTreasureLedger.MaximumReceipts, "Collecting all four chapters would overflow the save receipt capacity.");

            var ledger = new PirateTreasureLedger();
            string[] keys = { "t1:0:0123456789ABCDEF:12:0", "t1:1:0123456789ABCDEF:22:1", "t1:2:0123456789ABCDEF:32:2" };
            foreach (string key in keys) { Check(ledger.Collect(key), "First receipt refused."); Check(!ledger.Collect(key), "Duplicate receipt farmed score."); }
            Check(ledger.Count == 3 && ledger.Score == 200, "Score values/idempotence mismatch.");
            string[] captured = ledger.Capture();
            var restored = new PirateTreasureLedger(); restored.Restore(captured);
            Check(restored.Score == ledger.Score && restored.Capture().SequenceEqual(captured), "Ledger snapshot lost score or sorting.");
            captured[0] = "modified caller array";
            Check(restored.Contains(keys[0]) && restored.Score == 200, "Ledger retained mutable snapshot array.");
            string[] invalid = { null, "", " ", keys[0]+"\n", keys[0]+"\r\n", " "+keys[0], keys[0]+" ",
                "t1:4:0123456789ABCDEF:1:0", "t1:0:0123456789abcdef:1:0", "t1:0:0123456789ABCDEF:-1:0",
                "t1:0:0123456789ABCDEF:10000:0", "t1:0:0123456789ABCDEF:1:3", "t2:0:0123456789ABCDEF:1:0" };
            foreach (string key in invalid)
            {
                Check(!PirateTreasureLedger.IsValidKey(key) && !restored.Collect(key) && restored.Score == 200, "Malformed receipt accepted.");
                bool threw = false; try { restored.Restore(new[] { keys[0], key }); } catch (ArgumentException) { threw = true; }
                Check(threw && restored.Score == 200 && restored.Count == 3, "Invalid restore was not rejected atomically."); negative++;
            }
            bool duplicateRejected = false;
            try { restored.Restore(new[] { keys[0],keys[0] }); } catch (ArgumentException) { duplicateRejected = true; }
            Check(duplicateRejected && restored.Score == 200, "Duplicate saved receipt accepted/erased valid state."); negative++;
            var full = new PirateTreasureLedger();
            for (int i = 0; i < PirateTreasureLedger.MaximumReceipts; i++) Check(full.Collect("t1:0:0123456789ABCDEF:"+i+":0"), "Receipt cap rejected valid capacity.");
            Check(!full.Collect("t1:1:0123456789ABCDEF:9999:2") && full.Score == 10240, "Ledger capacity overflowed."); negative++;
            restored.Restore(null); Check(restored.Score == 0 && restored.Count == 0, "Legacy empty receipt state failed.");

            var save = new PirateSaveData { seed = 42, chapter = 2, upgrades = new[] {0,1,2,4},
                treasureReceipts = ledger.Capture(), introSeen = true, deaths = 7 };
            PirateSaveStore.Validate(save);
            PirateSaveData decoded = JsonUtility.FromJson<PirateSaveData>(JsonUtility.ToJson(save));
            PirateSaveStore.Validate(decoded); restored.Restore(decoded.treasureReceipts);
            Check(decoded.seed == 42 && decoded.chapter == 2 && decoded.deaths == 7 && decoded.introSeen &&
                restored.Score == 200 && restored.Capture().SequenceEqual(ledger.Capture()), "Real save DTO JSON roundtrip lost treasure.");
            PirateSaveData legacy = JsonUtility.FromJson<PirateSaveData>("{\"version\":1,\"seed\":42,\"chapter\":1,\"upgrades\":[0,1],\"deaths\":3}");
            PirateSaveStore.Validate(legacy); restored.Restore(legacy.treasureReceipts);
            Check(restored.Score == 0 && !legacy.introSeen && legacy.deaths == 3, "Legacy save without extension is not compatible.");
            foreach (string[] receipts in new[] { new[] { keys[0],keys[0] }, new[] { keys[0]+"\n" }, new string[PirateTreasureLedger.MaximumReceipts+1] })
            {
                bool refused = false;
                try { PirateSaveStore.Validate(new PirateSaveData { treasureReceipts = receipts }); } catch (InvalidDataException) { refused = true; }
                Check(refused, "Save validation accepted invalid receipt extension."); negative++;
            }
            detail = $"PASS layouts={layouts} items={items} checks={checks} negative={negative} coins={minCoins}..{maxCoins} gems={minGems}..{maxGems} relics={minRelics}..{maxRelics} " +
                $"maxCampaignItems={campaignCounts.Values.Max()}/{PirateTreasureLedger.MaximumReceipts} " +
                "deterministic=True staticReturn=True ledgerIdempotent=True actualSaveJson=True diskAccess=False realInputProof=False movingHazardProof=False";
            return true;
        }
        catch (Exception error)
        {
            detail = $"FAIL layouts={layouts} items={items} checks={checks}: {error.Message}";
            return false;
        }
    }
}
