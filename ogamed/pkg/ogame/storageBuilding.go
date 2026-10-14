package ogame

import (
	"math"
)

type storageBuilding struct {
	BaseBuilding
}

func (s storageBuilding) Capacity(lvl int64) int64 {
	return 5000 * int64(2.5*math.Pow(math.E, (20*float64(lvl))/33))
}
