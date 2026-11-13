using System;
using System.Collections.Generic;
using System.Linq;
using Genie.Core.Mapper;
using Xunit;

namespace Genie.Core.Tests;

/// <summary>
/// Regression for #407: the mapper compared a room only with its FIRST stored
/// description, so a room stored night-text-first could never be matched by
/// description during the day, and the loose "any shared exit" fallback then
/// placed the character in the wrong room.
///
/// The fixture is a slice of <c>Map123_Himineldar_Shel.xml</c>'s ice road, where
/// every room is titled <c>Himineldar Shel, Mountain Road</c>. It keeps the real
/// node ids, exits, and map order: 22 (northwest, southeast) comes first, then
/// 23, 27 and 47 share east + northwest, and 36 and 51 share northwest +
/// southwest. Rooms 22, 23, 47 and 51 store two descriptions, night first.
/// Live, the character stood in 47 by day and the mapper said 22.
/// </summary>
public class MapperMultiDescriptionTests
{
    private const string Road = "Himineldar Shel, Mountain Road";

    private const string Night22 = "Dense forest closes in on either side of the path, creating an absolute darkness no matter how bright the stars above the trees.  The rustle of animals and the occasional howl of wolves permeate the night, the only evidence of life.";
    private const string Day22   = "Dense forest closes in on either side of the path, creating the dimness of twilight no matter how bright the sun above the trees.  The rustle of animals can be heard in the distance, and the dark flash of the occasional leaping deer is the only visible sign of life.";
    private const string Night23 = "Delicate and pale, a cluster of white snowdrops blooms in the shadow of the rocks, closed tight against the cold of the night.";
    private const string Day23   = "Delicate and pale, a cluster of white snowdrops blooms in the shelter of the rocks, opening toward the weak mountain sun.";
    private const string Desc27  = "Sparse patches of brambles dot the steep path which winds its way up the mountainside.";
    private const string Desc36  = "The roadway narrows slightly as it hugs the steep exterior of the mountain.";
    private const string Night47 = "As night descends upon this mountainous region, falling temperatures increase the wind's bitter fury.  Blinding clouds of snow race across the slopes, disappearing into the darkness of the night.";
    private const string Day47   = "Incessant storms rage throughout this region, the bitter cold winds a constant companion to travelers.  Blinding clouds of snow race across the slopes, covering everything in an ever-increasing blanket of white.";
    private const string Night51 = "Nightfall covers the region in a blanket of darkness, cloaking the area's scenic treasures until the arrival of day.  Rugged mountains, hidden in shadow, form a protective circle around a small plateau.";
    private const string Day51   = "Majestic snowcapped peaks and an undisturbed blanket of snow are but a few of the treasures this region has to offer.  Rugged mountains form a protective circle around a small plateau.";

    private static MapNode Room(int id, Direction[] exits, params string[] descriptions)
    {
        var n = new MapNode { Id = id, Title = Road, Descriptions = descriptions.ToList() };
        foreach (var d in exits)
            n.Exits.Add(new MapExit { Direction = d, MoveCommand = d.ToString().ToLowerInvariant() });
        return n;
    }

    private static readonly Direction[] NwSe = { Direction.NorthWest, Direction.SouthEast };
    private static readonly Direction[] ENw  = { Direction.East, Direction.NorthWest };
    private static readonly Direction[] NwSw = { Direction.NorthWest, Direction.SouthWest };

    /// <summary>The ice-road slice, inserted in map order (the order the fuzzy
    /// tiers scan in).</summary>
    private static MapZone IceRoad()
    {
        var zone = new MapZone { Name = "Himineldar Shel", Genie4Id = "123" };
        foreach (var n in new[]
                 {
                     Room(22, NwSe, Night22, Day22),
                     Room(23, ENw,  Night23, Day23),
                     Room(27, ENw,  Desc27),
                     Room(36, NwSw, Desc36),
                     Room(47, ENw,  Night47, Day47),
                     Room(51, NwSw, Night51, Day51),
                 })
            zone.Nodes[n.Id] = n;
        return zone;
    }

    private sealed class FakeMapperState : IMapperGameState
    {
        public string RoomTitle       { get; private set; } = string.Empty;
        public string RoomDescription { get; private set; } = string.Empty;
        public string ServerRoomId    => string.Empty;
        public IReadOnlyCollection<string> Exits { get; private set; } = Array.Empty<string>();
        public event Action? StateChanged;

        public void EnterRoom(string title, string description, params string[] exits)
        {
            RoomTitle = title; RoomDescription = description; Exits = exits;
            StateChanged?.Invoke();
        }
    }

    /// <summary>Identify a room from scratch (no previous room, no move: a fresh
    /// #goto, #mapper reset, or reconnect) and report which tier placed it.</summary>
    private static (int? id, string trace) Identify(MapZone zone, string description, params string[] exits)
    {
        var engine = new AutoMapperEngine(zone);
        var log = new List<string>();
        engine.Diagnostics = log.Add;
        var state = new FakeMapperState();
        engine.Attach(state);
        state.EnterRoom(Road, description, exits);
        return (engine.CurrentNode?.Id, string.Join("\n", log));
    }

    [Fact]
    public void Fixture_stores_night_text_first_like_the_real_map()
    {
        var zone = IceRoad();
        Assert.Equal(Night47, zone.Nodes[47].Description);
        Assert.Equal(Day47, zone.Nodes[47].Descriptions[1]);
        // 47's exits are shared, so the exit fingerprint alone can't place it.
        Assert.Equal(3, zone.Nodes.Values.Count(n =>
            MapFingerprint.Compute(n.Title, n.Exits) == MapFingerprint.Compute(Road, new[] { "east", "northwest" })));
    }

    [Fact]
    public void Room_47_by_day_is_room_47_not_room_22()
    {
        var (id, trace) = Identify(IceRoad(), Day47, "east", "northwest");
        Assert.Equal(47, id);
        Assert.Contains("c:fingerprint+desc", trace);
    }

    [Fact]
    public void Room_47_by_night_still_works()
    {
        var (id, _) = Identify(IceRoad(), Night47, "east", "northwest");
        Assert.Equal(47, id);
    }

    [Fact]
    public void Room_51_by_day_is_room_51_not_its_exit_twin_36()
    {
        var (id, _) = Identify(IceRoad(), Day51, "northwest", "southwest");
        Assert.Equal(51, id);
    }

    [Fact]
    public void Description_tier_matches_the_second_description_when_exits_differ_from_the_map()
    {
        // Live shows only northwest (say the east way is blocked): no fingerprint
        // hit, so tier (e) has to find it, by its day text. Before #407 this fell
        // to "any shared exit" and returned 22, the first room with northwest.
        var (id, trace) = Identify(IceRoad(), Day47, "northwest");
        Assert.Equal(47, id);
        Assert.Contains("e:desc-only", trace);
    }

    [Fact]
    public void Description_tier_prefers_the_room_whose_exits_fit_over_the_first_listed()
    {
        // Two same-title rooms with the same text but different exits. Live
        // exits fit only the second; it must win over map order.
        var zone = new MapZone { Name = "Twins" };
        zone.Nodes[1] = Room(1, new[] { Direction.North, Direction.South }, Desc27);
        zone.Nodes[2] = Room(2, new[] { Direction.East, Direction.West, Direction.Up }, Desc27);

        // Live: east + west (the up exit is closed): superset fit for 2, none for 1.
        var (id, trace) = Identify(zone, Desc27, "east", "west");
        Assert.Equal(2, id);
        Assert.Contains("e:desc-only", trace);
    }

    [Fact]
    public void True_twins_resolve_to_the_first_in_the_map()
    {
        var zone = new MapZone { Name = "Twins" };
        zone.Nodes[5] = Room(5, ENw, Desc27);
        zone.Nodes[9] = Room(9, ENw, Desc27);

        var (id, _) = Identify(zone, Desc27, "east", "northwest");
        Assert.Equal(5, id);
    }

    [Theory]
    [InlineData(Night47, true)]
    [InlineData(Day47, true)]
    [InlineData("INCESSANT STORMS RAGE throughout this region, the bitter cold winds a constant companion to travelers.", true)]
    [InlineData("Incessant storms rage throughout this region, the bitter cold winds a constant companion to - different tail", true)]
    [InlineData("Incessant storms rage", false)]
    [InlineData(Day51, false)]
    [InlineData("", false)]
    public void MatchesDescription_checks_every_variant_on_the_first_80_characters(string live, bool expected)
    {
        var room = Room(47, ENw, Night47, Day47);
        Assert.Equal(expected, room.MatchesDescription(live));
    }

    [Fact]
    public void MatchesDescription_skips_empty_stored_descriptions()
    {
        var room = Room(1, ENw, "", Day47);
        Assert.True(room.MatchesDescription(Day47));
        Assert.False(Room(2, ENw).MatchesDescription(Day47));
    }
}
