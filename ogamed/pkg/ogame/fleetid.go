package ogame

import (
	"strconv"
)

type FleetID int64

func (f FleetID) String() string {
	return strconv.FormatInt(int64(f), 10)
}

func (f FleetID) IsSet() bool {
	return f > 0
}
