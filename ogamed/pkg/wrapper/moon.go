package wrapper

import (
	"errors"
	"github.com/alaingilbert/ogame/pkg/ogame"
)

type Moon struct {
	ogame.Moon
	ogame *OGame
}

func (m Moon) GetProduction() ([]ogame.Quantifiable, int64, error) {
	return m.ogame.GetProduction(m.ID.Celestial())
}

func (m Moon) ConstructionsBeingBuilt() (ogame.Constructions, error) {
	return m.ogame.ConstructionsBeingBuilt(ogame.CelestialID(m.ID))
}

func (m Moon) GetResourcesBuildings(options ...Option) (ogame.ResourcesBuildings, error) {
	return m.ogame.GetResourcesBuildings(m.ID.Celestial(), options...)
}

func (m Moon) GetDefense(options ...Option) (ogame.DefensesInfos, error) {
	return m.ogame.GetDefense(m.ID.Celestial(), options...)
}

func (m Moon) GetShips(options ...Option) (ogame.ShipsInfos, error) {
	return m.ogame.GetShips(m.ID.Celestial(), options...)
}

func (m Moon) Build(id ogame.ID, nbr int64) error {
	return m.ogame.Build(ogame.CelestialID(m.ID), id, nbr)
}

func (m Moon) TearDown(buildingID ogame.ID) error {
	return m.ogame.TearDown(ogame.CelestialID(m.ID), buildingID)
}

func (m Moon) BuildTechnology(technologyID ogame.ID) error {
	return errors.New("cannot build technology on a moon")
}

func (m Moon) BuildDefense(defenseID ogame.ID, nbr int64) error {
	return m.ogame.BuildDefense(ogame.CelestialID(m.ID), defenseID, nbr)
}

func (m Moon) BuildBuilding(buildingID ogame.ID) error {
	return m.ogame.BuildBuilding(ogame.CelestialID(m.ID), buildingID)
}

func (m Moon) CancelBuilding() error {
	return m.ogame.CancelBuilding(ogame.CelestialID(m.ID))
}

func (m Moon) CancelLfBuilding() error {
	return m.ogame.CancelLfBuilding(ogame.CelestialID(m.ID))
}

func (m Moon) CancelResearch() error {
	return m.ogame.CancelResearch(m.ID.Celestial())
}

func (m Moon) SendFleet(ships ogame.ShipsInfos, speed ogame.Speed, where ogame.Coordinate,
	mission ogame.MissionID, resources ogame.Resources, holdingTime, unionID int64) (ogame.Fleet, error) {
	return m.ogame.SendFleet(ogame.CelestialID(m.ID), ships, speed, where, mission, resources, holdingTime, unionID)
}

func (m Moon) EnsureFleet(ships ogame.ShipsInfos, speed ogame.Speed, where ogame.Coordinate,
	mission ogame.MissionID, resources ogame.Resources, holdingTime, unionID int64) (ogame.Fleet, error) {
	return m.ogame.EnsureFleet(ogame.CelestialID(m.ID), ships, speed, where, mission, resources, holdingTime, unionID)
}

func (m Moon) GetResources() (ogame.Resources, error) {
	return m.ogame.GetResources(ogame.CelestialID(m.ID))
}

func (m Moon) GetResourcesDetails() (ogame.ResourcesDetails, error) {
	return m.ogame.GetResourcesDetails(ogame.CelestialID(m.ID))
}

func (m Moon) GetFacilities(options ...Option) (ogame.Facilities, error) {
	return m.ogame.GetFacilities(m.ID.Celestial(), options...)
}

func (m Moon) GetItems() ([]ogame.Item, error) {
	return m.ogame.GetItems(m.ID.Celestial())
}

func (m Moon) ActivateItem(ref string) error {
	return m.ogame.ActivateItem(ref, m.ID.Celestial())
}


func (m Moon) Phalanx(coord ogame.Coordinate) ([]ogame.PhalanxFleet, error) {
	return m.ogame.Phalanx(m.ID, coord)
}


func (m Moon) GetLfBuildings(options ...Option) (ogame.LfBuildings, error) {
	return m.ogame.GetLfBuildings(m.ID.Celestial(), options...)
}

func (m Moon) GetLfResearch(options ...Option) (ogame.LfResearches, error) {
	return m.ogame.GetLfResearch(m.ID.Celestial(), options...)
}

func (m Moon) GetTechs() (ogame.Techs, error) {
	return m.ogame.GetTechs(m.ID.Celestial())
}
