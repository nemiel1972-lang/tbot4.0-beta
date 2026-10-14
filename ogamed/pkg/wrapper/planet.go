package wrapper

import (
	"fmt"

	"github.com/alaingilbert/ogame/pkg/ogame"
)

type Planet struct {
	ogame.Planet
	ogame *OGame
	Moon  *Moon
}

func (p Planet) String() string {
	return fmt.Sprintf("%s %s", p.Name, p.Coordinate)
}

func (p Planet) GetMoon() *Moon { return p.Moon }

func (p Planet) GetProduction() ([]ogame.Quantifiable, int64, error) {
	return p.ogame.GetProduction(p.ID.Celestial())
}

func (p Planet) GetResourceSettings(options ...Option) (ogame.ResourceSettings, error) {
	return p.ogame.GetResourceSettings(p.ID, options...)
}

func (p Planet) GetResourcesBuildings(options ...Option) (ogame.ResourcesBuildings, error) {
	return p.ogame.GetResourcesBuildings(p.ID.Celestial(), options...)
}

func (p Planet) GetDefense(options ...Option) (ogame.DefensesInfos, error) {
	return p.ogame.GetDefense(p.ID.Celestial(), options...)
}

func (p Planet) GetShips(options ...Option) (ogame.ShipsInfos, error) {
	return p.ogame.GetShips(p.ID.Celestial(), options...)
}

func (p Planet) GetFacilities(options ...Option) (ogame.Facilities, error) {
	return p.ogame.GetFacilities(p.ID.Celestial(), options...)
}

func (p Planet) Build(id ogame.ID, nbr int64) error {
	return p.ogame.Build(ogame.CelestialID(p.ID), id, nbr)
}

func (p Planet) TearDown(buildingID ogame.ID) error {
	return p.ogame.TearDown(ogame.CelestialID(p.ID), buildingID)
}

func (p Planet) BuildCancelable(id ogame.ID) error {
	return p.ogame.BuildCancelable(ogame.CelestialID(p.ID), id)
}

func (p Planet) BuildBuilding(buildingID ogame.ID) error {
	return p.ogame.BuildBuilding(ogame.CelestialID(p.ID), buildingID)
}

func (p Planet) BuildDefense(defenseID ogame.ID, nbr int64) error {
	return p.ogame.BuildDefense(ogame.CelestialID(p.ID), defenseID, nbr)
}

func (p Planet) BuildShips(shipID ogame.ID, nbr int64) error {
	return p.ogame.BuildShips(ogame.CelestialID(p.ID), shipID, nbr)
}

func (p Planet) BuildTechnology(technologyID ogame.ID) error {
	return p.ogame.BuildTechnology(p.ID.Celestial(), technologyID)
}

func (p Planet) GetResources() (ogame.Resources, error) {
	return p.ogame.GetResources(ogame.CelestialID(p.ID))
}

func (p Planet) GetResourcesDetails() (ogame.ResourcesDetails, error) {
	return p.ogame.GetResourcesDetails(ogame.CelestialID(p.ID))
}

func (p Planet) SendFleet(ships ogame.ShipsInfos, speed ogame.Speed, where ogame.Coordinate,
	mission ogame.MissionID, resources ogame.Resources, holdingTime, unionID int64) (ogame.Fleet, error) {
	return p.ogame.SendFleet(ogame.CelestialID(p.ID), ships, speed, where, mission, resources, holdingTime, unionID)
}

func (p Planet) EnsureFleet(ships ogame.ShipsInfos, speed ogame.Speed, where ogame.Coordinate,
	mission ogame.MissionID, resources ogame.Resources, holdingTime, unionID int64) (ogame.Fleet, error) {
	return p.ogame.EnsureFleet(ogame.CelestialID(p.ID), ships, speed, where, mission, resources, holdingTime, unionID)
}

func (p Planet) ConstructionsBeingBuilt() (ogame.Constructions, error) {
	return p.ogame.ConstructionsBeingBuilt(ogame.CelestialID(p.ID))
}

func (p Planet) CancelBuilding() error {
	return p.ogame.CancelBuilding(ogame.CelestialID(p.ID))
}

func (p Planet) CancelLfBuilding() error {
	return p.ogame.CancelLfBuilding(ogame.CelestialID(p.ID))
}

func (p Planet) CancelResearch() error {
	return p.ogame.CancelResearch(p.ID.Celestial())
}

func (p Planet) GetItems() ([]ogame.Item, error) {
	return p.ogame.GetItems(p.ID.Celestial())
}

func (p Planet) ActivateItem(ref string) error {
	return p.ogame.ActivateItem(ref, p.ID.Celestial())
}

func (p Planet) GetResourcesProductions() (ogame.Resources, error) {
	return p.ogame.GetResourcesProductions(p.ID)
}

func (p Planet) FlightTime(destination ogame.Coordinate, speed ogame.Speed, ships ogame.ShipsInfos, missionID ogame.MissionID, holdingTime int64) (secs, fuel int64) {
	return p.ogame.FlightTime(p.Coordinate, destination, speed, ships, missionID, holdingTime)
}

func (p Planet) SendIPM(planetID ogame.PlanetID, coord ogame.Coordinate, nbr int64, priority ogame.ID) (int64, error) {
	return p.ogame.SendIPM(planetID, coord, nbr, priority)
}

func (p Planet) GetLfBuildings(options ...Option) (ogame.LfBuildings, error) {
	return p.ogame.getLfBuildings(p.ID.Celestial(), options...)
}

func (p Planet) GetLfResearch(options ...Option) (ogame.LfResearches, error) {
	return p.ogame.getLfResearch(p.ID.Celestial(), options...)
}

func (p Planet) GetTechs() (ogame.Techs, error) {
	return p.ogame.GetTechs(p.ID.Celestial())
}
