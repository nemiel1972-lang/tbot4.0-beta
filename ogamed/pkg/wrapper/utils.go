package wrapper

import (
	"errors"
	"github.com/alaingilbert/ogame/pkg/ogame"
)

func GetFleetSpeedForMission(serverData ServerData, missionID ogame.MissionID) int64 {
	if missionID == ogame.Attack ||
		missionID == ogame.GroupedAttack ||
		missionID == ogame.Destroy ||
		missionID == ogame.MissileAttack ||
		missionID == ogame.RecycleDebrisField ||
		missionID == ogame.Spy {
		return serverData.SpeedFleetWar
	}
	return serverData.SpeedFleetPeaceful
}

type HasCoordinate interface {
	GetCoordinate() ogame.Coordinate
}

func ConvertIntoCoordinate(w Wrapper, v IntoCoordinate) (ogame.Coordinate, error) {
	switch vv := v.(type) {
	case string:
		return ogame.ParseCoord(vv)
	case ogame.Coordinate:
		return vv, nil
	case HasCoordinate:
		return vv.GetCoordinate(), nil
	case ogame.CelestialID, ogame.PlanetID, ogame.MoonID:
		c, err := w.GetCachedCelestial(vv)
		if err != nil {
			return ogame.Coordinate{}, err
		}
		return c.GetCoordinate(), nil
	default:
		return ogame.Coordinate{}, errors.New("invalid type")
	}
}

func ConvertIntoCelestial(w Wrapper, v IntoCelestial) (Celestial, error) {
	return w.GetCachedCelestial(v)
}

func ConvertIntoPlanet(w Wrapper, v IntoPlanet) (Planet, error) {
	if c, err := w.GetCachedCelestial(v); err == nil {
		if p, ok := c.(Planet); ok {
			return p, nil
		}
		return Planet{}, errors.New("not a planet")
	}
	return Planet{}, errors.New("planet not found")
}

func ConvertIntoMoon(w Wrapper, v IntoMoon) (Moon, error) {
	if c, err := w.GetCachedCelestial(v); err == nil {
		if m, ok := c.(Moon); ok {
			return m, nil
		}
		return Moon{}, errors.New("not a moon")
	}
	return Moon{}, errors.New("moon not found")
}
