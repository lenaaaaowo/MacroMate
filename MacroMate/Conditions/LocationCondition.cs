using System;
using System.Collections.Generic;
using System.Linq;
using MacroMate.Extensions.Dalamaud.Excel;
using MacroMate.Extensions.Dotnet;
using Lumina.Excel.Sheets;

namespace MacroMate.Conditions;

public record class LocationCondition(
    ExcelId<TerritoryType> territory,          // i.e. "Limsa Lominsa Lower Decks"
    ExcelId<PlaceName>? regionOrSubAreaName    // i.e. "The Octant" or "Seasong Grotto"
) : IValueCondition {
    public string ValueName {
        get {
            var names = new List<string?>() {
                territory.DisplayName(),
                regionOrSubAreaName?.DisplayName()
            };
            return String.Join(", ", names.WithoutNull());
        }
    }
    public string NarrowName => regionOrSubAreaName?.DisplayName() ?? territory.DisplayName();

    /// Default: Limsa Lomina Upper Decks
    public LocationCondition() : this(territoryId: 128) {}

    public LocationCondition(
        uint territoryId,
        uint? regionOrSubAreaNameId = null
    ) : this(
        territory: territoryId.Let(id => new ExcelId<TerritoryType>(id)),
        regionOrSubAreaName: regionOrSubAreaNameId?.Let(id => new ExcelId<PlaceName>(id))
    ) {}

    public static LocationCondition Current() {
        return new LocationCondition(
            territory: new ExcelId<TerritoryType>(Env.ClientState.TerritoryType),
            // We prefer sub-area since it's more specific then region. (A sub-area always exists in a region)
            regionOrSubAreaName: Env.PlayerLocationManager.SubAreaName ?? Env.PlayerLocationManager.RegionName
        );
    }


    public bool SatisfiedBy(ICondition other) {
        var otherLocation = other as LocationCondition;
        if (otherLocation == null) { return false; }

        // We check if the names are equal by their string representation instead of their ID.
        // We do this because some areas have many ids mapping to the same name, and we only really care
        // that the name is the same (since we don't want a giant location list filled with 100's of "Mist" entries)

        bool territoryEqual = this.territory.Name().Equals(otherLocation.territory.Name());
        if (!territoryEqual) {
            return false;
        }

        // If regionOrSubAreaName is null, assume we are satisfied (since we know the territory is equal)
        if (regionOrSubAreaName == null) { return true; }

        bool regionOrSubAreaEqual = this.regionOrSubAreaName.Name().Equals(otherLocation.regionOrSubAreaName?.Name());
        return regionOrSubAreaEqual;
    }

    public static IValueCondition.IFactory Factory = new ConditionFactory();
    public IValueCondition.IFactory FactoryRef => Factory;

    class ConditionFactory : IValueCondition.IFactory {
        public string ConditionName => "Location";
        public string ExpressionName => "Location";

        public IValueCondition? Current() => LocationCondition.Current();
        public IValueCondition Default() => new LocationCondition();
        public IValueCondition? FromConditions(CurrentConditions conditions) => conditions.location;

        public IEnumerable<IValueCondition> TopLevel() {
            return Env.DataManager.GetExcelSheet<TerritoryType>()!
                .Where(territoryType => territoryType.PlaceName.RowId != 0)
                .DistinctBy(territoryType => territoryType.PlaceName.RowId)
                .Select(territoryType =>
                    new LocationCondition(territoryId: territoryType.RowId) as IValueCondition
                );
        }

        public IEnumerable<IValueCondition> Narrow(IValueCondition search) {
            // We can only narrow conditions of our type
            var locationCondition = search as LocationCondition;
            if (locationCondition == null) { return new List<IValueCondition>(); }

            // If we already have a regionOrSubAreaNameId then we can't do any further narrowing.
            if (locationCondition.regionOrSubAreaName != null) { return new List<IValueCondition>(); }

            // Otherwise, we can fill out the data using Map Markers!
            //
            // First, a territory might have multiple maps (i.e. multiple floors), so we need all of them
            var locationMaps = Env.DataManager.GetExcelSheet<Map>()!
                .Where(map => map.TerritoryType.RowId == locationCondition.territory.Id);
            var locationMapMarkerRanges = locationMaps.Select(map => (uint)map.MapMarkerRange).ToHashSet();

            // Now for each of the maps we want to pull all the named sub-locations that are part of
            // that map
            return Env.DataManager.GetSubrowExcelSheet<MapMarker>()!
                .SelectMany(x => x)
                .Where(mapMarker =>
                    locationMapMarkerRanges.Contains(mapMarker.RowId) && mapMarker.PlaceNameSubtext.RowId != 0
                )
                .DistinctBy(mapMarker => mapMarker.PlaceNameSubtext.RowId)
                .Select(mapMarker =>
                    locationCondition with { regionOrSubAreaName = new ExcelId<PlaceName>(mapMarker.PlaceNameSubtext.RowId) } as IValueCondition
                );
        }
    }
}
