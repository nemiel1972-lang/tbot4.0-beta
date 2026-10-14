package ogame

import (
	"github.com/alaingilbert/ogame/pkg/utils"
	"iter"
	"math"
)

type ShipsInfos struct {
	LightFighter   int64
	HeavyFighter   int64
	Cruiser        int64
	Battleship     int64
	Battlecruiser  int64
	Bomber         int64
	Destroyer      int64
	Deathstar      int64
	SmallCargo     int64
	LargeCargo     int64
	ColonyShip     int64
	Recycler       int64
	EspionageProbe int64
	SolarSatellite int64
	Crawler        int64
	Reaper         int64
	Pathfinder     int64
}

func (s ShipsInfos) ToPtr() *ShipsInfos {
	return &s
}

func (s ShipsInfos) Equal(other ShipsInfos) bool {
	for _, ship := range Ships {
		shipID := ship.GetID()
		if s.ByID(shipID) != other.ByID(shipID) {
			return false
		}
	}
	return true
}

func (s ShipsInfos) HasShips() bool {
	return utils.Count2(s.Iter()) > 0
}

func (s ShipsInfos) IsEmpty() bool {
	return !s.HasShips()
}

func (s ShipsInfos) HasFlyableShips() bool {
	return utils.Any2(s.Iter(), func(shipID ID, i int64) bool { return shipID.IsFlyableShip() })
}

func (s ShipsInfos) HasCombatShips() bool {
	return utils.Any2(s.Iter(), func(shipID ID, i int64) bool { return shipID.IsCombatShip() })
}

func (s ShipsInfos) HasCivilShips() bool {
	return utils.Any2(s.Iter(), func(shipID ID, i int64) bool { return shipID.IsCivilShip() })
}

func (s ShipsInfos) Speed(techs IResearches, lfBonuses LfBonuses, characterClass CharacterClass, allianceClass AllianceClass) int64 {
	return utils.Second(s.SlowestShip(techs, lfBonuses, characterClass, allianceClass))
}

func (s ShipsInfos) SlowestShip(techs IResearches, lfBonuses LfBonuses, characterClass CharacterClass, allianceClass AllianceClass) (ID, int64) {
	var slowestShipID ID
	var minSpeed int64 = math.MaxInt64
	for shipID := range s.IterFlyable() {
		shipSpeed := Objs.GetShip(shipID).GetSpeed(techs, lfBonuses, characterClass, allianceClass)
		if shipSpeed < minSpeed {
			minSpeed = shipSpeed
			slowestShipID = shipID
		}
	}
	return slowestShipID, minSpeed
}

func (s ShipsInfos) ToQuantifiables() []Quantifiable {
	out := make([]Quantifiable, 0)
	for shipID, nb := range s.IterFlyable() {
		out = append(out, Quantifiable{ID: shipID, Nbr: nb})
	}
	return out
}

func (s ShipsInfos) FromQuantifiables(in []Quantifiable) (out ShipsInfos) {
	for _, item := range in {
		out.Set(item.ID, item.Nbr)
	}
	return
}

func (s ShipsInfos) Cargo(techs IResearches, lfBonuses LfBonuses, characterClass CharacterClass, multiplier float64, probeRaids bool) (out int64) {
	for shipID, nb := range s.Iter() {
		out += Objs.GetShip(shipID).GetCargoCapacity(techs, lfBonuses, characterClass, multiplier, probeRaids) * nb
	}
	return
}

func (s ShipsInfos) Has(v ShipsInfos) bool {
	for _, ship := range Ships {
		needed := v.ByID(ship.GetID())
		current := s.ByID(ship.GetID())
		if needed > 0 && needed > current {
			return false
		}
	}
	return true
}

func (s ShipsInfos) FleetValue(lfBonuses LfBonuses) (out int64) {
	for shipID, nb := range s.Iter() {
		out += Objs.GetShip(shipID).GetPrice(nb, lfBonuses).Total()
	}
	return
}

func (s ShipsInfos) FleetCost(lfBonuses LfBonuses) (out Resources) {
	for shipID, nb := range s.Iter() {
		out = out.Add(Objs.GetShip(shipID).GetPrice(nb, lfBonuses))
	}
	return
}

func (s ShipsInfos) CountShips() (out int64) {
	for _, nb := range s.Iter() {
		out += nb
	}
	return
}

func (s *ShipsInfos) Add(v ShipsInfos) {
	for _, ship := range Ships {
		shipID := ship.GetID()
		ownNb := s.ByID(shipID)
		otherNb := v.ByID(shipID)
		toSet := utils.Ternary(otherNb != -1, max(ownNb+otherNb, 0), -1)
		s.Set(shipID, toSet)
	}
}

func (s *ShipsInfos) Sub(v ShipsInfos) {
	for _, ship := range Ships {
		shipID := ship.GetID()
		s.Set(shipID, max(s.ByID(shipID)-v.ByID(shipID), 0))
	}
}

func (s *ShipsInfos) AddShips(shipID ID, nb int64) {
	s.Set(shipID, max(s.ByID(shipID)+nb, 0))
}

func (s *ShipsInfos) SubShips(shipID ID, nb int64) {
	s.AddShips(shipID, -1*nb)
}

func (s ShipsInfos) each(clb func(shipID ID, nb int64) bool) {
	for _, ship := range Ships {
		shipID := ship.GetID()
		nb := s.ByID(shipID)
		if nb > 0 {
			if !clb(shipID, nb) {
				return
			}
		}
	}
}

func (s ShipsInfos) eachFlyable(clb func(shipID ID, nb int64) bool) {
	for shipID, nb := range s.Iter() {
		if shipID.IsFlyableShip() {
			if !clb(shipID, nb) {
				return
			}
		}
	}
}

func (s ShipsInfos) Each(clb func(shipID ID, nb int64)) {
	s.each(func(shipID ID, nb int64) bool {
		clb(shipID, nb)
		return true
	})
}

func (s ShipsInfos) EachFlyable(clb func(shipID ID, nb int64)) {
	s.eachFlyable(func(shipID ID, nb int64) bool {
		clb(shipID, nb)
		return true
	})
}

func (s ShipsInfos) Iter() iter.Seq2[ID, int64] {
	return func(yield func(shipID ID, nb int64) bool) {
		s.each(yield)
	}
}

func (s ShipsInfos) IterFlyable() iter.Seq2[ID, int64] {
	return func(yield func(shipID ID, nb int64) bool) {
		s.eachFlyable(yield)
	}
}

func (s ShipsInfos) GetWeaponPower(researches IResearches) (out int64) {
	for _, ship := range Ships {
		out += s.ByShip(ship) * ship.GetWeaponPower(researches)
	}
	return
}

func (s ShipsInfos) GetShieldPower(researches IResearches) (out int64) {
	for _, ship := range Ships {
		out += s.ByShip(ship) * ship.GetShieldPower(researches)
	}
	return
}

func (s ShipsInfos) GetStructuralIntegrity(researches IResearches) (out int64) {
	for _, ship := range Ships {
		out += s.ByShip(ship) * ship.GetStructuralIntegrity(researches)
	}
	return
}

func (s ShipsInfos) ByID(id ID) int64 {
	switch id {
	case LightFighterID:
		return s.LightFighter
	case HeavyFighterID:
		return s.HeavyFighter
	case CruiserID:
		return s.Cruiser
	case BattleshipID:
		return s.Battleship
	case BattlecruiserID:
		return s.Battlecruiser
	case BomberID:
		return s.Bomber
	case DestroyerID:
		return s.Destroyer
	case DeathstarID:
		return s.Deathstar
	case SmallCargoID:
		return s.SmallCargo
	case LargeCargoID:
		return s.LargeCargo
	case ColonyShipID:
		return s.ColonyShip
	case RecyclerID:
		return s.Recycler
	case EspionageProbeID:
		return s.EspionageProbe
	case SolarSatelliteID:
		return s.SolarSatellite
	case CrawlerID:
		return s.Crawler
	case ReaperID:
		return s.Reaper
	case PathfinderID:
		return s.Pathfinder
	default:
		return 0
	}
}

func (s ShipsInfos) ByShip(ship Ship) int64 {
	return s.ByID(ship.GetID())
}

func (s ShipsInfos) Get(v any) int64 {
	switch vv := v.(type) {
	case ID:
		return s.ByID(vv)
	case Ship:
		return s.ByShip(vv)
	default:
		return 0
	}
}

func (s *ShipsInfos) Set(id ID, val int64) {
	switch id {
	case LightFighterID:
		s.LightFighter = val
	case HeavyFighterID:
		s.HeavyFighter = val
	case CruiserID:
		s.Cruiser = val
	case BattleshipID:
		s.Battleship = val
	case BattlecruiserID:
		s.Battlecruiser = val
	case BomberID:
		s.Bomber = val
	case DestroyerID:
		s.Destroyer = val
	case DeathstarID:
		s.Deathstar = val
	case SmallCargoID:
		s.SmallCargo = val
	case LargeCargoID:
		s.LargeCargo = val
	case ColonyShipID:
		s.ColonyShip = val
	case RecyclerID:
		s.Recycler = val
	case EspionageProbeID:
		s.EspionageProbe = val
	case SolarSatelliteID:
		s.SolarSatellite = val
	case CrawlerID:
		s.Crawler = val
	case ReaperID:
		s.Reaper = val
	case PathfinderID:
		s.Pathfinder = val
	}
}

func (s *ShipsInfos) SetShip(ship Ship, val int64) {
	s.Set(ship.GetID(), val)
}

func (s ShipsInfos) String() string {
	return "\n" +
		"  Light Fighter: " + utils.FI64(s.LightFighter) + "\n" +
		"  Heavy Fighter: " + utils.FI64(s.HeavyFighter) + "\n" +
		"        Cruiser: " + utils.FI64(s.Cruiser) + "\n" +
		"     Battleship: " + utils.FI64(s.Battleship) + "\n" +
		"  Battlecruiser: " + utils.FI64(s.Battlecruiser) + "\n" +
		"         Bomber: " + utils.FI64(s.Bomber) + "\n" +
		"      Destroyer: " + utils.FI64(s.Destroyer) + "\n" +
		"      Deathstar: " + utils.FI64(s.Deathstar) + "\n" +
		"    Small Cargo: " + utils.FI64(s.SmallCargo) + "\n" +
		"    Large Cargo: " + utils.FI64(s.LargeCargo) + "\n" +
		"    Colony Ship: " + utils.FI64(s.ColonyShip) + "\n" +
		"       Recycler: " + utils.FI64(s.Recycler) + "\n" +
		"Espionage Probe: " + utils.FI64(s.EspionageProbe) + "\n" +
		"Solar Satellite: " + utils.FI64(s.SolarSatellite) + "\n" +
		"        Crawler: " + utils.FI64(s.Crawler) + "\n" +
		"         Reaper: " + utils.FI64(s.Reaper) + "\n" +
		"     Pathfinder: " + utils.FI64(s.Pathfinder)
}
