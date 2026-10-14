package ogame

import (
	"github.com/alaingilbert/ogame/pkg/utils"
)

type DefensesInfos struct {
	RocketLauncher         int64
	LightLaser             int64
	HeavyLaser             int64
	GaussCannon            int64
	IonCannon              int64
	PlasmaTurret           int64
	SmallShieldDome        int64
	LargeShieldDome        int64
	AntiBallisticMissiles  int64
	InterplanetaryMissiles int64
}

func (d DefensesInfos) HasShipDefense() bool {
	return d.CountShipDefenses() > 0
}

func (d DefensesInfos) HasMissilesDefense() bool {
	return d.AntiBallisticMissiles > 0
}

func (d DefensesInfos) CountShipDefenses() (out int64) {
	excluded := []ID{
		InterplanetaryMissilesID,
		AntiBallisticMissilesID,
		SmallShieldDomeID,
		LargeShieldDomeID,
	}
	for _, defense := range Defenses {
		if !utils.InArr(defense.GetID(), excluded) {
			out += d.ByID(defense.GetID())
		}
	}
	return
}

func (d DefensesInfos) AttackableValue() int64 {
	val := d.RocketLauncher * RocketLauncher.Price.Total()
	val += d.LightLaser * LightLaser.Price.Total()
	val += d.HeavyLaser * HeavyLaser.Price.Total()
	val += d.GaussCannon * GaussCannon.Price.Total()
	val += d.IonCannon * IonCannon.Price.Total()
	val += d.PlasmaTurret * PlasmaTurret.Price.Total()
	val += d.SmallShieldDome * SmallShieldDome.Price.Total()
	val += d.LargeShieldDome * LargeShieldDome.Price.Total()
	return val
}

func (d DefensesInfos) GetWeaponPower(researches IResearches) (out int64) {
	for _, defense := range Defenses {
		out += d.ByID(defense.GetID()) * defense.GetWeaponPower(researches)
	}
	return
}

func (d DefensesInfos) GetShieldPower(researches IResearches) (out int64) {
	for _, defense := range Defenses {
		out += d.ByID(defense.GetID()) * defense.GetShieldPower(researches)
	}
	return
}

func (d DefensesInfos) GetStructuralIntegrity(researches IResearches) (out int64) {
	for _, defense := range Defenses {
		out += d.ByID(defense.GetID()) * defense.GetStructuralIntegrity(researches)
	}
	return
}

func (d DefensesInfos) String() string {
	return "\n" +
		"        Rocket Launcher: " + utils.FI64(d.RocketLauncher) + "\n" +
		"            Light Laser: " + utils.FI64(d.LightLaser) + "\n" +
		"            Heavy Laser: " + utils.FI64(d.HeavyLaser) + "\n" +
		"           Gauss Cannon: " + utils.FI64(d.GaussCannon) + "\n" +
		"             Ion Cannon: " + utils.FI64(d.IonCannon) + "\n" +
		"          Plasma Turret: " + utils.FI64(d.PlasmaTurret) + "\n" +
		"      Small Shield Dome: " + utils.FI64(d.SmallShieldDome) + "\n" +
		"      Large Shield Dome: " + utils.FI64(d.LargeShieldDome) + "\n" +
		"Anti Ballistic Missiles: " + utils.FI64(d.AntiBallisticMissiles) + "\n" +
		"Interplanetary Missiles: " + utils.FI64(d.InterplanetaryMissiles)
}

func (d DefensesInfos) ByID(id ID) int64 {
	switch id {
	case RocketLauncherID:
		return d.RocketLauncher
	case LightLaserID:
		return d.LightLaser
	case HeavyLaserID:
		return d.HeavyLaser
	case GaussCannonID:
		return d.GaussCannon
	case IonCannonID:
		return d.IonCannon
	case PlasmaTurretID:
		return d.PlasmaTurret
	case SmallShieldDomeID:
		return d.SmallShieldDome
	case LargeShieldDomeID:
		return d.LargeShieldDome
	case AntiBallisticMissilesID:
		return d.AntiBallisticMissiles
	case InterplanetaryMissilesID:
		return d.InterplanetaryMissiles
	default:
		return 0
	}
}

func (d *DefensesInfos) Set(id ID, val int64) {
	switch id {
	case RocketLauncherID:
		d.RocketLauncher = val
	case LightLaserID:
		d.LightLaser = val
	case HeavyLaserID:
		d.HeavyLaser = val
	case GaussCannonID:
		d.GaussCannon = val
	case IonCannonID:
		d.IonCannon = val
	case PlasmaTurretID:
		d.PlasmaTurret = val
	case SmallShieldDomeID:
		d.SmallShieldDome = val
	case LargeShieldDomeID:
		d.LargeShieldDome = val
	case AntiBallisticMissilesID:
		d.AntiBallisticMissiles = val
	case InterplanetaryMissilesID:
		d.InterplanetaryMissiles = val
	}
}
