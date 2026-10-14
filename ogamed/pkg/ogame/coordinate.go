package ogame

import (
	"fmt"
	"strings"
)

type Coordinate struct {
	Galaxy   int64
	System   int64
	Position int64
	Type     CelestialType
}

func NewCoordinate(galaxy, system, position int64, typ CelestialType) Coordinate {
	return Coordinate{Galaxy: galaxy, System: system, Position: position, Type: typ}
}

func NewPlanetCoordinate(galaxy, system, position int64) Coordinate {
	return Coordinate{Galaxy: galaxy, System: system, Position: position, Type: PlanetType}
}

func NewDebrisCoordinate(galaxy, system, position int64) Coordinate {
	return Coordinate{Galaxy: galaxy, System: system, Position: position, Type: DebrisType}
}

func NewMoonCoordinate(galaxy, system, position int64) Coordinate {
	return Coordinate{Galaxy: galaxy, System: system, Position: position, Type: MoonType}
}

func (c Coordinate) String() string {
	return fmt.Sprintf("[%c:%d:%d:%d]", strings.ToUpper(c.Type.String())[0], c.Galaxy, c.System, c.Position)
}

func (c Coordinate) IsZero() bool {
	return c.Galaxy == 0 && c.System == 0 && c.Position == 0 && c.Type == 0
}

func (c Coordinate) Equal(v Coordinate) bool {
	return c.Cmp(v) == 0
}

func (c Coordinate) Cmp(v Coordinate) int {
	switch {
	case c.Galaxy < v.Galaxy:
		return -1
	case c.Galaxy > v.Galaxy:
		return 1
	case c.System < v.System:
		return -1
	case c.System > v.System:
		return 1
	case c.Position < v.Position:
		return -1
	case c.Position > v.Position:
		return 1
	case c.Type < v.Type:
		return -1
	case c.Type > v.Type:
		return 1
	default:
		return 0
	}
}

func (c Coordinate) IsPlanet() bool {
	return c.Type == PlanetType
}

func (c Coordinate) IsMoon() bool {
	return c.Type == MoonType
}

func (c Coordinate) IsDebris() bool {
	return c.Type == DebrisType
}

func (c Coordinate) Planet() Coordinate {
	return Coordinate{Galaxy: c.Galaxy, System: c.System, Position: c.Position, Type: PlanetType}
}

func (c Coordinate) Moon() Coordinate {
	return Coordinate{Galaxy: c.Galaxy, System: c.System, Position: c.Position, Type: MoonType}
}

func (c Coordinate) Debris() Coordinate {
	return Coordinate{Galaxy: c.Galaxy, System: c.System, Position: c.Position, Type: DebrisType}
}
