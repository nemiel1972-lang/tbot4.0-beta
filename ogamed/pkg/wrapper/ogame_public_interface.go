package wrapper

import (
	"crypto/tls"
	"github.com/alaingilbert/ogame/pkg/device"
	"github.com/alaingilbert/ogame/pkg/extractor"
	"github.com/alaingilbert/ogame/pkg/gameforge"
	"github.com/alaingilbert/ogame/pkg/httpclient"
	"github.com/alaingilbert/ogame/pkg/ogame"
	"github.com/alaingilbert/ogame/pkg/taskRunner"
	"net/http"
	"net/url"
	"time"
)


func (b *OGame) Enable() {
	b.enable()
}

func (b *OGame) Disable() {
	b.disable()
}

func (b *OGame) IsEnabled() bool {
	return b.isEnabled()
}

func (b *OGame) IsLoggedIn() bool {
	return b.isLoggedIn()
}

func (b *OGame) IsConnected() bool {
	return b.isConnected()
}

func (b *OGame) GetDevice() *device.Device {
	return b.getDevice()
}

func (b *OGame) GetClient() *httpclient.Client {
	return b.getClient()
}

func (b *OGame) SetClient(client *httpclient.Client) {
	b.setClient(client)
}

func (b *OGame) GetPublicIP() (string, error) {
	return b.getPublicIP()
}

func (b *OGame) ValidateAccount(code string) error {
	return b.validateAccount(code)
}

func (b *OGame) OnStateChange(clb func(locked bool, actor string)) {
	b.onStateChange(clb)
}

func (b *OGame) GetState() (bool, string) {
	return b.getState()
}

func (b *OGame) IsLocked() bool {
	return b.isLocked()
}

func (b *OGame) GetSession() string {
	return b.getSession()
}

func (b *OGame) AddAccount(number int, lang string) (*gameforge.AddAccountResponse, error) {
	return b.addAccount(number, lang)
}

func (b *OGame) WithPriority(priority taskRunner.Priority) Prioritizable {
	return b.withPriority(priority)
}

func (b *OGame) Begin() Prioritizable {
	return b.WithPriority(taskRunner.Normal).Begin()
}

func (b *OGame) BeginNamed(name string) Prioritizable {
	return b.WithPriority(taskRunner.Normal).BeginNamed(name)
}

func (b *OGame) SetInitiator(initiator string) Prioritizable {
	return b.setInitiator(initiator)
}

func (b *OGame) Done() {
	b.done()
}

func (b *OGame) Tx(clb func(tx Prioritizable) error) error {
	return b.WithPriority(taskRunner.Normal).Tx(clb)
}

func (b *OGame) TxNamed(name string, clb func(tx Prioritizable) error) error {
	return b.WithPriority(taskRunner.Normal).TxNamed(name, clb)
}

func (b *OGame) GetServer() gameforge.Server {
	return b.getServer()
}

func (b *OGame) PlanetID() ogame.CelestialID {
	return b.planetID()
}

func (b *OGame) ServerURL() string {
	return b.serverURL()
}

func (b *OGame) GetLanguage() string {
	return b.getLanguage()
}

func (b *OGame) LoginWithBearerToken(token string) (bool, bool, error) {
	return b.WithPriority(taskRunner.Normal).LoginWithBearerToken(token)
}

func (b *OGame) LoginWithExistingCookies() (bool, bool, error) {
	return b.WithPriority(taskRunner.Normal).LoginWithExistingCookies()
}

func (b *OGame) Login() error {
	return b.WithPriority(taskRunner.Normal).Login()
}

func (b *OGame) Logout() error { return b.WithPriority(taskRunner.Normal).Logout() }

func (b *OGame) BytesDownloaded() int64 {
	return b.bytesDownloaded()
}

func (b *OGame) BytesUploaded() int64 {
	return b.bytesUploaded()
}

func (b *OGame) GetUniverseName() string {
	return b.getUniverseName()
}

func (b *OGame) GetUsername() string {
	return b.getUsername()
}

func (b *OGame) GetUniverseSpeed() int64 {
	return b.getUniverseSpeed()
}

func (b *OGame) GetUniverseSpeedFleet() int64 {
	return b.getUniverseSpeedFleet()
}

func (b *OGame) IsPioneers() bool {
	return b.isPioneers()
}

func (b *OGame) IsDonutGalaxy() bool {
	return b.isDonutGalaxy()
}

func (b *OGame) IsDonutSystem() bool {
	return b.isDonutSystem()
}

func (b *OGame) ConstructionTime(id ogame.ID, nbr int64, facilities ogame.Facilities) time.Duration {
	return b.constructionTime(id, nbr, facilities)
}

func (b *OGame) GetPageContent(vals url.Values) ([]byte, error) {
	return b.WithPriority(taskRunner.Normal).GetPageContent(vals)
}

func (b *OGame) PostPageContent(vals, payload url.Values) ([]byte, error) {
	return b.WithPriority(taskRunner.Normal).PostPageContent(vals, payload)
}

func (b *OGame) IsUnderAttack(opts ...Option) (bool, error) {
	return b.WithPriority(taskRunner.Normal).IsUnderAttack(opts...)
}

func (b *OGame) GetCachedPlayer() ogame.UserInfos {
	return b.cache.player
}

func (b *OGame) GetCachedPreferences() ogame.Preferences {
	return b.getCachedPreferences()
}

func (b *OGame) SetVacationMode() error {
	return b.WithPriority(taskRunner.Normal).SetVacationMode()
}

func (b *OGame) SetPreferences(p ogame.Preferences) error {
	return b.WithPriority(taskRunner.Normal).SetPreferences(p)
}

func (b *OGame) SetPreferencesLang(lang string) error {
	return b.WithPriority(taskRunner.Normal).SetPreferencesLang(lang)
}

func (b *OGame) IsVacationModeEnabled() bool {
	return b.isVacationModeEnabled()
}

func (b *OGame) GetPlanets() ([]Planet, error) {
	return b.WithPriority(taskRunner.Normal).GetPlanets()
}

func (b *OGame) GetCachedPlanet(v IntoPlanet) (Planet, error) {
	return b.getCachedPlanet(v)
}

func (b *OGame) GetCachedMoon(v IntoMoon) (Moon, error) {
	return b.getCachedMoon(v)
}

func (b *OGame) GetCachedPlanets() []Planet {
	return b.getCachedPlanets()
}

func (b *OGame) GetCachedMoons() []Moon {
	return b.getCachedMoons()
}

func (b *OGame) GetCachedCelestials() []Celestial {
	return b.getCachedCelestials()
}

func (b *OGame) GetCachedCelestial(v IntoCelestial) (Celestial, error) {
	return b.getCachedCelestial(v)
}

func (b *OGame) GetPlanet(v IntoPlanet) (Planet, error) {
	return b.WithPriority(taskRunner.Normal).GetPlanet(v)
}

func (b *OGame) GetMoons() ([]Moon, error) {
	return b.WithPriority(taskRunner.Normal).GetMoons()
}

func (b *OGame) GetMoon(v IntoMoon) (Moon, error) {
	return b.WithPriority(taskRunner.Normal).GetMoon(v)
}

func (b *OGame) GetCelestials() ([]Celestial, error) {
	return b.WithPriority(taskRunner.Normal).GetCelestials()
}

func (b *OGame) RecruitOfficer(typ, days int64) error {
	return b.WithPriority(taskRunner.Normal).RecruitOfficer(typ, days)
}

func (b *OGame) Abandon(v IntoPlanet) error {
	return b.WithPriority(taskRunner.Normal).Abandon(v)
}

func (b *OGame) GetCelestial(v IntoCelestial) (Celestial, error) {
	return b.WithPriority(taskRunner.Normal).GetCelestial(v)
}

func (b *OGame) ServerTime() (time.Time, error) {
	return b.WithPriority(taskRunner.Normal).ServerTime()
}

func (b *OGame) Location() *time.Location {
	return b.location()
}

func (b *OGame) GetCachedToken() string {
	return b.getCachedToken()
}

func (b *OGame) GetUserInfos() (ogame.UserInfos, error) {
	return b.WithPriority(taskRunner.Normal).GetUserInfos()
}

func (b *OGame) SelectLfResearchSelect(planetID ogame.PlanetID, slotNumber int64) error {
	return b.WithPriority(taskRunner.Normal).SelectLfResearchSelect(planetID, slotNumber)
}

func (b *OGame) SelectLfResearchRandom(planetID ogame.PlanetID, slotNumber int64) error {
	return b.WithPriority(taskRunner.Normal).SelectLfResearchRandom(planetID, slotNumber)
}

func (b *OGame) SelectLfResearchArtifacts(planetID ogame.PlanetID, slotNumber int64, techID ogame.ID) error {
	return b.WithPriority(taskRunner.Normal).SelectLfResearchArtifacts(planetID, slotNumber, techID)
}

func (b *OGame) FreeResetTree(planetID ogame.PlanetID, tier int64) error {
	return b.WithPriority(taskRunner.Normal).FreeResetTree(planetID, tier)
}

func (b *OGame) BuyResetTree(planetID ogame.PlanetID, tier int64) error {
	return b.WithPriority(taskRunner.Normal).BuyResetTree(planetID, tier)
}

func (b *OGame) SendMessage(playerID int64, message string) error {
	return b.WithPriority(taskRunner.Normal).SendMessage(playerID, message)
}

func (b *OGame) SendMessageAlliance(associationID int64, message string) error {
	return b.WithPriority(taskRunner.Normal).SendMessageAlliance(associationID, message)
}

func (b *OGame) GetFleets(opts ...Option) ([]ogame.Fleet, ogame.Slots, error) {
	return b.WithPriority(taskRunner.Normal).GetFleets(opts...)
}

func (b *OGame) GetFleetsFromEventList() ([]ogame.Fleet, error) {
	return b.WithPriority(taskRunner.Normal).GetFleetsFromEventList()
}

func (b *OGame) CancelFleet(fleetID ogame.FleetID) error {
	return b.WithPriority(taskRunner.Normal).CancelFleet(fleetID)
}

func (b *OGame) GetAttacks(opts ...Option) ([]ogame.AttackEvent, error) {
	return b.WithPriority(taskRunner.Normal).GetAttacks(opts...)
}

func (b *OGame) GalaxyInfos(galaxy, system int64, options ...Option) (ogame.SystemInfos, error) {
	return b.WithPriority(taskRunner.Normal).GalaxyInfos(galaxy, system, options...)
}

func (b *OGame) GetResourceSettings(planetID ogame.PlanetID, options ...Option) (ogame.ResourceSettings, error) {
	return b.WithPriority(taskRunner.Normal).GetResourceSettings(planetID, options...)
}

func (b *OGame) SetResourceSettings(planetID ogame.PlanetID, settings ogame.ResourceSettings) error {
	return b.WithPriority(taskRunner.Normal).SetResourceSettings(planetID, settings)
}

func (b *OGame) GetResourcesBuildings(celestialID ogame.CelestialID, options ...Option) (ogame.ResourcesBuildings, error) {
	return b.WithPriority(taskRunner.Normal).GetResourcesBuildings(celestialID, options...)
}

func (b *OGame) GetDefense(celestialID ogame.CelestialID, options ...Option) (ogame.DefensesInfos, error) {
	return b.WithPriority(taskRunner.Normal).GetDefense(celestialID, options...)
}

func (b *OGame) GetShips(celestialID ogame.CelestialID, options ...Option) (ogame.ShipsInfos, error) {
	return b.WithPriority(taskRunner.Normal).GetShips(celestialID, options...)
}

func (b *OGame) GetFacilities(celestialID ogame.CelestialID, options ...Option) (ogame.Facilities, error) {
	return b.WithPriority(taskRunner.Normal).GetFacilities(celestialID, options...)
}

func (b *OGame) GetProduction(celestialID ogame.CelestialID) ([]ogame.Quantifiable, int64, error) {
	return b.WithPriority(taskRunner.Normal).GetProduction(celestialID)
}

func (b *OGame) GetCachedResearch() ogame.Researches {
	return b.WithPriority(taskRunner.Normal).GetCachedResearch()
}

func (b *OGame) GetLfBonuses() (ogame.LfBonuses, error) {
	return b.WithPriority(taskRunner.Normal).GetLfBonuses()
}

func (b *OGame) GetCachedLfBonuses() (ogame.LfBonuses, error) {
	return b.WithPriority(taskRunner.Normal).GetCachedLfBonuses()
}

func (b *OGame) GetResearch() (ogame.Researches, error) {
	return b.WithPriority(taskRunner.Normal).GetResearch()
}

func (b *OGame) GetSlots() (ogame.Slots, error) {
	return b.WithPriority(taskRunner.Normal).GetSlots()
}

func (b *OGame) GetFleetDispatch(celestialID ogame.CelestialID, options ...Option) (ogame.FleetDispatchInfos, error) {
	return b.WithPriority(taskRunner.Normal).GetFleetDispatch(celestialID, options...)
}

func (b *OGame) Build(celestialID ogame.CelestialID, id ogame.ID, nbr int64) error {
	return b.WithPriority(taskRunner.Normal).Build(celestialID, id, nbr)
}

func (b *OGame) TechnologyDetails(celestialID ogame.CelestialID, id ogame.ID) (ogame.TechnologyDetails, error) {
	return b.WithPriority(taskRunner.Normal).TechnologyDetails(celestialID, id)
}

func (b *OGame) TearDown(celestialID ogame.CelestialID, id ogame.ID) error {
	return b.WithPriority(taskRunner.Normal).TearDown(celestialID, id)
}

func (b *OGame) BuildCancelable(celestialID ogame.CelestialID, id ogame.ID) error {
	return b.WithPriority(taskRunner.Normal).BuildCancelable(celestialID, id)
}

func (b *OGame) BuildProduction(celestialID ogame.CelestialID, id ogame.ID, nbr int64) error {
	return b.WithPriority(taskRunner.Normal).BuildProduction(celestialID, id, nbr)
}

func (b *OGame) BuildBuilding(celestialID ogame.CelestialID, buildingID ogame.ID) error {
	return b.WithPriority(taskRunner.Normal).BuildBuilding(celestialID, buildingID)
}

func (b *OGame) BuildDefense(celestialID ogame.CelestialID, defenseID ogame.ID, nbr int64) error {
	return b.WithPriority(taskRunner.Normal).BuildDefense(celestialID, defenseID, nbr)
}

func (b *OGame) BuildShips(celestialID ogame.CelestialID, shipID ogame.ID, nbr int64) error {
	return b.WithPriority(taskRunner.Normal).BuildShips(celestialID, shipID, nbr)
}

func (b *OGame) ConstructionsBeingBuilt(celestialID ogame.CelestialID) (ogame.Constructions, error) {
	return b.WithPriority(taskRunner.Normal).ConstructionsBeingBuilt(celestialID)
}

func (b *OGame) CancelBuilding(celestialID ogame.CelestialID) error {
	return b.WithPriority(taskRunner.Normal).CancelBuilding(celestialID)
}

func (b *OGame) CancelLfBuilding(celestialID ogame.CelestialID) error {
	return b.WithPriority(taskRunner.Normal).CancelLfBuilding(celestialID)
}

func (b *OGame) CancelResearch(celestialID ogame.CelestialID) error {
	return b.WithPriority(taskRunner.Normal).CancelResearch(celestialID)
}

func (b *OGame) BuildTechnology(celestialID ogame.CelestialID, technologyID ogame.ID) error {
	return b.WithPriority(taskRunner.Normal).BuildTechnology(celestialID, technologyID)
}

func (b *OGame) GetResources(celestialID ogame.CelestialID) (ogame.Resources, error) {
	return b.WithPriority(taskRunner.Normal).GetResources(celestialID)
}

func (b *OGame) GetResourcesDetails(celestialID ogame.CelestialID) (ogame.ResourcesDetails, error) {
	return b.WithPriority(taskRunner.Normal).GetResourcesDetails(celestialID)
}

func (b *OGame) GetTechs(celestialID ogame.CelestialID) (ogame.Techs, error) {
	return b.WithPriority(taskRunner.Normal).GetTechs(celestialID)
}

func (b *OGame) SendFleet(celestialID ogame.CelestialID, ships ogame.ShipsInfos, speed ogame.Speed, where ogame.Coordinate,
	mission ogame.MissionID, resources ogame.Resources, holdingTime, unionID int64) (ogame.Fleet, error) {
	return b.WithPriority(taskRunner.Normal).SendFleet(celestialID, ships, speed, where, mission, resources, holdingTime, unionID)
}

func (b *OGame) EnsureFleet(celestialID ogame.CelestialID, ships ogame.ShipsInfos, speed ogame.Speed, where ogame.Coordinate,
	mission ogame.MissionID, resources ogame.Resources, holdingTime, unionID int64) (ogame.Fleet, error) {
	return b.WithPriority(taskRunner.Normal).EnsureFleet(celestialID, ships, speed, where, mission, resources, holdingTime, unionID)
}

func (b *OGame) FastMiniFleetSpy(coordinate ogame.Coordinate, nbShips int64, options ...Option) (ogame.MinifleetResponse, error) {
	return b.WithPriority(taskRunner.Normal).FastMiniFleetSpy(coordinate, nbShips, options...)
}

func (b *OGame) MiniFleetSpy(coordinate ogame.Coordinate, nbShips int64, options ...Option) (ogame.Fleet, error) {
	return b.WithPriority(taskRunner.Normal).MiniFleetSpy(coordinate, nbShips, options...)
}

func (b *OGame) DestroyRockets(planetID ogame.PlanetID, abm, ipm int64) error {
	return b.WithPriority(taskRunner.Normal).DestroyRockets(planetID, abm, ipm)
}

func (b *OGame) SendIPM(planetID ogame.PlanetID, coord ogame.Coordinate, nbr int64, priority ogame.ID) (int64, error) {
	return b.WithPriority(taskRunner.Normal).SendIPM(planetID, coord, nbr, priority)
}

func (b *OGame) GetCombatReportSummaryForFleet(fleetID ogame.FleetID) (ogame.CombatReportSummary, error) {
	return b.WithPriority(taskRunner.Normal).GetCombatReportSummaryForFleet(fleetID)
}

func (b *OGame) GetCombatReportSummaryFor(coord ogame.Coordinate) (ogame.CombatReportSummary, error) {
	return b.WithPriority(taskRunner.Normal).GetCombatReportSummaryFor(coord)
}

func (b *OGame) GetEspionageReportFor(coord ogame.Coordinate) (ogame.EspionageReport, error) {
	return b.WithPriority(taskRunner.Normal).GetEspionageReportFor(coord)
}

func (b *OGame) GetExpeditionMessages(maxPage int64) ([]ogame.ExpeditionMessage, error) {
	return b.WithPriority(taskRunner.Normal).GetExpeditionMessages(maxPage)
}

func (b *OGame) GetOtherMessages() ([]ogame.OtherMessage, error) {
	return b.WithPriority(taskRunner.Normal).GetOtherMessages()
}

func (b *OGame) GetExpeditionMessageAt(t time.Time) (ogame.ExpeditionMessage, error) {
	return b.WithPriority(taskRunner.Normal).GetExpeditionMessageAt(t)
}

func (b *OGame) CollectAllMarketplaceMessages() error {
	return b.WithPriority(taskRunner.Normal).CollectAllMarketplaceMessages()
}

func (b *OGame) CollectMarketplaceMessage(msg ogame.MarketplaceMessage) error {
	return b.WithPriority(taskRunner.Normal).CollectMarketplaceMessage(msg)
}

func (b *OGame) GetEspionageReportMessages(maxPage int64) ([]ogame.EspionageReportSummary, error) {
	return b.WithPriority(taskRunner.Normal).GetEspionageReportMessages(maxPage)
}

func (b *OGame) GetEspionageReport(msgID int64) (ogame.EspionageReport, error) {
	return b.WithPriority(taskRunner.Normal).GetEspionageReport(msgID)
}

func (b *OGame) DeleteMessage(msgID int64) error {
	return b.WithPriority(taskRunner.Normal).DeleteMessage(msgID)
}

func (b *OGame) DeleteAllMessagesFromTab(tabID ogame.MessagesTabID) error {
	return b.WithPriority(taskRunner.Normal).DeleteAllMessagesFromTab(tabID)
}

func (b *OGame) GetResourcesProductions(planetID ogame.PlanetID) (ogame.Resources, error) {
	return b.WithPriority(taskRunner.Normal).GetResourcesProductions(planetID)
}

func (b *OGame) GetResourcesProductionsLight(resBuildings ogame.ResourcesBuildings, researches ogame.Researches,
	resSettings ogame.ResourceSettings, temp ogame.Temperature) ogame.Resources {
	return b.WithPriority(taskRunner.Normal).GetResourcesProductionsLight(resBuildings, researches, resSettings, temp)
}

func (b *OGame) FlightTime(origin, destination ogame.Coordinate, speed ogame.Speed, ships ogame.ShipsInfos, missionID ogame.MissionID, holdingTime int64) (secs, fuel int64) {
	return b.WithPriority(taskRunner.Normal).FlightTime(origin, destination, speed, ships, missionID, holdingTime)
}

func (b *OGame) FastFlightTime(origin, destination ogame.Coordinate, speed ogame.Speed, ships ogame.ShipsInfos, missionID ogame.MissionID, holdingTime int64) (secs, fuel int64) {
	return b.WithPriority(taskRunner.Normal).FastFlightTime(origin, destination, speed, ships, missionID, holdingTime)
}

func (b *OGame) GetServerData() ServerData {
	return b.getServerData()
}

func (b *OGame) GetResearchSpeed() int64 {
	return b.getResearchSpeed()
}

func (b *OGame) GetNbSystems() int64 {
	return b.getNbSystems()
}

func (b *OGame) FleetDeutSaveFactor() float64 {
	return b.fleetDeutSaveFactor()
}

func (b *OGame) ServerVersion() string {
	return b.serverVersion()
}

func (b *OGame) Distance(origin, destination ogame.Coordinate) int64 {
	return b.distance(origin, destination)
}

func (b *OGame) SystemDistance(system1, system2 int64) int64 {
	return b.systemDistance(system1, system2)
}

func (b *OGame) RegisterWSCallback(id string, fn func(msg []byte)) {
	b.Lock()
	defer b.Unlock()
	b.registerWSCallback(id, fn)
}

func (b *OGame) RemoveWSCallback(id string) {
	b.Lock()
	defer b.Unlock()
	b.removeWSCallback(id)
}

func (b *OGame) RegisterChatCallback(fn func(msg ogame.ChatMsg)) {
	b.registerChatCallback(fn)
}

func (b *OGame) RegisterAuctioneerCallback(fn func(packet any)) {
	b.registerAuctioneerCallback(fn)
}

func (b *OGame) RegisterHTMLInterceptor(fn func(method, url string, params, payload url.Values, pageHTML []byte)) {
	b.registerHTMLInterceptor(fn)
}

func (b *OGame) Phalanx(moonID ogame.MoonID, coord ogame.Coordinate) ([]ogame.PhalanxFleet, error) {
	return b.WithPriority(taskRunner.Normal).Phalanx(moonID, coord)
}

func (b *OGame) UnsafePhalanx(moonID ogame.MoonID, coord ogame.Coordinate) ([]ogame.PhalanxFleet, error) {
	return b.WithPriority(taskRunner.Normal).UnsafePhalanx(moonID, coord)
}

func (b *OGame) JumpGateDestinations(origin ogame.MoonID) (moonIDs []ogame.MoonID, rechargeCountdown int64, err error) {
	return b.WithPriority(taskRunner.Normal).JumpGateDestinations(origin)
}

func (b *OGame) JumpGate(origin, dest ogame.MoonID, ships ogame.ShipsInfos) (success bool, rechargeCountdown int64, err error) {
	return b.WithPriority(taskRunner.Normal).JumpGate(origin, dest, ships)
}

func (b *OGame) BuyOfferOfTheDay() error {
	return b.WithPriority(taskRunner.Normal).BuyOfferOfTheDay()
}

func (b *OGame) CreateUnion(fleet ogame.Fleet, users []string) (int64, error) {
	return b.WithPriority(taskRunner.Normal).CreateUnion(fleet, users)
}

func (b *OGame) HeadersForPage(url string) (http.Header, error) {
	return b.WithPriority(taskRunner.Normal).HeadersForPage(url)
}

func (b *OGame) GetEmpire(celestialType ogame.CelestialType) ([]ogame.EmpireCelestial, error) {
	return b.WithPriority(taskRunner.Normal).GetEmpire(celestialType)
}

func (b *OGame) GetEmpireJSON(celestialType ogame.CelestialType) (any, error) {
	return b.WithPriority(taskRunner.Normal).GetEmpireJSON(celestialType)
}

func (b *OGame) CharacterClass() ogame.CharacterClass {
	return b.characterClass()
}

func (b *OGame) GetCachedAllianceClass() (ogame.AllianceClass, error) {
	return b.WithPriority(taskRunner.Normal).GetCachedAllianceClass()
}

func (b *OGame) CheckTarget(ships ogame.ShipsInfos, coordinate ogame.Coordinate, options ...Option) (CheckTargetResponse, error) {
	return b.WithPriority(taskRunner.Normal).CheckTarget(ships, coordinate, options...)
}

func (b *OGame) CountColonies() (int64, int64) {
	return b.countColonies()
}

func (b *OGame) GetAuction() (ogame.Auction, error) {
	return b.WithPriority(taskRunner.Critical).GetAuction()
}

func (b *OGame) DoAuction(bid map[ogame.CelestialID]ogame.Resources, token string, allCelestialIDs []string) error {
	return b.WithPriority(taskRunner.Critical).DoAuction(bid, token, allCelestialIDs)
}

func (b *OGame) Highscore(category, typ, page int64) (ogame.Highscore, error) {
	return b.WithPriority(taskRunner.Normal).Highscore(category, typ, page)
}

func (b *OGame) GetAllResources() (map[ogame.CelestialID]ogame.Resources, error) {
	return b.WithPriority(taskRunner.Normal).GetAllResources()
}

func (b *OGame) GetTasks() taskRunner.TasksOverview {
	return b.getTasks()
}

func (b *OGame) GetDMCosts(celestialID ogame.CelestialID) (ogame.DMCosts, error) {
	return b.WithPriority(taskRunner.Normal).GetDMCosts(celestialID)
}

func (b *OGame) UseDM(typ ogame.DMType, celestialID ogame.CelestialID) error {
	return b.WithPriority(taskRunner.Normal).UseDM(typ, celestialID)
}

func (b *OGame) GetItems(celestialID ogame.CelestialID) ([]ogame.Item, error) {
	return b.WithPriority(taskRunner.Normal).GetItems(celestialID)
}

func (b *OGame) GetActiveItems(celestialID ogame.CelestialID) ([]ogame.ActiveItem, error) {
	return b.WithPriority(taskRunner.Normal).GetActiveItems(celestialID)
}

func (b *OGame) ActivateItem(ref string, celestialID ogame.CelestialID) error {
	return b.WithPriority(taskRunner.Normal).ActivateItem(ref, celestialID)
}

func (b *OGame) BuyMarketplace(itemID int64, celestialID ogame.CelestialID) error {
	return b.WithPriority(taskRunner.Normal).BuyMarketplace(itemID, celestialID)
}

func (b *OGame) OfferSellMarketplace(itemID any, quantity, priceType, price, priceRange int64, celestialID ogame.CelestialID) error {
	return b.WithPriority(taskRunner.Normal).OfferSellMarketplace(itemID, quantity, priceType, price, priceRange, celestialID)
}

func (b *OGame) OfferBuyMarketplace(itemID any, quantity, priceType, price, priceRange int64, celestialID ogame.CelestialID) error {
	return b.WithPriority(taskRunner.Normal).OfferBuyMarketplace(itemID, quantity, priceType, price, priceRange, celestialID)
}

func (b *OGame) GetLfBuildings(celestialID ogame.CelestialID, opts ...Option) (ogame.LfBuildings, error) {
	return b.WithPriority(taskRunner.Normal).GetLfBuildings(celestialID, opts...)
}

func (b *OGame) GetLfResearch(celestialID ogame.CelestialID, opts ...Option) (ogame.LfResearches, error) {
	return b.WithPriority(taskRunner.Normal).GetLfResearch(celestialID, opts...)
}

func (b *OGame) GetLfResearchDetails(celestialID ogame.CelestialID, opts ...Option) (ogame.LfResearchDetails, error) {
	return b.WithPriority(taskRunner.Normal).GetLfResearchDetails(celestialID, opts...)
}

func (b *OGame) SendDiscoveryFleet(celestialID ogame.CelestialID, coord ogame.Coordinate, options ...Option) error {
	return b.WithPriority(taskRunner.Normal).SendDiscoveryFleet(celestialID, coord, options...)
}

func (b *OGame) SendDiscoveryFleet2(celestialID ogame.CelestialID, coord ogame.Coordinate, options ...Option) (ogame.Fleet, error) {
	return b.WithPriority(taskRunner.Normal).SendDiscoveryFleet2(celestialID, coord, options...)
}

func (b *OGame) SendSystemDiscoveryFleet(celestialID ogame.CelestialID, galaxy, system int64, options ...Option) ([]ogame.Coordinate, error) {
	return b.WithPriority(taskRunner.Normal).SendSystemDiscoveryFleet(celestialID, galaxy, system, options...)
}

func (b *OGame) GetAvailableDiscoveries(opts ...Option) (int64, error) {
	return b.WithPriority(taskRunner.Normal).GetAvailableDiscoveries(opts...)
}

func (b *OGame) GetPositionsAvailableForDiscoveryFleet(galaxy int64, system int64, opts ...Option) ([]ogame.Coordinate, error) {
	return b.WithPriority(taskRunner.Normal).GetPositionsAvailableForDiscoveryFleet(galaxy, system, opts...)
}

func (b *OGame) GetChapter(chapterID int64) (ogame.Chapter, error) {
	return b.WithPriority(taskRunner.Normal).GetChapter(chapterID)
}

func (b *OGame) ChapterClaimAll(chapterID int64) ([]byte, error) {
	return b.WithPriority(taskRunner.Normal).ChapterClaimAll(chapterID)
}

func (b *OGame) ChapterCollectReward(taskID int64) ([]byte, error) {
	return b.WithPriority(taskRunner.Normal).ChapterCollectReward(taskID)
}

func (b *OGame) SetProxy(proxyAddress, username, password, proxyType string, loginOnly bool, config *tls.Config) error {
	return b.setProxy(proxyAddress, username, password, proxyType, loginOnly, config)
}

func (b *OGame) GetExtractor() extractor.Extractor {
	return b.getExtractor()
}

func (b *OGame) SetOGameCredentials(username, password, otpSecret, bearerToken string) {
	b.setOGameCredentials(username, password, otpSecret, bearerToken)
}

func (b *OGame) SetLoginWrapper(newWrapper func(LoginFn) error) {
	b.setLoginWrapper(newWrapper)
}

func (b *OGame) ReconnectChat() bool {
	return b.reconnectChat()
}

func (b *OGame) SetAllianceClass(allianceClass ogame.AllianceClass) {
	b.Lock()
	defer b.Unlock()
	b.setAllianceClass(allianceClass)
}

func (b *OGame) SetResearches(researches ogame.Researches) {
	b.Lock()
	defer b.Unlock()
	b.setResearches(researches)
}

func (b *OGame) SetLfBonuses(lfBonuses ogame.LfBonuses) {
	b.Lock()
	defer b.Unlock()
	b.setLfBonuses(lfBonuses)
}

func (b *OGame) SoftLogout() {
	b.softLogout()
}
