package ogame

import (
	"math"
	"time"
)

type BaseTechnology struct {
	BaseLevelable
}

func (b BaseTechnology) TechnologyConstructionTime(level, universeSpeed int64, acc TechAccelerators, lfBonuses LfBonuses, class CharacterClass, hasTechnocrat bool) time.Duration {
	price := b.GetPrice(level, lfBonuses)
	metalCost := float64(price.Metal)
	crystalCost := float64(price.Crystal)
	researchLabLvl := float64(acc.GetResearchLab())
	hours := (metalCost + crystalCost) / (1000 * (1 + researchLabLvl) * float64(universeSpeed))
	if hasTechnocrat {
		hours -= 0.25 * hours
	}
	if class.IsDiscoverer() {
		hours -= 0.25 * hours
	}
	secs := math.Max(1, hours*3600)
	dur := time.Duration(int64(math.Floor(secs))) * time.Second
	bonus := lfBonuses.CostTimeBonuses[b.ID].Duration
	return time.Duration(float64(dur) - float64(dur)*bonus)
}

func (b BaseTechnology) ConstructionTime(level, universeSpeed int64, facilities BuildAccelerators, lfBonuses LfBonuses, class CharacterClass, hasTechnocrat bool) time.Duration {
	return b.TechnologyConstructionTime(level, universeSpeed, facilities, lfBonuses, class, hasTechnocrat)
}

func (b BaseTechnology) GetLevel(_ IResourcesBuildings, _ IFacilities, researches IResearches) int64 {
	return researches.ByID(b.ID)
}
