package wrapper

import (
	"github.com/alaingilbert/ogame/pkg/utils"
	"net/http"
	"net/url"
	"sync/atomic"
	"time"

	"github.com/alaingilbert/ogame/pkg/ogame"
)

type Prioritize struct {
	bot          *OGame
	initiator    string
	name         string
	taskIsDoneCh chan struct{}
	isTx         int32
}

func (b *Prioritize) SetTaskDoneCh(ch chan struct{}) {
	b.taskIsDoneCh = ch
}

func (b *Prioritize) SetInitiator(initiator string) Prioritizable {
	b.initiator = initiator
	return b
}

func (b *Prioritize) Begin() Prioritizable {
	return b.BeginNamed("Tx")
}

func (b *Prioritize) BeginNamed(name string) Prioritizable {
	name = utils.Ternary(name == "", "Tx", name)
	return b.begin(name)
}

func (b *Prioritize) Done() {
	b.done()
}

func (b *Prioritize) begin(name string) *Prioritize {
	if atomic.AddInt32(&b.isTx, 1) == 1 {
		b.name = utils.Ternary(b.initiator == "", name, b.initiator+":"+name)
		b.bot.botLock(b.name)
	}
	return b
}

func (b *Prioritize) done() {
	if atomic.AddInt32(&b.isTx, -1) == 0 {
		defer close(b.taskIsDoneCh)
		b.bot.botUnlock(b.name)
	}
}

func (b *Prioritize) Tx(clb func(Prioritizable) error) error {
	tx := b.Begin()
	defer tx.Done()
	return clb(tx)
}

func (b *Prioritize) TxNamed(name string, clb func(Prioritizable) error) error {
	tx := b.BeginNamed(name)
	defer tx.Done()
	return clb(tx)
}

func (b *Prioritize) LoginWithBearerToken(token string) (bool, bool, error) {
	b.begin("LoginWithBearerToken")
	defer b.done()
	return b.bot.wrapLoginWithBearerToken(token)
}

func (b *Prioritize) LoginWithExistingCookies() (bool, bool, error) {
	b.begin("LoginWithExistingCookies")
	defer b.done()
	return b.bot.wrapLoginWithExistingCookies()
}

func (b *Prioritize) Login() error {
	b.begin("Login")
	defer b.done()
	return b.bot.wrapLogin()
}

func (b *Prioritize) Logout() error {
	b.begin("Logout")
	defer b.done()
	return b.bot.logout()
}

func (b *Prioritize) GetPageContent(vals url.Values) ([]byte, error) {
	b.begin("GetPageContent")
	defer b.done()
	return b.bot.getPageContent(vals)
}

func (b *Prioritize) PostPageContent(vals, payload url.Values) ([]byte, error) {
	b.begin("PostPageContent")
	defer b.done()
	return b.bot.postPageContent(vals, payload)
}

func (b *Prioritize) IsUnderAttack(opts ...Option) (bool, error) {
	b.begin("IsUnderAttack")
	defer b.done()
	return b.bot.isUnderAttack(opts...)
}

func (b *Prioritize) SetVacationMode() error {
	b.begin("SetVacationMode")
	defer b.done()
	return b.bot.setVacationMode()
}

func (b *Prioritize) SetPreferences(p ogame.Preferences) error {
	b.begin("SetPreferences")
	defer b.done()
	return b.bot.setPreferences(p)
}

func (b *Prioritize) SetPreferencesLang(lang string) error {
	b.begin("SetPreferencesLang")
	defer b.done()
	return b.bot.setPreferencesLang(lang)
}

func (b *Prioritize) GetPlanets() ([]Planet, error) {
	b.begin("GetPlanets")
	defer b.done()
	return b.bot.getPlanets()
}

func (b *Prioritize) GetPlanet(v IntoPlanet) (Planet, error) {
	b.begin("GetPlanet")
	defer b.done()
	return b.bot.getPlanet(v)
}

func (b *Prioritize) GetMoons() ([]Moon, error) {
	b.begin("GetMoons")
	defer b.done()
	return b.bot.getMoons()
}

func (b *Prioritize) GetMoon(v IntoMoon) (Moon, error) {
	b.begin("GetMoon")
	defer b.done()
	return b.bot.getMoon(v)
}

func (b *Prioritize) GetCelestials() ([]Celestial, error) {
	b.begin("GetCelestials")
	defer b.done()
	return b.bot.getCelestials()
}

func (b *Prioritize) RecruitOfficer(typ, days int64) error {
	b.begin("RecruitOfficer")
	defer b.done()
	return b.bot.recruitOfficer(typ, days)
}

func (b *Prioritize) Abandon(v IntoPlanet) error {
	b.begin("Abandon")
	defer b.done()
	return b.bot.abandon(v)
}

func (b *Prioritize) GetCelestial(v IntoCelestial) (Celestial, error) {
	b.begin("GetCelestial")
	defer b.done()
	return b.bot.getCelestial(v)
}

func (b *Prioritize) ServerTime() (time.Time, error) {
	b.begin("ServerTime")
	defer b.done()
	return b.bot.serverTime()
}

func (b *Prioritize) GetUserInfos() (ogame.UserInfos, error) {
	b.begin("GetUserInfos")
	defer b.done()
	return b.bot.getUserInfos()
}

func (b *Prioritize) SendMessage(playerID int64, message string) error {
	b.begin("SendMessage")
	defer b.done()
	return b.bot.sendMessage(playerID, message, true)
}

func (b *Prioritize) SendMessageAlliance(associationID int64, message string) error {
	b.begin("SendMessageAlliance")
	defer b.done()
	return b.bot.sendMessage(associationID, message, false)
}

func (b *Prioritize) GetFleets(opts ...Option) ([]ogame.Fleet, ogame.Slots, error) {
	b.begin("GetFleets")
	defer b.done()
	return b.bot.getFleets(opts...)
}

func (b *Prioritize) GetFleetsFromEventList() ([]ogame.Fleet, error) {
	b.begin("GetFleets")
	defer b.done()
	return b.bot.getFleetsFromEventList()
}

func (b *Prioritize) CancelFleet(fleetID ogame.FleetID) error {
	b.begin("CancelFleet")
	defer b.done()
	return b.bot.cancelFleet(fleetID)
}

func (b *Prioritize) GetAttacks(opts ...Option) ([]ogame.AttackEvent, error) {
	b.begin("GetAttacks")
	defer b.done()
	return b.bot.getAttacks(opts...)
}

func (b *Prioritize) GalaxyInfos(galaxy, system int64, options ...Option) (ogame.SystemInfos, error) {
	b.begin("GalaxyInfos")
	defer b.done()
	return b.bot.galaxyInfos(galaxy, system, options...)
}

func (b *Prioritize) GetResourceSettings(planetID ogame.PlanetID, options ...Option) (ogame.ResourceSettings, error) {
	b.begin("GetResourceSettings")
	defer b.done()
	return b.bot.getResourceSettings(planetID, options...)
}

func (b *Prioritize) SetResourceSettings(planetID ogame.PlanetID, settings ogame.ResourceSettings) error {
	b.begin("SetResourceSettings")
	defer b.done()
	return b.bot.setResourceSettings(planetID, settings)
}

func (b *Prioritize) GetResourcesBuildings(celestialID ogame.CelestialID, options ...Option) (ogame.ResourcesBuildings, error) {
	b.begin("GetResourcesBuildings")
	defer b.done()
	return b.bot.getResourcesBuildings(celestialID, options...)
}

func (b *Prioritize) GetDefense(celestialID ogame.CelestialID, options ...Option) (ogame.DefensesInfos, error) {
	b.begin("GetDefense")
	defer b.done()
	return b.bot.getDefense(celestialID, options...)
}

func (b *Prioritize) GetShips(celestialID ogame.CelestialID, options ...Option) (ogame.ShipsInfos, error) {
	b.begin("GetShips")
	defer b.done()
	return b.bot.getShips(celestialID, options...)
}

func (b *Prioritize) GetFacilities(celestialID ogame.CelestialID, options ...Option) (ogame.Facilities, error) {
	b.begin("GetFacilities")
	defer b.done()
	return b.bot.getFacilities(celestialID, options...)
}

func (b *Prioritize) GetProduction(celestialID ogame.CelestialID) ([]ogame.Quantifiable, int64, error) {
	b.begin("GetProduction")
	defer b.done()
	return b.bot.getProduction(celestialID)
}

func (b *Prioritize) GetCachedResearch() ogame.Researches {
	b.begin("GetCachedResearch")
	defer b.done()
	return b.bot.getCachedResearch()
}

func (b *Prioritize) GetLfBonuses() (ogame.LfBonuses, error) {
	b.begin("GetLfBonuses")
	defer b.done()
	return b.bot.getLfBonuses()
}

func (b *Prioritize) GetCachedLfBonuses() (ogame.LfBonuses, error) {
	b.begin("GetCachedLfBonuses")
	defer b.done()
	return b.bot.getCachedLfBonuses()
}

func (b *Prioritize) GetCachedAllianceClass() (ogame.AllianceClass, error) {
	b.begin("GetCachedAllianceClass")
	defer b.done()
	return b.bot.getCachedAllianceClass()
}

func (b *Prioritize) CheckTarget(ships ogame.ShipsInfos, coord ogame.Coordinate, options ...Option) (CheckTargetResponse, error) {
	b.begin("CheckTarget")
	defer b.done()
	return b.bot.checkTarget(ships, coord, options...)
}

func (b *Prioritize) GetResearch() (ogame.Researches, error) {
	b.begin("GetResearch")
	defer b.done()
	return b.bot.getResearch()
}

func (b *Prioritize) GetSlots() (ogame.Slots, error) {
	b.begin("GetSlots")
	defer b.done()
	return b.bot.getSlots()
}

func (b *Prioritize) GetFleetDispatch(celestialID ogame.CelestialID, options ...Option) (ogame.FleetDispatchInfos, error) {
	b.begin("GetFleetDispatch")
	defer b.done()
	return b.bot.getFleetDispatch(celestialID, options...)
}

func (b *Prioritize) Build(celestialID ogame.CelestialID, id ogame.ID, nbr int64) error {
	b.begin("Build")
	defer b.done()
	return b.bot.build(celestialID, id, nbr)
}

func (b *Prioritize) TechnologyDetails(celestialID ogame.CelestialID, id ogame.ID) (ogame.TechnologyDetails, error) {
	b.begin("TechnologyDetails")
	defer b.done()
	return b.bot.technologyDetails(celestialID, id)
}

func (b *Prioritize) TearDown(celestialID ogame.CelestialID, id ogame.ID) error {
	b.begin("TearDown")
	defer b.done()
	return b.bot.tearDown(celestialID, id)
}

func (b *Prioritize) BuildCancelable(celestialID ogame.CelestialID, id ogame.ID) error {
	b.begin("BuildCancelable")
	defer b.done()
	return b.bot.buildCancelable(celestialID, id)
}

func (b *Prioritize) BuildProduction(celestialID ogame.CelestialID, id ogame.ID, nbr int64) error {
	b.begin("BuildProduction")
	defer b.done()
	return b.bot.buildProduction(celestialID, id, nbr)
}

func (b *Prioritize) BuildBuilding(celestialID ogame.CelestialID, buildingID ogame.ID) error {
	b.begin("BuildBuilding")
	defer b.done()
	return b.bot.buildBuilding(celestialID, buildingID)
}

func (b *Prioritize) BuildDefense(celestialID ogame.CelestialID, defenseID ogame.ID, nbr int64) error {
	b.begin("BuildDefense")
	defer b.done()
	return b.bot.buildDefense(celestialID, defenseID, nbr)
}

func (b *Prioritize) BuildShips(celestialID ogame.CelestialID, shipID ogame.ID, nbr int64) error {
	b.begin("BuildShips")
	defer b.done()
	return b.bot.buildShips(celestialID, shipID, nbr)
}

func (b *Prioritize) ConstructionsBeingBuilt(celestialID ogame.CelestialID) (ogame.Constructions, error) {
	b.begin("ConstructionsBeingBuilt")
	defer b.done()
	return b.bot.constructionsBeingBuilt(celestialID)
}

func (b *Prioritize) CancelBuilding(celestialID ogame.CelestialID) error {
	b.begin("CancelBuilding")
	defer b.done()
	return b.bot.cancelBuilding(celestialID)
}

func (b *Prioritize) CancelLfBuilding(celestialID ogame.CelestialID) error {
	b.begin("CancelLfBuilding")
	defer b.done()
	return b.bot.cancelLfBuilding(celestialID)
}

func (b *Prioritize) CancelResearch(celestialID ogame.CelestialID) error {
	b.begin("CancelResearch")
	defer b.done()
	return b.bot.cancelResearch(celestialID)
}

func (b *Prioritize) BuildTechnology(celestialID ogame.CelestialID, technologyID ogame.ID) error {
	b.begin("BuildTechnology")
	defer b.done()
	return b.bot.buildTechnology(celestialID, technologyID)
}

func (b *Prioritize) GetResources(celestialID ogame.CelestialID) (ogame.Resources, error) {
	b.begin("GetResources")
	defer b.done()
	return b.bot.getResources(celestialID)
}

func (b *Prioritize) GetResourcesDetails(celestialID ogame.CelestialID) (ogame.ResourcesDetails, error) {
	b.begin("GetResourcesDetails")
	defer b.done()
	return b.bot.getResourcesDetails(celestialID)
}

func (b *Prioritize) GetTechs(celestialID ogame.CelestialID) (ogame.Techs, error) {
	b.begin("GetTechs")
	defer b.done()
	return b.bot.getTechs(celestialID)
}

func (b *Prioritize) SendFleet(celestialID ogame.CelestialID, ships ogame.ShipsInfos, speed ogame.Speed, where ogame.Coordinate,
	mission ogame.MissionID, resources ogame.Resources, holdingTime, unionID int64) (ogame.Fleet, error) {
	b.begin("SendFleet")
	defer b.done()
	return b.bot.sendFleet(celestialID, ships, speed, where, mission, resources, holdingTime, unionID, false)
}

func (b *Prioritize) FastMiniFleetSpy(coordinate ogame.Coordinate, nbShips int64, options ...Option) (ogame.MinifleetResponse, error) {
	b.begin("FastMiniFleetSpy")
	defer b.done()
	return b.bot.fastMiniFleetSpy(coordinate, nbShips, options...)
}

func (b *Prioritize) MiniFleetSpy(coordinate ogame.Coordinate, nbShips int64, options ...Option) (ogame.Fleet, error) {
	b.begin("MiniFleetSpy")
	defer b.done()
	return b.bot.miniFleetSpy(coordinate, nbShips, options...)
}

func (b *Prioritize) SendDiscoveryFleet(celestialID ogame.CelestialID, coord ogame.Coordinate, options ...Option) error {
	b.begin("SendDiscoveryFleet")
	defer b.done()
	return b.bot.sendDiscoveryFleet(celestialID, coord, options...)
}

func (b *Prioritize) SendDiscoveryFleet2(celestialID ogame.CelestialID, coord ogame.Coordinate, options ...Option) (ogame.Fleet, error) {
	b.begin("SendDiscoveryFleet2")
	defer b.done()
	return b.bot.sendDiscoveryFleet2(celestialID, coord, options...)
}

func (b *Prioritize) SendSystemDiscoveryFleet(celestialID ogame.CelestialID, galaxy, system int64, options ...Option) ([]ogame.Coordinate, error) {
	b.begin("SendSystemDiscoveryFleet")
	defer b.done()
	return b.bot.sendSystemDiscoveryFleet(celestialID, galaxy, system, options...)
}

func (b *Prioritize) EnsureFleet(celestialID ogame.CelestialID, ships ogame.ShipsInfos, speed ogame.Speed, where ogame.Coordinate,
	mission ogame.MissionID, resources ogame.Resources, holdingTime, unionID int64) (ogame.Fleet, error) {
	b.begin("EnsureFleet")
	defer b.done()
	return b.bot.sendFleet(celestialID, ships, speed, where, mission, resources, holdingTime, unionID, true)
}

func (b *Prioritize) DestroyRockets(planetID ogame.PlanetID, abm, ipm int64) error {
	b.begin("DestroyRockets")
	defer b.done()
	return b.bot.destroyRockets(planetID, abm, ipm)
}

func (b *Prioritize) SendIPM(planetID ogame.PlanetID, coord ogame.Coordinate, nbr int64, priority ogame.ID) (int64, error) {
	b.begin("SendIPM")
	defer b.done()
	return b.bot.sendIPM(planetID, coord, nbr, priority)
}

func (b *Prioritize) GetCombatReportSummaryForFleet(fleetID ogame.FleetID) (ogame.CombatReportSummary, error) {
	b.begin("GetCombatReportSummaryForFleet")
	defer b.done()
	return b.bot.getCombatReportForFleet(fleetID)
}

func (b *Prioritize) GetCombatReportSummaryFor(coord ogame.Coordinate) (ogame.CombatReportSummary, error) {
	b.begin("GetCombatReportSummaryFor")
	defer b.done()
	return b.bot.getCombatReportFor(coord)
}

func (b *Prioritize) GetEspionageReportFor(coord ogame.Coordinate) (ogame.EspionageReport, error) {
	b.begin("GetEspionageReportFor")
	defer b.done()
	return b.bot.getEspionageReportFor(coord)
}

func (b *Prioritize) GetEspionageReportMessages(maxPage int64) ([]ogame.EspionageReportSummary, error) {
	b.begin("GetEspionageReportMessages")
	defer b.done()
	return b.bot.getEspionageReportMessages(maxPage)
}

func (b *Prioritize) CollectAllMarketplaceMessages() error {
	b.begin("CollectAllMarketplaceMessages")
	defer b.done()
	return b.bot.collectAllMarketplaceMessages()
}

func (b *Prioritize) CollectMarketplaceMessage(msg ogame.MarketplaceMessage) error {
	b.begin("CollectMarketplaceMessage")
	defer b.done()
	_, err := b.bot.collectMarketplaceMessage(msg, "")
	return err
}

func (b *Prioritize) GetExpeditionMessages(maxPage int64) ([]ogame.ExpeditionMessage, error) {
	b.begin("GetExpeditionMessages")
	defer b.done()
	return b.bot.getExpeditionMessages(maxPage)
}

func (b *Prioritize) GetExpeditionMessageAt(t time.Time) (ogame.ExpeditionMessage, error) {
	b.begin("GetExpeditionMessageAt")
	defer b.done()
	return b.bot.getExpeditionMessageAt(t)
}

func (b *Prioritize) GetEspionageReport(msgID int64) (ogame.EspionageReport, error) {
	b.begin("GetEspionageReport")
	defer b.done()
	return b.bot.getEspionageReport(msgID)
}

func (b *Prioritize) DeleteMessage(msgID int64) error {
	b.begin("DeleteMessage")
	defer b.done()
	return b.bot.deleteMessage(msgID)
}

func (b *Prioritize) DeleteAllMessagesFromTab(tabID ogame.MessagesTabID) error {
	b.begin("DeleteAllMessagesFromTab")
	defer b.done()
	return b.bot.deleteAllMessagesFromTab(tabID)
}

func (b *Prioritize) GetResourcesProductions(planetID ogame.PlanetID) (ogame.Resources, error) {
	b.begin("GetResourcesProductions")
	defer b.done()
	return b.bot.getResourcesProductions(planetID)
}

func (b *Prioritize) GetResourcesProductionsLight(resBuildings ogame.ResourcesBuildings, researches ogame.Researches,
	resSettings ogame.ResourceSettings, temp ogame.Temperature) ogame.Resources {
	b.begin("GetResourcesProductionsLight")
	defer b.done()
	return getResourcesProductionsLight(resBuildings, researches, resSettings, temp, b.bot.cache.serverData.Speed)
}

func (b *Prioritize) FlightTime(origin, destination ogame.Coordinate, speed ogame.Speed, ships ogame.ShipsInfos, missionID ogame.MissionID, holdingTime int64) (secs, fuel int64) {
	b.begin("FlightTime")
	defer b.done()
	return b.flightTime(origin, destination, speed, ships, missionID, holdingTime, true)
}

func (b *Prioritize) FastFlightTime(origin, destination ogame.Coordinate, speed ogame.Speed, ships ogame.ShipsInfos, missionID ogame.MissionID, holdingTime int64) (secs, fuel int64) {
	b.begin("FastFlightTime")
	defer b.done()
	return b.flightTime(origin, destination, speed, ships, missionID, holdingTime, false)
}

func (b *Prioritize) flightTime(origin, destination ogame.Coordinate, speed ogame.Speed, ships ogame.ShipsInfos, missionID ogame.MissionID, holdingTime int64, checkTarget bool) (secs, fuel int64) {
	serverData := b.bot.cache.serverData
	researches := b.bot.getCachedResearch()
	lfBonuses, _ := b.bot.getCachedLfBonuses()
	allianceClass, _ := b.bot.getCachedAllianceClass()
	var systemsSkip int64
	if checkTarget {
		fleetIgnoreEmptySystems := serverData.FleetIgnoreEmptySystems
		fleetIgnoreInactiveSystems := serverData.FleetIgnoreInactiveSystems
		opts := make([]Option, 0)
		if originCelestial, err := b.bot.GetCachedCelestial(origin); err == nil {
			opts = append(opts, ChangePlanet(originCelestial.GetID()))
		}
		res, _ := b.bot.checkTarget(ships, destination, opts...)
		systemsSkip += utils.TernaryOrZero(fleetIgnoreEmptySystems, res.EmptySystems)
		systemsSkip += utils.TernaryOrZero(fleetIgnoreInactiveSystems, res.InactiveSystems)
	}
	return CalcFlightTime(origin, destination, serverData.Galaxies, serverData.Systems,
		serverData.DonutGalaxy, serverData.DonutSystem, serverData.GlobalDeuteriumSaveFactor,
		float64(speed)/10, GetFleetSpeedForMission(serverData, missionID), ships, researches, lfBonuses, b.bot.cache.characterClass, allianceClass, systemsSkip, holdingTime)
}

func (b *Prioritize) Phalanx(moonID ogame.MoonID, coord ogame.Coordinate) ([]ogame.PhalanxFleet, error) {
	b.begin("Phalanx")
	defer b.done()
	return b.bot.getPhalanx(moonID, coord)
}

func (b *Prioritize) UnsafePhalanx(moonID ogame.MoonID, coord ogame.Coordinate) ([]ogame.PhalanxFleet, error) {
	b.begin("Phalanx")
	defer b.done()
	return b.bot.getUnsafePhalanx(moonID, coord)
}

func (b *Prioritize) JumpGate(origin, dest ogame.MoonID, ships ogame.ShipsInfos) (bool, int64, error) {
	b.begin("JumpGate")
	defer b.done()
	return b.bot.executeJumpGate(origin, dest, ships)
}

func (b *Prioritize) JumpGateDestinations(origin ogame.MoonID) ([]ogame.MoonID, int64, error) {
	b.begin("JumpGateDestinations")
	defer b.done()
	return b.bot.jumpGateDestinations(origin)
}

func (b *Prioritize) BuyOfferOfTheDay() error {
	b.begin("BuyOfferOfTheDay")
	defer b.done()
	return b.bot.buyOfferOfTheDay()
}

func (b *Prioritize) CreateUnion(fleet ogame.Fleet, users []string) (int64, error) {
	b.begin("CreateUnion")
	defer b.done()
	return b.bot.createUnion(fleet, users)
}

func (b *Prioritize) HeadersForPage(url string) (http.Header, error) {
	b.begin("HeadersForPage")
	defer b.done()
	return b.bot.headersForPage(url)
}

func (b *Prioritize) GetEmpire(celestialType ogame.CelestialType) ([]ogame.EmpireCelestial, error) {
	b.begin("GetEmpire")
	defer b.done()
	return b.bot.getEmpire(celestialType)
}

func (b *Prioritize) GetEmpireJSON(celestialType ogame.CelestialType) (any, error) {
	b.begin("GetEmpireJSON")
	defer b.done()
	return b.bot.getEmpireJSON(celestialType)
}

func (b *Prioritize) GetAuction() (ogame.Auction, error) {
	b.begin("GetAuction")
	defer b.done()
	return b.bot.getAuction(ogame.CelestialID(0))
}

func (b *Prioritize) DoAuction(bid map[ogame.CelestialID]ogame.Resources, token string, allCelestialIDs []string) error {
	b.begin("DoAuction")
	defer b.done()
	return b.bot.doAuction(ogame.CelestialID(0), bid, token, allCelestialIDs)
}

func (b *Prioritize) Highscore(category, typ, page int64) (ogame.Highscore, error) {
	b.begin("Highscore")
	defer b.done()
	return b.bot.highscore(category, typ, page)
}

func (b *Prioritize) GetAllResources() (map[ogame.CelestialID]ogame.Resources, error) {
	b.begin("GetAllResources")
	defer b.done()
	return b.bot.getAllResources()
}

func (b *Prioritize) GetDMCosts(celestialID ogame.CelestialID) (ogame.DMCosts, error) {
	b.begin("GetDMCosts")
	defer b.done()
	return b.bot.getDMCosts(celestialID)
}

func (b *Prioritize) UseDM(typ ogame.DMType, celestialID ogame.CelestialID) error {
	b.begin("UseDM")
	defer b.done()
	return b.bot.useDM(typ, celestialID)
}

func (b *Prioritize) GetItems(celestialID ogame.CelestialID) ([]ogame.Item, error) {
	b.begin("GetItems")
	defer b.done()
	return b.bot.getItems(celestialID)
}

func (b *Prioritize) GetActiveItems(celestialID ogame.CelestialID) ([]ogame.ActiveItem, error) {
	b.begin("GetActiveItems")
	defer b.done()
	return b.bot.getActiveItems(celestialID)
}

func (b *Prioritize) ActivateItem(ref string, celestialID ogame.CelestialID) error {
	b.begin("ActivateItem")
	defer b.done()
	return b.bot.activateItem(ref, celestialID)
}

func (b *Prioritize) BuyMarketplace(itemID int64, celestialID ogame.CelestialID) error {
	b.begin("BuyMarketplace")
	defer b.done()
	return b.bot.buyMarketplace(itemID, celestialID)
}

func (b *Prioritize) OfferSellMarketplace(itemID any, quantity, priceType, price, priceRange int64, celestialID ogame.CelestialID) error {
	b.begin("OfferSellMarketplace")
	defer b.done()
	return b.bot.offerMarketplace(4, itemID, quantity, priceType, price, priceRange, celestialID)
}

func (b *Prioritize) OfferBuyMarketplace(itemID any, quantity, priceType, price, priceRange int64, celestialID ogame.CelestialID) error {
	b.begin("OfferBuyMarketplace")
	defer b.done()
	return b.bot.offerMarketplace(3, itemID, quantity, priceType, price, priceRange, celestialID)
}

func (b *Prioritize) GetLfBuildings(celestialID ogame.CelestialID, options ...Option) (ogame.LfBuildings, error) {
	b.begin("GetLfBuildings")
	defer b.done()
	return b.bot.getLfBuildings(celestialID, options...)
}

func (b *Prioritize) GetLfResearch(celestialID ogame.CelestialID, options ...Option) (ogame.LfResearches, error) {
	b.begin("GetLfResearch")
	defer b.done()
	return b.bot.getLfResearch(celestialID, options...)
}

func (b *Prioritize) GetLfResearchDetails(celestialID ogame.CelestialID, options ...Option) (ogame.LfResearchDetails, error) {
	b.begin("GetLfResearch")
	defer b.done()
	return b.bot.getLfResearchDetails(celestialID, options...)
}

func (b *Prioritize) GetAvailableDiscoveries(opts ...Option) (int64, error) {
	b.begin("GetAvailableDiscoveries")
	defer b.done()
	return b.bot.getAvailableDiscoveries(opts...)
}

func (b *Prioritize) GetPositionsAvailableForDiscoveryFleet(galaxy int64, system int64, opts ...Option) ([]ogame.Coordinate, error) {
	b.begin("GetPositionsAvailableForDiscoveryFleet")
	defer b.done()
	return b.bot.getPositionsAvailableForDiscoveryFleet(galaxy, system, opts...)
}

func (b *Prioritize) GetChapter(chapterID int64) (ogame.Chapter, error) {
	b.begin("GetChapter")
	defer b.done()
	return b.bot.getChapter(chapterID)
}

func (b *Prioritize) ChapterClaimAll(chapterID int64) error {
	b.begin("GetChapter")
	defer b.done()
	return b.bot.chapterClaimAll(chapterID)
}

func (b *Prioritize) ChapterCollectReward(taskID int64) error {
	b.begin("GetChapter")
	defer b.done()
	return b.bot.chapterCollectReward(taskID)
}

func (b *Prioritize) SelectLfResearchSelect(planetID ogame.PlanetID, slotNumber int64) error {
	b.begin("SelectLfResearchSelect")
	defer b.done()
	return b.bot.selectLfResearchSelect(planetID, slotNumber)
}

func (b *Prioritize) SelectLfResearchRandom(planetID ogame.PlanetID, slotNumber int64) error {
	b.begin("SelectLfResearchRandom")
	defer b.done()
	return b.bot.selectLfResearchRandom(planetID, slotNumber)
}

func (b *Prioritize) SelectLfResearchArtifacts(planetID ogame.PlanetID, slotNumber int64, techID ogame.ID) error {
	b.begin("SelectLfResearchArtifacts")
	defer b.done()
	return b.bot.selectLfResearchArtifacts(planetID, slotNumber, techID)
}

func (b *Prioritize) FreeResetTree(planetID ogame.PlanetID, tier int64) error {
	b.begin("FreeResetTree")
	defer b.done()
	return b.bot.freeResetTree(planetID, tier)
}

func (b *Prioritize) BuyResetTree(planetID ogame.PlanetID, tier int64) error {
	b.begin("BuyResetTree")
	defer b.done()
	return b.bot.buyResetTree(planetID, tier)
}
