using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class CampaignLayout
{
    private string KnownAlternative(Region room)
    {
        string id = room.ScenarioId ?? string.Empty;
        if (id.Contains("hook-vault-with-spring"))
            return "Пружина — удобный старт, но обычный прыжок с новым кольцом также работает; требуется только Hook2.";
        if (id.Contains("slide-spring")) return "После скольжения второй участок можно преодолеть двойным прыжком вместо пружины.";
        if (id.Contains("slide-double")) return "Отскок на самом краю первого поля шипов может заменить следующий двойной прыжок.";
        if (id.Contains("three-ring")) return "Предусмотрен маршрут через три кольца; опытный игрок может использовать два. После опоры кольца заменяют следующий DoubleJump.";
        if (id.Contains("spring-return")) return "Отскок необходим, но можно приземлиться на следующую верхнюю балку вместо левого балкона.";
        return null;
    }

    private void AddSpringBed(Region room, float x, float surface, float width)
    {
        AddPlatform(new Vector2(x, surface - .3f), width, room.Id, true, false);
        AddSpawn(SpawnKind.Spikes, new Vector2(x, surface - .15f), new Vector2(width, .3f), Route.Count - 1,
            text: "One bounce, then land safely to recharge.");
    }

    private void FillSpringRecharge(Region room, int direction, Vector2 end)
    {
        float c = room.Bounds.center.x, y = room.Entrance.y;
        ConnectOrdinary(room, new Vector2(c - direction * 8.5f, y), 3.4f);
        AddSpringBed(room, c - direction * 5.4f, y, 2.4f);
        int middle = AddRoutePlatform(new Vector2(c - direction * 2f, y + 3.8f), 1.8f, room.Id,
            TraversalAction.Spring, PirateUpgrade.SpringLeg, "Первый отскок: обязательная безопасная опора возвращает заряд");
        SealLandingToFloor(middle, room);
        AddSpringBed(room, c + direction * .6f, y + 3.8f, 2f);
        int upper = AddRoutePlatform(new Vector2(c + direction * 4.8f, y + 7.6f), 1.6f, room.Id,
            TraversalAction.Spring, PirateUpgrade.SpringLeg, "Второй отскок после реального восстановления заряда");
        SealLandingToFloor(upper, room);
        ConnectOrdinary(room, end, 4.8f);
    }

    private void FillSpringDrop(Region room, int direction, Vector2 end)
    {
        float c = room.Bounds.center.x, y = room.Entrance.y;
        Vector2 takeoff = new Vector2(c - direction * 4f, y + 2f);
        ConnectOrdinary(room, takeoff, 3.4f);
        AddSpringBed(room, c, y - 1f, 4.2f);
        AddRoof(room, c - direction * 6.5f, c + direction * 2.1f, takeoff.y + 1.3f);
        int target = AddRoutePlatform(new Vector2(c + direction * 5f, y + 2.8f), 2.2f, room.Id,
            TraversalAction.SpringDrop, PirateUpgrade.SpringLeg,
            "Спустись на три метра в колодец шипов и отскочи под открытым краем потолка");
        SealLandingToFloor(target, room);
        ConnectOrdinary(room, end, 4.8f);
    }

    private void FillSpringReturn(Region room, int direction, Vector2 end)
    {
        float c = room.Bounds.center.x, y = room.Entrance.y;
        ConnectOrdinary(room, new Vector2(c - direction * 4f, y), 3.4f);
        AddSpringBed(room, c + direction, y, 3f);
        AddRoutePlatform(new Vector2(c - direction * 3f, y + 4f), 2.2f, room.Id,
            TraversalAction.Spring, PirateUpgrade.SpringLeg,
            "Отскок с изменением направления на верхний балкон над началом провала");
        ConnectOrdinary(room, new Vector2(c + direction * 5f, y + 4f), 2.2f);
        SealLandingToFloor(Route.Count - 1, room);
        ConnectOrdinary(room, end, 4.8f);
    }

    private void AddRoof(Region room, float from, float to, float bottom)
    {
        Platforms.Add(new Platform { Bounds = new Rect(Mathf.Min(from, to), bottom, Mathf.Abs(to - from),
            RoomCeilingY(room) - bottom), OneWay = false, Optional = true, RoomIndex = room.Id });
    }

    private void FillDoubleWide(Region room, int direction, Vector2 end)
    {
        float c = room.Bounds.center.x, y = room.Entrance.y;
        ConnectOrdinary(room, new Vector2(c - direction * 3.8f, y), 2.4f);
        int target = AddRoutePlatform(new Vector2(c + direction * 3.8f, y + 4f), 2.2f, room.Id,
            TraversalAction.DoubleJump, PirateUpgrade.DoubleJump,
            "Широкий высокий пролёт: второй прыжок нужен и для высоты, и для воздушного хода");
        SealLandingToFloor(target, room);
        ConnectOrdinary(room, end, 4.8f);
    }

    private void FillDoubleWindow(Region room, int direction, Vector2 end)
    {
        float c = room.Bounds.center.x, y = room.Entrance.y;
        Vector2 takeoff = new Vector2(c - direction * 4f, y);
        ConnectOrdinary(room, takeoff, 2.6f);
        AddRoof(room, takeoff.x - direction * 1.4f, takeoff.x + direction * 1.5f, y + 3.2f);
        int target = AddRoutePlatform(new Vector2(c + direction * 3.2f, y + 3.8f), 2.6f, room.Id,
            TraversalAction.DoubleWindow, PirateUpgrade.DoubleJump,
            "Первый прыжок под козырьком; второй — после выхода из-под его края");
        SealLandingToFloor(target, room);
        ConnectOrdinary(room, end, 4.8f);
    }

    private void AddSlideRun(Region room, int direction, float from, float length, float surface)
    {
        float finish = from + direction * length;
        float left = Mathf.Min(from, finish), floor = RoomBaseY(room) + 1f;
        ConnectOrdinary(room, new Vector2(from - direction * 1.2f, surface), 2.4f);
        Platforms.Add(new Platform { Bounds = new Rect(left, floor, length, surface - .3f - floor),
            OneWay = false, RoomIndex = room.Id });
        AddSpawn(SpawnKind.Spikes, new Vector2((from + finish) * .5f, surface - .15f),
            new Vector2(length, .3f), Route.Count - 1, text: "Hold S + A/D. Slide to the next safe pocket.");
        AddRoof(room, from, finish, surface + 1.3f);
        AddRoutePlatform(new Vector2(finish + direction * .9f, surface), 1.8f, room.Id,
            TraversalAction.SaberSlide, PirateUpgrade.Saber2, "Чистый карман между двумя раздельными полями шипов");
    }

    private void FillSlideRefuges(Region room, int direction, Vector2 end, bool downhill)
    {
        float c = room.Bounds.center.x, y = room.Entrance.y;
        if (downhill)
        {
            AddSlideRun(room, direction, c - direction * 9f, 6f, y + 2f);
            AddSlideRun(room, direction, c + direction * 3f, 6f, y);
        }
        else
        {
            AddSlideRun(room, direction, c - direction * 7f, 6f, y);
            AddSlideRun(room, direction, c + direction * 1.25f, 6f, y);
        }
        ConnectOrdinary(room, end, 4.8f);
    }

    private void FillSlideLaunch(Region room, int direction, Vector2 end)
    {
        float c = room.Bounds.center.x, y = room.Entrance.y;
        AddSlideRun(room, direction, c - direction * 8f, 6f, y);
        Vector2 lip = Route[Route.Count - 1].FeetPosition;
        int jump = AddRoutePlatform(lip + new Vector2(direction * 3.3f, 1.8f), 2.4f, room.Id,
            TraversalAction.Jump, null, "После чистого пускового выступа — обычный прыжок на узкую ступень; новый протез не нужен");
        SealLandingToFloor(jump, room);
        ConnectOrdinary(room, end, 4.8f);
    }

    private void ValidateStandaloneTrials()
    {
        if (Chapter == 3)
        {
            var widths = new HashSet<int>();
            var shafts = new HashSet<int>();
            foreach (Region room in Rooms)
                if (room.IsLeaf)
                {
                    if (room.IsShaft) shafts.Add(room.Bounds.xMin);
                    else widths.Add(room.Bounds.width);
                }
            if (widths.Count < 5 || shafts.Count < 7 || PlannedChambers != 12)
                ValidationErrors.Add("Shifted observatories lost their different chamber widths or moving shaft positions.");
        }
        foreach (Region room in Rooms)
        {
            if (!room.IsLeaf || room.IsShaft || room.ScenarioId == null) continue;
            string id = room.ScenarioId;
            if (id == "arsenal.spring-recharge-relay")
            {
                int springs = 0;
                for (int node = room.FirstRouteNode; node <= room.LastRouteNode; node++)
                {
                    if (Route[node].Action != TraversalAction.Spring) continue;
                    springs++;
                    Platform landing = Platforms[Route[node].PlatformIndex];
                    if (landing.Bounds.width < 1.59f || landing.Bounds.height < .1f ||
                        Mathf.Abs(Route[node].FeetPosition.y - Route[node - 1].FeetPosition.y - 3.8f) > .015f)
                        ValidationErrors.Add("Spring recharge relay lost an actual safe landing between its bounces.");
                }
                if (springs != 2) ValidationErrors.Add("Spring recharge relay needs two separate bounce transitions.");
            }
            if (id == "arsenal.spring-drop-chute")
            {
                int node = Route.FindIndex(item => item.RoomIndex == room.Id && item.Action == TraversalAction.SpringDrop);
                Spawn bed = node > 0 ? Spawns.Find(item => item.Kind == SpawnKind.Spikes && item.NodeIndex == node - 1) : null;
                float top = bed != null ? bed.Position.y + bed.Size.y * .5f : float.NaN;
                bool hood = false;
                foreach (Platform roof in Platforms)
                    if (node > 0 && roof.RoomIndex == room.Id && !roof.OneWay &&
                        Mathf.Abs(roof.Bounds.yMin - Route[node - 1].FeetPosition.y - 1.3f) < .015f && roof.Bounds.width > 8f) hood = true;
                if (node < 1 || bed == null || !hood || Mathf.Abs(Route[node - 1].FeetPosition.y - top - 3f) > .015f ||
                    Mathf.Abs(Route[node].FeetPosition.y - top - 3.8f) > .015f)
                    ValidationErrors.Add("Spring drop chute lost its three-metre descent, departure hood or safe bounce rise.");
            }
            if (id == "crown.double-window")
            {
                int node = Route.FindIndex(item => item.RoomIndex == room.Id && item.Action == TraversalAction.DoubleWindow);
                bool hood = false;
                if (node > 0)
                    foreach (Platform roof in Platforms)
                        if (roof.RoomIndex == room.Id && !roof.OneWay && Mathf.Abs(roof.Bounds.width - 2.9f) < .015f &&
                            Mathf.Abs(roof.Bounds.yMin - Route[node - 1].FeetPosition.y - 3.2f) < .015f) hood = true;
                if (!hood || node < 1 || Mathf.Abs(Route[node].FeetPosition.y - Route[node - 1].FeetPosition.y - 3.8f) > .015f ||
                    Mathf.Abs(Mathf.Abs(Route[node].FeetPosition.x - Route[node - 1].FeetPosition.x) - 7.2f) > .015f)
                    ValidationErrors.Add("Double-window trial lost its solid hood or verified exit envelope.");
            }
            if (id == "crown.double-wide-vault")
            {
                int node = Route.FindIndex(item => item.RoomIndex == room.Id && item.Action == TraversalAction.DoubleJump);
                if (node < 1 || Mathf.Abs(Mathf.Abs(Route[node].FeetPosition.x - Route[node - 1].FeetPosition.x) - 7.6f) > .015f ||
                    Mathf.Abs(Route[node].FeetPosition.y - Route[node - 1].FeetPosition.y - 4f) > .015f)
                    ValidationErrors.Add("Wide double-jump vault lost its high long-gap contract.");
            }
            if (id == "crown.slide-refuges" || id == "crown.slide-descending-terraces")
            {
                int beds = 0;
                foreach (Spawn spike in Spawns)
                {
                    if (spike.Kind != SpawnKind.Spikes || spike.NodeIndex < room.FirstRouteNode || spike.NodeIndex > room.LastRouteNode) continue;
                    beds++;
                    Rect bed = new Rect(spike.Position - spike.Size * .5f, spike.Size);
                    bool foundation = false, roof = false;
                    foreach (Platform slab in Platforms)
                    {
                        if (slab.RoomIndex != room.Id || slab.OneWay || slab.Bounds.xMin > bed.xMin + .015f || slab.Bounds.xMax < bed.xMax - .015f) continue;
                        if (slab.Bounds.yMin <= RoomBaseY(room) + 1.015f && Mathf.Abs(slab.Bounds.yMax - bed.yMin) < .015f) foundation = true;
                        if (Mathf.Abs(slab.Bounds.yMin - bed.yMax - 1.3f) < .015f && slab.Bounds.yMax >= RoomCeilingY(room) - .015f) roof = true;
                    }
                    if (!foundation || !roof || bed.width < 5.99f)
                        ValidationErrors.Add("Separated slide segment lost its own sealed foundation or roof.");
                }
                if (beds != 2) ValidationErrors.Add("Slide refuge trial needs two separate spike fields and its real clean pocket.");
            }
        }
    }

    private static string RunStandaloneTrialSelfTests()
    {
        CampaignLayout relay = Create(42, 1, 14f, 34.335f, .02f, 6.6f);
        Region relayRoom = relay.Rooms.Find(room => room.ScenarioId == "arsenal.spring-recharge-relay");
        RouteNode middle = relay.Route.Find(node => node.RoomIndex == relayRoom.Id && node.Action == TraversalAction.Spring);
        Platform recharge = relay.Platforms[middle.PlatformIndex];
        recharge.Bounds = new Rect(recharge.CenterX - .2f, recharge.Bounds.yMin, .4f, recharge.Bounds.height);
        if (relay.Validate(out _) || !relay.ValidationErrors.Exists(error => error.StartsWith("Spring recharge relay lost")))
            throw new InvalidOperationException("Missing recharge landing regression was not rejected.");
        CampaignLayout window = Create(42, 3, 14f, 34.335f, .02f, 8f);
        Region windowRoom = window.Rooms.Find(room => room.ScenarioId == "crown.double-window");
        Platform hood = window.Platforms.Find(platform => platform.RoomIndex == windowRoom.Id && !platform.OneWay && Mathf.Abs(platform.Bounds.width - 2.9f) < .015f);
        hood.OneWay = true;
        if (window.Validate(out _) || !window.ValidationErrors.Exists(error => error.StartsWith("Double-window trial lost")))
            throw new InvalidOperationException("One-way double-window hood regression was not rejected.");
        CampaignLayout slide = Create(42, 3, 14f, 34.335f, .02f, 8f);
        Region slideRoom = slide.Rooms.Find(room => room.ScenarioId == "crown.slide-descending-terraces");
        Spawn secondBed = null;
        foreach (Spawn spawn in slide.Spawns)
            if (spawn.Kind == SpawnKind.Spikes && spawn.NodeIndex >= slideRoom.FirstRouteNode && spawn.NodeIndex <= slideRoom.LastRouteNode) secondBed = spawn;
        Rect secondBounds = new Rect(secondBed.Position - secondBed.Size * .5f, secondBed.Size);
        Platform secondBase = slide.Platforms.Find(platform => platform.RoomIndex == slideRoom.Id && !platform.OneWay &&
            Mathf.Abs(platform.Bounds.yMax - secondBounds.yMin) < .015f && Mathf.Abs(platform.Bounds.xMin - secondBounds.xMin) < .015f);
        secondBase.OneWay = true;
        if (slide.Validate(out _) || !slide.ValidationErrors.Exists(error => error.StartsWith("Separated slide segment lost")))
            throw new InvalidOperationException("Second slide-foundation regression was not rejected.");
        CampaignLayout firstCatalogue = Create(42, 3, 14f, 34.335f, .02f, 8f);
        CampaignLayout secondCatalogue = Create(20260918, 3, 14f, 34.335f, .02f, 8f);
        if (firstCatalogue.ScenarioVariant == secondCatalogue.ScenarioVariant ||
            firstCatalogue.Rooms.Find(room => room.ScenarioId == "crown.double-window").Band ==
                secondCatalogue.Rooms.Find(room => room.ScenarioId == "crown.double-window").Band)
            throw new InvalidOperationException("Two curated late-game scenario catalogues did not differ.");
        return "newStandaloneFamilies=8, shiftedCrownVerified=True, catalogueVariants=2, rechargeLandingRejected=True, doubleHoodRejected=True, secondSlideBaseRejected=True";
    }
}
