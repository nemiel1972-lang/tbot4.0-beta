package ogame

import (
	"encoding/json"
)

type SystemInfos struct {
	galaxy           int64
	system           int64
	planets          [15]*PlanetInfos
	Relocations      [15]Relocation
	ExpeditionDebris struct {
		Metal             int64
		Crystal           int64
		Deuterium         int64
		PathfindersNeeded int64
	}
	Events struct {
		Darkmatter  int64
		HasAsteroid bool
	}
	OverlayToken string
}

func (s SystemInfos) Galaxy() int64 {
	return s.galaxy
}

func (s *SystemInfos) SetGalaxy(galaxy int64) {
	s.galaxy = galaxy
}

func (s SystemInfos) System() int64 {
	return s.system
}

func (s *SystemInfos) SetSystem(system int64) {
	s.system = system
}

func (s *SystemInfos) SetPlanet(i int, planet *PlanetInfos) {
	s.planets[i] = planet
}

func (s SystemInfos) Position(idx int64) *PlanetInfos {
	if idx < 1 || idx > 15 {
		return nil
	}
	return s.planets[idx-1]
}

func (s SystemInfos) Each(clb func(planetInfo *PlanetInfos)) {
	var i int64
	for i = 1; i <= 15; i++ {
		clb(s.Position(i))
	}
}

func (s SystemInfos) MarshalJSON() ([]byte, error) {
	var tmp struct {
		Galaxy           int64
		System           int64
		Planets          [15]*PlanetInfos
		ExpeditionDebris struct {
			Metal             int64
			Crystal           int64
			Deuterium         int64
			PathfindersNeeded int64
		}
	}
	tmp.Galaxy = s.galaxy
	tmp.System = s.system
	tmp.Planets = s.planets
	tmp.ExpeditionDebris.Metal = s.ExpeditionDebris.Metal
	tmp.ExpeditionDebris.Crystal = s.ExpeditionDebris.Crystal
	tmp.ExpeditionDebris.Deuterium = s.ExpeditionDebris.Deuterium
	tmp.ExpeditionDebris.PathfindersNeeded = s.ExpeditionDebris.PathfindersNeeded
	return json.Marshal(tmp)
}

type MoonInfos struct {
	ID       int64
	Name     string
	Diameter int64
	Activity int64
}

type AllianceInfos struct {
	ID     int64
	Name   string
	Tag    string
	Rank   int64
	Member int64
}

type PlanetInfos struct {
	ID              int64
	Activity        int64
	Name            string
	Img             string
	Coordinate      Coordinate
	Administrator   bool
	Destroyed       bool
	Inactive        bool
	Vacation        bool
	StrongPlayer    bool
	Newbie          bool
	HonorableTarget bool
	Banned          bool
	Debris          struct {
		Metal           int64
		Crystal         int64
		Deuterium       int64
		RecyclersNeeded int64
	}
	Moon   *MoonInfos
	Player struct {
		ID         int64
		Name       string
		Rank       int64
		IsBandit   bool
		IsStarlord bool
	}
	Alliance *AllianceInfos
}
