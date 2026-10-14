package ogame

import (
	"github.com/alaingilbert/ogame/pkg/utils"
)

type LazyFacilities func() Facilities

func (f LazyFacilities) GetRoboticsFactory() int64 { return f().RoboticsFactory }
func (f LazyFacilities) GetShipyard() int64        { return f().Shipyard }
func (f LazyFacilities) GetResearchLab() int64     { return f().ResearchLab }
func (f LazyFacilities) GetAllianceDepot() int64   { return f().AllianceDepot }
func (f LazyFacilities) GetMissileSilo() int64     { return f().MissileSilo }
func (f LazyFacilities) GetNaniteFactory() int64   { return f().NaniteFactory }
func (f LazyFacilities) GetTerraformer() int64     { return f().Terraformer }
func (f LazyFacilities) GetSpaceDock() int64       { return f().SpaceDock }
func (f LazyFacilities) GetLunarBase() int64       { return f().LunarBase }
func (f LazyFacilities) GetSensorPhalanx() int64   { return f().SensorPhalanx }
func (f LazyFacilities) GetJumpGate() int64        { return f().JumpGate }
func (f LazyFacilities) ByID(id ID) int64          { return f().ByID(id) }

type Facilities struct {
	RoboticsFactory int64
	Shipyard        int64
	ResearchLab     int64
	AllianceDepot   int64
	MissileSilo     int64
	NaniteFactory   int64
	Terraformer     int64
	SpaceDock       int64
	LunarBase       int64
	SensorPhalanx   int64
	JumpGate        int64
}

func (f Facilities) GetRoboticsFactory() int64 { return f.RoboticsFactory }
func (f Facilities) GetShipyard() int64        { return f.Shipyard }
func (f Facilities) GetResearchLab() int64     { return f.ResearchLab }
func (f Facilities) GetAllianceDepot() int64   { return f.AllianceDepot }
func (f Facilities) GetMissileSilo() int64     { return f.MissileSilo }
func (f Facilities) GetNaniteFactory() int64   { return f.NaniteFactory }
func (f Facilities) GetTerraformer() int64     { return f.Terraformer }
func (f Facilities) GetSpaceDock() int64       { return f.SpaceDock }
func (f Facilities) GetLunarBase() int64       { return f.LunarBase }
func (f Facilities) GetSensorPhalanx() int64   { return f.SensorPhalanx }
func (f Facilities) GetJumpGate() int64        { return f.JumpGate }

func (f Facilities) Lazy() LazyFacilities {
	return func() Facilities { return f }
}

func (f Facilities) ByID(id ID) int64 {
	switch id {
	case RoboticsFactoryID:
		return f.GetRoboticsFactory()
	case ShipyardID:
		return f.GetShipyard()
	case ResearchLabID:
		return f.GetResearchLab()
	case AllianceDepotID:
		return f.GetAllianceDepot()
	case MissileSiloID:
		return f.GetMissileSilo()
	case NaniteFactoryID:
		return f.GetNaniteFactory()
	case TerraformerID:
		return f.GetTerraformer()
	case SpaceDockID:
		return f.GetSpaceDock()
	case LunarBaseID:
		return f.GetLunarBase()
	case SensorPhalanxID:
		return f.GetSensorPhalanx()
	case JumpGateID:
		return f.GetJumpGate()
	}
	return 0
}

func (f Facilities) String() string {
	return "\n" +
		"RoboticsFactory: " + utils.FI64(f.RoboticsFactory) + "\n" +
		"       Shipyard: " + utils.FI64(f.Shipyard) + "\n" +
		"   Research Lab: " + utils.FI64(f.ResearchLab) + "\n" +
		" Alliance Depot: " + utils.FI64(f.AllianceDepot) + "\n" +
		"   Missile Silo: " + utils.FI64(f.MissileSilo) + "\n" +
		" Nanite Factory: " + utils.FI64(f.NaniteFactory) + "\n" +
		"    Terraformer: " + utils.FI64(f.Terraformer) + "\n" +
		"     Space Dock: " + utils.FI64(f.SpaceDock) + "\n" +
		"     Lunar Base: " + utils.FI64(f.LunarBase) + "\n" +
		" Sensor Phalanx: " + utils.FI64(f.SensorPhalanx) + "\n" +
		"      Jump Gate: " + utils.FI64(f.JumpGate)
}
