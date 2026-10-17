package wrapper

import (
	"errors"
	"github.com/alaingilbert/ogame/pkg/ogame"
	"github.com/alaingilbert/ogame/pkg/utils"
	"log"
	"time"
)

var ErrInvalidOrigin = errors.New("invalid origin")

type FleetBuilderFactory struct {
	b Wrapper
}

func NewFleetBuilderFactory(b Wrapper) *FleetBuilderFactory {
	return &FleetBuilderFactory{b: b}
}

func (f FleetBuilderFactory) NewFleet() *FleetBuilder {
	return NewFleetBuilder(f.b)
}

type FleetBuilder struct {
	b                Wrapper
	tx               Prioritizable
	origin           Celestial
	destination      ogame.Coordinate
	speed            ogame.Speed
	mission          ogame.MissionID
	ships            ogame.ShipsInfos
	resources        ogame.Resources
	err              error
	fleet            ogame.Fleet
	minimumDeuterium int64
	holdingTime      int64
	unionID          int64
	allShips         bool
	recallIn         int64
	successCallbacks []func(ogame.Fleet)
	errorCallbacks   []func(error)
}

func NewFleetBuilder(b Wrapper) *FleetBuilder {
	fb := new(FleetBuilder)
	fb.b = b
	fb.mission = ogame.Transport
	fb.speed = ogame.HundredPercent
	return fb
}

func (f *FleetBuilder) SetTx(tx Prioritizable) *FleetBuilder {
	f.tx = tx
	return f
}

func (f *FleetBuilder) SetOrigin(v IntoCelestial) *FleetBuilder {
	f.origin, _ = f.b.GetCachedCelestial(v)
	return f
}

func (f *FleetBuilder) SetDestination(v IntoCoordinate) *FleetBuilder {
	f.destination, _ = ConvertIntoCoordinate(f.b, v)
	return f
}

func (f *FleetBuilder) SetSpeed(speed ogame.Speed) *FleetBuilder {
	f.speed = speed
	return f
}

func (f *FleetBuilder) SetResources(resources ogame.Resources) *FleetBuilder {
	f.resources = resources
	return f
}

func (f *FleetBuilder) SetMetal(metal int64) *FleetBuilder {
	f.resources.Metal = max(metal, -1)
	return f
}

func (f *FleetBuilder) SetCrystal(crystal int64) *FleetBuilder {
	f.resources.Crystal = max(crystal, -1)
	return f
}

func (f *FleetBuilder) SetDeuterium(deuterium int64) *FleetBuilder {
	f.resources.Deuterium = max(deuterium, -1)
	return f
}

func (f *FleetBuilder) SetAllResources() *FleetBuilder {
	f.resources = ogame.Resources{Metal: -1, Crystal: -1, Deuterium: -1}
	return f
}

func (f *FleetBuilder) SetAllMetal() *FleetBuilder {
	f.resources.Metal = -1
	return f
}

func (f *FleetBuilder) SetAllCrystal() *FleetBuilder {
	f.resources.Crystal = -1
	return f
}

func (f *FleetBuilder) SetAllDeuterium() *FleetBuilder {
	f.resources.Deuterium = -1
	return f
}

func (f *FleetBuilder) SetMinimumDeuterium(minimumDeuterium int64) *FleetBuilder {
	f.minimumDeuterium = minimumDeuterium
	return f
}

func (f *FleetBuilder) SetMission(mission ogame.MissionID) *FleetBuilder {
	f.mission = mission
	return f
}

func (f *FleetBuilder) SetDuration(holdingTime int64) *FleetBuilder {
	f.holdingTime = holdingTime
	return f
}

func (f *FleetBuilder) SetUnionID(unionID int64) *FleetBuilder {
	f.unionID = unionID
	return f
}

func (f *FleetBuilder) AddShips(id ogame.ID, nbr int64) *FleetBuilder {
	f.ships.Set(id, f.ships.ByID(id)+nbr)
	return f
}

func (f *FleetBuilder) SetShips(ships ogame.ShipsInfos) *FleetBuilder {
	f.ships = ships
	return f
}

func (f *FleetBuilder) SetAllShips() *FleetBuilder {
	f.allShips = true
	return f
}

func (f *FleetBuilder) SetRecallIn(secs int64) *FleetBuilder {
	f.recallIn = secs
	return f
}

func (f *FleetBuilder) FlightTime() (secs, fuel int64) {
	return f.flightTime(true)
}

func (f *FleetBuilder) FastFlightTime() (secs, fuel int64) {
	return f.flightTime(false)
}

func (f *FleetBuilder) Cargo() int64 {
	return f.cargo()
}

func (f *FleetBuilder) CargoUsing(techs ogame.IResearches, lfBonuses ogame.LfBonuses) int64 {
	return f.cargoUsing(techs, lfBonuses)
}

func (f *FleetBuilder) cargo() int64 {
	var w Prioritizable = f.b
	if f.tx != nil {
		w = f.tx
	}
	techs := w.GetCachedResearch()
	lfBonuses, _ := w.GetCachedLfBonuses()
	return f.cargoUsing(techs, lfBonuses)
}

func (f *FleetBuilder) cargoUsing(techs ogame.IResearches, lfBonuses ogame.LfBonuses) int64 {
	multiplier := float64(f.b.GetServerData().CargoHyperspaceTechMultiplier) / 100.0
	return f.ships.Cargo(techs, lfBonuses, f.b.CharacterClass(), multiplier, f.b.GetServer().OGameSettings().ProbeRaidsEnabled())
}

func (f *FleetBuilder) flightTime(checkTarget bool) (secs, fuel int64) {
	var w Prioritizable = f.b
	if f.tx != nil {
		w = f.tx
	}
	origin := f.origin
	if origin == nil {
		f.err = ErrInvalidOrigin
		return 0, 0
	}
	ships := f.ships
	if f.allShips {
		ships, _ = w.GetShips(origin.GetID())
	}
	flightTimeFn := utils.Ternary(checkTarget, w.FlightTime, w.FastFlightTime)
	return flightTimeFn(origin.GetCoordinate(), f.destination, f.speed, ships, f.mission, f.holdingTime)
}

func (f *FleetBuilder) sendNow(tx Prioritizable) error {
	if f.origin == nil {
		f.err = ErrInvalidOrigin
		return f.err
	}

	if f.allShips {
		allShips, err := tx.GetShips(f.origin.GetID())
		if err != nil {
			f.err = err
			return f.err
		}
		f.ships = allShips
	}

	var fuel int64
	var planetResources ogame.Resources
	if f.minimumDeuterium > 0 {
		res, err := tx.GetResources(f.origin.GetID())
		if err != nil {
			f.err = err
			return f.err
		}
		planetResources = res
		_, fuel = tx.FlightTime(f.origin.GetCoordinate(), f.destination, f.speed, f.ships, f.mission, f.holdingTime)
	}

	if f.minimumDeuterium > 0 && f.resources.Deuterium > 0 {
		planetResources.Deuterium = max(0, planetResources.Deuterium-(fuel+10)-f.minimumDeuterium)
		f.resources.Deuterium = min(f.resources.Deuterium, planetResources.Deuterium)
	}

	payload := f.resources
	if f.resources.Metal == -1 || f.resources.Crystal == -1 || f.resources.Deuterium == -1 {
		cargoCapacity := f.cargo()
		if f.minimumDeuterium <= 0 {
			res, err := tx.GetResources(f.origin.GetID())
			if err != nil {
				f.err = err
				return f.err
			}
			planetResources = res
		}
		if f.resources.Deuterium == -1 {
			if f.minimumDeuterium > 0 {
				planetResources.Deuterium = max(0, planetResources.Deuterium-(fuel+10)-f.minimumDeuterium)
			}
			payload.Deuterium = min(cargoCapacity, planetResources.Deuterium)
			cargoCapacity -= payload.Deuterium
		}
		if f.resources.Crystal == -1 {
			payload.Crystal = min(cargoCapacity, planetResources.Crystal)
			cargoCapacity -= payload.Crystal
		}
		if f.resources.Metal == -1 {
			payload.Metal = min(cargoCapacity, planetResources.Metal)
		}
	}

	f.fleet, f.err = tx.EnsureFleet(f.origin.GetID(), f.ships, f.speed, f.destination, f.mission, payload, f.holdingTime, f.unionID)
	return f.err
}

func (f *FleetBuilder) SendNow() (ogame.Fleet, error) {
	var err error
	if f.tx != nil {
		err = f.sendNow(f.tx)
	} else {
		err = f.b.TxNamed("FleetBuilder SendNow", func(tx Prioritizable) error {
			return f.sendNow(tx)
		})
	}
	if err != nil {
		for _, clb := range f.errorCallbacks {
			clb(err)
		}
	} else {
		if f.recallIn > 0 {
			go func() {
				time.Sleep(time.Duration(f.recallIn) * time.Second)
				if err := f.b.CancelFleet(f.fleet.ID); err != nil {
					log.Printf("scheduled recall of fleet %d failed: %v", f.fleet.ID, err)
				}
			}()
		}

		for _, clb := range f.successCallbacks {
			clb(f.fleet)
		}
	}
	return f.fleet, f.err
}

func (f *FleetBuilder) OnError(clb func(error)) *FleetBuilder {
	f.errorCallbacks = append(f.errorCallbacks, clb)
	return f
}

func (f *FleetBuilder) OnSuccess(clb func(ogame.Fleet)) *FleetBuilder {
	f.successCallbacks = append(f.successCallbacks, clb)
	return f
}
