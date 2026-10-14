package ogame

import (
	"strconv"
)

type PlanetID CelestialID

func (p PlanetID) String() string {
	return strconv.FormatInt(int64(p), 10)
}

func (p PlanetID) Celestial() CelestialID {
	return CelestialID(p)
}
