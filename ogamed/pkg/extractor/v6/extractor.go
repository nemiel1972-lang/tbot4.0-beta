package v6

import (
	"bytes"
	"errors"
	"net/url"
	"time"

	"github.com/alaingilbert/ogame/pkg/ogame"

	"github.com/PuerkitoBio/goquery"
	"github.com/alaingilbert/clockwork"
)

type Extractor struct {
	loc             *time.Location
	lang            string
	lifeformEnabled bool
}

func NewExtractor() *Extractor {
	return &Extractor{}
}

func (e *Extractor) SetLocation(loc *time.Location)          { e.loc = loc }
func (e *Extractor) SetLanguage(lang string)                 { e.lang = lang }
func (e *Extractor) SetLifeformEnabled(lifeformEnabled bool) { e.lifeformEnabled = lifeformEnabled }
func (e *Extractor) GetLifeformEnabled() bool                { return e.lifeformEnabled }
func (e *Extractor) GetLocation() *time.Location {
	if e.loc == nil {
		return time.UTC
	}
	return e.loc
}
func (e *Extractor) GetLanguage() string {
	if e.lang == "" {
		return "en"
	}
	return e.lang
}

func (e *Extractor) ExtractToken(pageHTML []byte) (string, error) {
	panic("implement me")
}

func (e *Extractor) ExtractLfBonuses(pageHTML []byte) (ogame.LfBonuses, error) {
	panic("implement me")
}

func (e *Extractor) ExtractLfBonusesFromDoc(doc *goquery.Document) (ogame.LfBonuses, error) {
	panic("implement me")
}

func (e *Extractor) ExtractTechnologyDetails(pageHTML []byte) (out ogame.TechnologyDetails, err error) {
	panic("implement me")
}

func (e *Extractor) ExtractTechnologyDetailsFromDoc(doc *goquery.Document) (ogame.TechnologyDetails, error) {
	panic("implement me")
}

func (e *Extractor) ExtractCancelLfBuildingInfos(pageHTML []byte) (token string, id, listID int64, err error) {
	panic("implement me")
}

func (e *Extractor) ExtractActiveItems(pageHTML []byte) ([]ogame.ActiveItem, error) {
	panic("implement me")
}

func (e *Extractor) ExtractPremiumToken(pageHTML []byte, days int64) (string, error) {
	panic("implement me")
}

func (e *Extractor) ExtractTechs(pageHTML []byte) (ogame.Techs, error) {
	panic("implement me")
}

func (e *Extractor) ExtractDestroyRockets(pageHTML []byte) (abm, ipm int64, token string, err error) {
	panic("implement me")
}

func (e *Extractor) ExtractCancelFleetToken(pageHTML []byte, fleetID ogame.FleetID) (string, error) {
	panic("implement me")
}

func (e *Extractor) ExtractMarketplaceMessages(pageHTML []byte) ([]ogame.MarketplaceMessage, int64, error) {
	panic("implement me")
}

func (e *Extractor) ExtractExpeditionMessages(pageHTML []byte) ([]ogame.ExpeditionMessage, int64, error) {
	panic("implement me")
}

func (e *Extractor) ExtractExpeditionMessagesFromDoc(doc *goquery.Document) ([]ogame.ExpeditionMessage, int64, error) {
	panic("implement me")
}

func (e *Extractor) ExtractTearDownButtonEnabled(pageHTML []byte) (bool, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return false, err
	}
	return e.ExtractTearDownButtonEnabledFromDoc(doc), nil
}

func (e *Extractor) ExtractUpgradeToken(pageHTML []byte) (string, error) {
	return extractUpgradeToken(pageHTML)
}

func (e *Extractor) ExtractLifeformEnabled(pageHTML []byte) bool {
	return bytes.Contains(pageHTML, []byte(`lifeformEnabled":true`))
}

func (e *Extractor) ExtractIsInVacation(pageHTML []byte) (bool, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return false, err
	}
	return e.ExtractIsInVacationFromDoc(doc), nil
}

func (e *Extractor) ExtractAttackBlock(pageHTML []byte) (bool, time.Time, error) {
	panic("implement me")
}

func (e *Extractor) ExtractAttackBlockFromDoc(doc *goquery.Document) (bool, time.Time) {
	panic("implement me")
}

func (e *Extractor) ExtractPlanets(pageHTML []byte) ([]ogame.Planet, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return nil, err
	}
	return e.ExtractPlanetsFromDoc(doc), nil
}

func (e *Extractor) ExtractPlanet(pageHTML []byte, v any) (ogame.Planet, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return ogame.Planet{}, err
	}
	return e.ExtractPlanetFromDoc(doc, v)
}

func (e *Extractor) ExtractMoons(pageHTML []byte) ([]ogame.Moon, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return nil, err
	}
	return e.ExtractMoonsFromDoc(doc), nil
}

func (e *Extractor) ExtractMoon(pageHTML []byte, v any) (ogame.Moon, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return ogame.Moon{}, err
	}
	return e.ExtractMoonFromDoc(doc, v)
}

func (e *Extractor) ExtractCelestials(pageHTML []byte) ([]ogame.Celestial, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return nil, err
	}
	return e.ExtractCelestialsFromDoc(doc)
}

func (e *Extractor) ExtractCelestial(pageHTML []byte, v any) (ogame.Celestial, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return nil, err
	}
	return e.ExtractCelestialFromDoc(doc, v)
}

func (e *Extractor) ExtractServerTime(pageHTML []byte) (time.Time, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return time.Time{}, err
	}
	return e.ExtractServerTimeFromDoc(doc)
}

func (e *Extractor) ExtractFleetsFromEventList(pageHTML []byte) ([]ogame.Fleet, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return nil, err
	}
	return e.ExtractFleetsFromEventListFromDoc(doc), nil
}

func (e *Extractor) ExtractIPM(pageHTML []byte) (duration, max int64, token string, err error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return
	}
	return e.ExtractIPMFromDoc(doc)
}

func (e *Extractor) ExtractFleets(pageHTML []byte) ([]ogame.Fleet, error) {
	return e.extractFleets(pageHTML, e.loc)
}

func (e *Extractor) extractFleets(pageHTML []byte, location *time.Location) ([]ogame.Fleet, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return nil, err
	}
	return e.extractFleetsFromDoc(doc, location)
}

func (e *Extractor) ExtractSlots(pageHTML []byte) (ogame.Slots, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return ogame.Slots{}, err
	}
	return e.ExtractSlotsFromDoc(doc)
}

func (e *Extractor) ExtractOgameTimestamp(pageHTML []byte) (int64, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return 0, err
	}
	return e.ExtractOgameTimestampFromDoc(doc), nil
}

func (e *Extractor) ExtractResources(pageHTML []byte) (ogame.Resources, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return ogame.Resources{}, err
	}
	return e.ExtractResourcesFromDoc(doc), nil
}

func (e *Extractor) ExtractResourcesDetailsFromFullPage(pageHTML []byte) (ogame.ResourcesDetails, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return ogame.ResourcesDetails{}, err
	}
	return e.ExtractResourcesDetailsFromFullPageFromDoc(doc), nil
}

func (e *Extractor) ExtractResourceSettings(pageHTML []byte) (ogame.ResourceSettings, string, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return ogame.ResourceSettings{}, "", err
	}
	return e.ExtractResourceSettingsFromDoc(doc)
}

func (e *Extractor) ExtractAttacks(pageHTML []byte, ownCoords []ogame.Coordinate) ([]ogame.AttackEvent, error) {
	return e.extractAttacks(pageHTML, clockwork.NewRealClock(), ownCoords)
}

func (e *Extractor) extractAttacks(pageHTML []byte, clock clockwork.Clock, ownCoords []ogame.Coordinate) ([]ogame.AttackEvent, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return nil, err
	}
	return e.extractAttacksFromDoc(doc, clock, ownCoords)
}

func (e *Extractor) ExtractOfferOfTheDay(pageHTML []byte) (int64, string, ogame.PlanetResources, ogame.Multiplier, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return 0, "", ogame.PlanetResources{}, ogame.Multiplier{}, err
	}
	return e.ExtractOfferOfTheDayFromDoc(doc)
}

func (e *Extractor) ExtractResourcesBuildings(pageHTML []byte) (ogame.ResourcesBuildings, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return ogame.ResourcesBuildings{}, err
	}
	return e.ExtractResourcesBuildingsFromDoc(doc)
}

func (e *Extractor) ExtractDefense(pageHTML []byte) (ogame.DefensesInfos, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return ogame.DefensesInfos{}, err
	}
	return e.ExtractDefenseFromDoc(doc)
}

func (e *Extractor) ExtractShips(pageHTML []byte) (ogame.ShipsInfos, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return ogame.ShipsInfos{}, err
	}
	return e.ExtractShipsFromDoc(doc)
}

func (e *Extractor) ExtractFacilities(pageHTML []byte) (ogame.Facilities, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return ogame.Facilities{}, err
	}
	return e.ExtractFacilitiesFromDoc(doc)
}

func (e *Extractor) ExtractTearDownToken(pageHTML []byte) (string, error) {
	return extractTearDownToken(pageHTML)
}

func (e *Extractor) ExtractResearch(pageHTML []byte) (ogame.Researches, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return ogame.Researches{}, err
	}
	return e.ExtractResearchFromDoc(doc), nil
}

func (e *Extractor) ExtractProduction(pageHTML []byte) ([]ogame.Quantifiable, int64, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return nil, 0, err
	}
	shipSumCountdown := e.ExtractOverviewShipSumCountdownFromBytes(pageHTML)
	production, err := e.ExtractProductionFromDoc(doc)
	return production, shipSumCountdown, err
}

func (e *Extractor) ExtractOverviewProduction(pageHTML []byte) ([]ogame.Quantifiable, int64, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return nil, 0, err
	}
	shipSumCountdown := e.ExtractOverviewShipSumCountdownFromBytes(pageHTML)
	production, err := e.ExtractOverviewProductionFromDoc(doc)
	return production, shipSumCountdown, err
}

func (e *Extractor) ExtractFleet1Ships(pageHTML []byte) (ogame.ShipsInfos, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return ogame.ShipsInfos{}, err
	}
	return e.ExtractFleet1ShipsFromDoc(doc)
}

func (e *Extractor) ExtractEspionageReportMessageIDs(pageHTML []byte) ([]ogame.EspionageReportSummary, int64, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return nil, 0, err
	}
	return e.ExtractEspionageReportMessageIDsFromDoc(doc)
}

func (e *Extractor) ExtractCombatReportMessagesSummary(pageHTML []byte) ([]ogame.CombatReportSummary, int64, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return nil, 0, err
	}
	return e.ExtractCombatReportMessagesFromDoc(doc)
}

func (e *Extractor) ExtractEspionageReport(pageHTML []byte) (ogame.EspionageReport, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return ogame.EspionageReport{}, err
	}
	return e.ExtractEspionageReportFromDoc(doc)
}

func (e *Extractor) ExtractResourcesProductions(pageHTML []byte) (ogame.Resources, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return ogame.Resources{}, err
	}
	return e.ExtractResourcesProductionsFromDoc(doc)
}

func (e *Extractor) ExtractPreferences(pageHTML []byte) (ogame.Preferences, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return ogame.Preferences{}, err
	}
	return e.ExtractPreferencesFromDoc(doc), nil
}

func (e *Extractor) ExtractSpioAnz(pageHTML []byte) (int64, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return 0, err
	}
	return e.ExtractSpioAnzFromDoc(doc), nil
}

func (e *Extractor) ExtractPreferencesShowActivityMinutes(pageHTML []byte) (bool, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return false, err
	}
	return e.ExtractShowActivityMinutesFromDoc(doc), nil
}

func (e *Extractor) ExtractHiddenFields(pageHTML []byte) (url.Values, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return nil, err
	}
	return e.ExtractHiddenFieldsFromDoc(doc), nil
}

func (e *Extractor) ExtractCommander(pageHTML []byte) (bool, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return false, err
	}
	return e.ExtractCommanderFromDoc(doc), nil
}

func (e *Extractor) ExtractAdmiral(pageHTML []byte) (bool, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return false, err
	}
	return e.ExtractAdmiralFromDoc(doc), nil
}

func (e *Extractor) ExtractEngineer(pageHTML []byte) (bool, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return false, err
	}
	return e.ExtractEngineerFromDoc(doc), nil
}

func (e *Extractor) ExtractGeologist(pageHTML []byte) (bool, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return false, err
	}
	return e.ExtractGeologistFromDoc(doc), nil
}

func (e *Extractor) ExtractTechnocrat(pageHTML []byte) (bool, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return false, err
	}
	return e.ExtractTechnocratFromDoc(doc), nil
}

func (e *Extractor) ExtractOGameSession(pageHTML []byte) (string, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return "", err
	}
	return e.ExtractOGameSessionFromDoc(doc), nil
}


func (e *Extractor) ExtractPlanetTypeFromDoc(doc *goquery.Document) (ogame.CelestialType, error) {
	return extractPlanetTypeFromDoc(doc)
}

func (e *Extractor) ExtractPlanetIDFromDoc(doc *goquery.Document) (ogame.CelestialID, error) {
	return extractPlanetIDFromDoc(doc)
}

func (e *Extractor) ExtractHiddenFieldsFromDoc(doc *goquery.Document) url.Values {
	return extractHiddenFieldsFromDoc(doc)
}

func (e *Extractor) ExtractBodyIDFromDoc(doc *goquery.Document) string {
	return ExtractBodyIDFromDoc(doc)
}

func (e *Extractor) ExtractIsInVacationFromDoc(doc *goquery.Document) bool {
	return extractIsInVacationFromDoc(doc)
}

func (e *Extractor) ExtractTearDownButtonEnabledFromDoc(doc *goquery.Document) bool {
	return extractTearDownButtonEnabledFromDoc(doc)
}

func (e *Extractor) ExtractPlanetsFromDoc(doc *goquery.Document) []ogame.Planet {
	return extractPlanetsFromDoc(doc)
}

func (e *Extractor) ExtractOgameTimestampFromDoc(doc *goquery.Document) int64 {
	return extractOgameTimestampFromDoc(doc)
}

func (e *Extractor) ExtractResourcesFromDoc(doc *goquery.Document) ogame.Resources {
	return extractResourcesFromDoc(doc)
}

func (e *Extractor) ExtractResourcesDetailsFromFullPageFromDoc(doc *goquery.Document) ogame.ResourcesDetails {
	return extractResourcesDetailsFromFullPageFromDoc(doc)
}

func (e *Extractor) ExtractPlanetFromDoc(doc *goquery.Document, v any) (ogame.Planet, error) {
	return extractPlanetFromDoc(doc, v)
}

func (e *Extractor) ExtractMoonsFromDoc(doc *goquery.Document) []ogame.Moon {
	return extractMoonsFromDoc(doc)
}

func (e *Extractor) ExtractMoonFromDoc(doc *goquery.Document, v any) (ogame.Moon, error) {
	return extractMoonFromDoc(doc, v)
}

func (e *Extractor) ExtractCelestialsFromDoc(doc *goquery.Document) ([]ogame.Celestial, error) {
	return extractCelestialsFromDoc(doc), nil
}

func (e *Extractor) ExtractCelestialFromDoc(doc *goquery.Document, v any) (ogame.Celestial, error) {
	return extractCelestialFromDoc(doc, v)
}

func (e *Extractor) ExtractResourcesBuildingsFromDoc(doc *goquery.Document) (ogame.ResourcesBuildings, error) {
	return extractResourcesBuildingsFromDoc(doc)
}

func (e *Extractor) ExtractDefenseFromDoc(doc *goquery.Document) (ogame.DefensesInfos, error) {
	return extractDefenseFromDoc(doc)
}

func (e *Extractor) ExtractShipsFromDoc(doc *goquery.Document) (ogame.ShipsInfos, error) {
	return extractShipsFromDoc(doc)
}

func (e *Extractor) ExtractFacilitiesFromDoc(doc *goquery.Document) (ogame.Facilities, error) {
	return extractFacilitiesFromDoc(doc)
}

func (e *Extractor) ExtractResearchFromDoc(doc *goquery.Document) ogame.Researches {
	return extractResearchFromDoc(doc)
}

func (e *Extractor) ExtractOGameSessionFromDoc(doc *goquery.Document) string {
	return ExtractOGameSessionFromDoc(doc)
}

func (e *Extractor) ExtractAttacksFromDoc(doc *goquery.Document, ownCoords []ogame.Coordinate) ([]ogame.AttackEvent, error) {
	return e.extractAttacksFromDoc(doc, clockwork.NewRealClock(), ownCoords)
}

func (e *Extractor) extractAttacksFromDoc(doc *goquery.Document, clock clockwork.Clock, ownCoords []ogame.Coordinate) ([]ogame.AttackEvent, error) {
	return extractAttacksFromDoc(doc, clock, ownCoords)
}

func (e *Extractor) ExtractOfferOfTheDayFromDoc(doc *goquery.Document) (price int64, importToken string, planetResources ogame.PlanetResources, multiplier ogame.Multiplier, err error) {
	return extractOfferOfTheDayFromDoc(doc)
}

func (e *Extractor) ExtractProductionFromDoc(doc *goquery.Document) ([]ogame.Quantifiable, error) {
	return extractProductionFromDoc(doc)
}

func (e *Extractor) ExtractOverviewProductionFromDoc(doc *goquery.Document) ([]ogame.Quantifiable, error) {
	return extractOverviewProductionFromDoc(doc)
}

func (e *Extractor) ExtractFleet1ShipsFromDoc(doc *goquery.Document) (ogame.ShipsInfos, error) {
	return extractFleet1ShipsFromDoc(doc)
}

func (e *Extractor) ExtractFleetDispatchACSFromDoc(doc *goquery.Document) []ogame.ACSValues {
	return extractFleetDispatchACSFromDoc(doc)
}

func (e *Extractor) ExtractEspionageReportMessageIDsFromDoc(doc *goquery.Document) ([]ogame.EspionageReportSummary, int64, error) {
	return extractEspionageReportMessageIDsFromDoc(doc)
}

func (e *Extractor) ExtractCombatReportMessagesFromDoc(doc *goquery.Document) ([]ogame.CombatReportSummary, int64, error) {
	return extractCombatReportMessagesFromDoc(doc)
}

func (e *Extractor) ExtractEspionageReportFromDoc(doc *goquery.Document) (ogame.EspionageReport, error) {
	return extractEspionageReportFromDoc(doc, e.loc)
}

func (e *Extractor) ExtractResourcesProductionsFromDoc(doc *goquery.Document) (ogame.Resources, error) {
	return extractResourcesProductionsFromDoc(doc)
}

func (e *Extractor) ExtractPreferencesFromDoc(doc *goquery.Document) ogame.Preferences {
	return ExtractPreferencesFromDoc(doc)
}

func (e *Extractor) ExtractResourceSettingsFromDoc(doc *goquery.Document) (ogame.ResourceSettings, string, error) {
	return extractResourceSettingsFromDoc(doc)
}

func (e *Extractor) ExtractFleetsFromEventListFromDoc(doc *goquery.Document) []ogame.Fleet {
	return extractFleetsFromEventListFromDoc(doc)
}

func (e *Extractor) ExtractIPMFromDoc(doc *goquery.Document) (duration, max int64, token string, err error) {
	return extractIPMFromDoc(doc)
}

func (e *Extractor) ExtractFleetsFromDoc(doc *goquery.Document) ([]ogame.Fleet, error) {
	return e.extractFleetsFromDoc(doc, e.loc)
}

func (e *Extractor) extractFleetsFromDoc(doc *goquery.Document, location *time.Location) ([]ogame.Fleet, error) {
	return extractFleetsFromDoc(doc, location, e.lifeformEnabled)
}

func (e *Extractor) ExtractSlotsFromDoc(doc *goquery.Document) (ogame.Slots, error) {
	return extractSlotsFromDoc(doc)
}

func (e *Extractor) ExtractServerTimeFromDoc(doc *goquery.Document) (time.Time, error) {
	return extractServerTimeFromDoc(doc)
}

func (e *Extractor) ExtractSpioAnzFromDoc(doc *goquery.Document) int64 {
	return extractSpioAnzFromDoc(doc)
}

func (e *Extractor) ExtractDisableChatBarFromDoc(doc *goquery.Document) bool {
	return extractDisableChatBarFromDoc(doc)
}

func (e *Extractor) ExtractDisableOutlawWarningFromDoc(doc *goquery.Document) bool {
	return extractDisableOutlawWarningFromDoc(doc)
}

func (e *Extractor) ExtractMobileVersionFromDoc(doc *goquery.Document) bool {
	return extractMobileVersionFromDoc(doc)
}

func (e *Extractor) ExtractShowOldDropDownsFromDoc(doc *goquery.Document) bool {
	return extractShowOldDropDownsFromDoc(doc)
}

func (e *Extractor) ExtractActivateAutofocusFromDoc(doc *goquery.Document) bool {
	return extractActivateAutofocusFromDoc(doc)
}

func (e *Extractor) ExtractEventsShowFromDoc(doc *goquery.Document) int64 {
	return extractEventsShowFromDoc(doc)
}

func (e *Extractor) ExtractSortSettingFromDoc(doc *goquery.Document) int64 {
	return extractSortSettingFromDoc(doc)
}

func (e *Extractor) ExtractSortOrderFromDoc(doc *goquery.Document) int64 {
	return extractSortOrderFromDoc(doc)
}

func (e *Extractor) ExtractShowDetailOverlayFromDoc(doc *goquery.Document) bool {
	return extractShowDetailOverlayFromDoc(doc)
}

func (e *Extractor) ExtractAnimatedSlidersFromDoc(doc *goquery.Document) bool {
	return extractAnimatedSlidersFromDoc(doc)
}

func (e *Extractor) ExtractAnimatedOverviewFromDoc(doc *goquery.Document) bool {
	return extractAnimatedOverviewFromDoc(doc)
}

func (e *Extractor) ExtractPopupsNoticesFromDoc(doc *goquery.Document) bool {
	return extractPopupsNoticesFromDoc(doc)
}

func (e *Extractor) ExtractPopopsCombatreportFromDoc(doc *goquery.Document) bool {
	return extractPopopsCombatreportFromDoc(doc)
}

func (e *Extractor) ExtractSpioReportPicturesFromDoc(doc *goquery.Document) bool {
	return extractSpioReportPicturesFromDoc(doc)
}

func (e *Extractor) ExtractMsgResultsPerPageFromDoc(doc *goquery.Document) int64 {
	return extractMsgResultsPerPageFromDoc(doc)
}

func (e *Extractor) ExtractAuctioneerNotificationsFromDoc(doc *goquery.Document) bool {
	return extractAuctioneerNotificationsFromDoc(doc)
}

func (e *Extractor) ExtractEconomyNotificationsFromDoc(doc *goquery.Document) bool {
	return extractEconomyNotificationsFromDoc(doc)
}

func (e *Extractor) ExtractShowActivityMinutesFromDoc(doc *goquery.Document) bool {
	return extractShowActivityMinutesFromDoc(doc)
}

func (e *Extractor) ExtractPreserveSystemOnPlanetChangeFromDoc(doc *goquery.Document) bool {
	return extractPreserveSystemOnPlanetChangeFromDoc(doc)
}

func (e *Extractor) ExtractNotifBuildListFromDoc(doc *goquery.Document) bool {
	return extractNotifBuildListFromDoc(doc)
}

func (e *Extractor) ExtractNotifFriendlyFleetActivitiesFromDoc(doc *goquery.Document) bool {
	return extractNotifFriendlyFleetActivitiesFromDoc(doc)
}

func (e *Extractor) ExtractNotifHostileFleetActivitiesFromDoc(doc *goquery.Document) bool {
	return extractNotifHostileFleetActivitiesFromDoc(doc)
}

func (e *Extractor) ExtractNotifForeignEspionageFromDoc(doc *goquery.Document) bool {
	return extractNotifForeignEspionageFromDoc(doc)
}

func (e *Extractor) ExtractNotifAllianceBroadcastsFromDoc(doc *goquery.Document) bool {
	return extractNotifAllianceBroadcastsFromDoc(doc)
}

func (e *Extractor) ExtractNotifAllianceMessagesFromDoc(doc *goquery.Document) bool {
	return extractNotifAllianceMessagesFromDoc(doc)
}

func (e *Extractor) ExtractNotifAuctionsFromDoc(doc *goquery.Document) bool {
	return extractNotifAuctionsFromDoc(doc)
}

func (e *Extractor) ExtractNotifAccountFromDoc(doc *goquery.Document) bool {
	return extractNotifAccountFromDoc(doc)
}

func (e *Extractor) ExtractCharacterClassFromDoc(doc *goquery.Document) (ogame.CharacterClass, error) {
	return 0, errors.New("character class not supported in ")
}

func (e *Extractor) ExtractAllianceClass(pageHTML []byte) (ogame.AllianceClass, error) {
	return 0, errors.New("alliance class not supported")
}

func (e *Extractor) ExtractAllianceClassFromDoc(doc *goquery.Document) (ogame.AllianceClass, error) {
	return 0, errors.New("alliance class not supported")
}

func (e *Extractor) ExtractCommanderFromDoc(doc *goquery.Document) bool {
	return extractCommanderFromDoc(doc)
}

func (e *Extractor) ExtractAdmiralFromDoc(doc *goquery.Document) bool {
	return extractAdmiralFromDoc(doc)
}

func (e Extractor) ExtractLifeformTypeFromDoc(doc *goquery.Document) ogame.LifeformType {
	return ogame.NoneLfType
}

func (e *Extractor) ExtractEngineerFromDoc(doc *goquery.Document) bool {
	return extractEngineerFromDoc(doc)
}

func (e *Extractor) ExtractGeologistFromDoc(doc *goquery.Document) bool {
	return extractGeologistFromDoc(doc)
}

func (e *Extractor) ExtractTechnocratFromDoc(doc *goquery.Document) bool {
	return extractTechnocratFromDoc(doc)
}

func (e *Extractor) ExtractColoniesFromDoc(doc *goquery.Document) (int64, int64) {
	return extractColoniesFromDoc(doc)
}

func (e *Extractor) ExtractAbandonInformation(doc *goquery.Document) (string, string) {
	return extractAbandonInformation(doc)
}



func (e *Extractor) ExtractPlanetCoordinate(pageHTML []byte) (ogame.Coordinate, error) {
	return extractPlanetCoordinate(pageHTML)
}

func (e *Extractor) ExtractPlanetID(pageHTML []byte) (ogame.CelestialID, error) {
	return extractPlanetID(pageHTML)
}

func (e *Extractor) ExtractOverviewShipSumCountdownFromBytes(pageHTML []byte) int64 {
	return extractOverviewShipSumCountdownFromBytes(pageHTML)
}

func (e *Extractor) ExtractOGameTimestampFromBytes(pageHTML []byte) (int64, error) {
	return extractOGameTimestampFromBytes(pageHTML)
}

func (e *Extractor) ExtractPlanetType(pageHTML []byte) (ogame.CelestialType, error) {
	return extractPlanetType(pageHTML)
}

func (e *Extractor) ExtractAjaxChatToken(pageHTML []byte) (string, error) {
	return extractAjaxChatToken(pageHTML)
}

func (e *Extractor) ExtractUserInfos(pageHTML []byte) (ogame.UserInfos, error) {
	return extractUserInfos(pageHTML, e.GetLanguage())
}

func (e *Extractor) ExtractResourcesDetails(pageHTML []byte) (out ogame.ResourcesDetails, err error) {
	return extractResourcesDetails(pageHTML)
}


func (e *Extractor) ExtractCoord(v string) (coord ogame.Coordinate) {
	return ExtractCoord(v)
}

func (e *Extractor) ExtractGalaxyInfos(pageHTML []byte, botPlayerName string, botPlayerID, botPlayerRank int64) (ogame.SystemInfos, error) {
	return extractGalaxyInfos(pageHTML, botPlayerName, botPlayerID, botPlayerRank)
}

func (e *Extractor) ExtractPhalanx(pageHTML []byte) ([]ogame.PhalanxFleet, error) {
	return extractPhalanx(pageHTML)
}

func (e *Extractor) ExtractPhalanxNewToken(pageHTML []byte) (string, error) {
	panic("not implemented")
}

func (e *Extractor) ExtractJumpGate(pageHTML []byte) (ogame.ShipsInfos, string, []ogame.MoonID, int64, error) {
	return extractJumpGate(pageHTML)
}

func (e *Extractor) ExtractFederation(pageHTML []byte) (url.Values, error) {
	return extractFederation(pageHTML)
}

func (e *Extractor) ExtractConstructions(pageHTML []byte) (ogame.Constructions, error) {
	return extractConstructions(pageHTML), nil
}

func (e *Extractor) ExtractFleetDeutSaveFactor(pageHTML []byte) float64 {
	return extractFleetDeutSaveFactor(pageHTML)
}

func (e *Extractor) ExtractCancelBuildingInfos(pageHTML []byte) (token string, techID, listID int64, err error) {
	return extractCancelBuildingInfos(pageHTML)
}

func (e *Extractor) ExtractCancelResearchInfos(pageHTML []byte) (token string, techID, listID int64, err error) {
	return extractCancelResearchInfos(pageHTML)
}

func (e *Extractor) ExtractEmpire(pageHTML []byte) ([]ogame.EmpireCelestial, error) {
	return extractEmpire(pageHTML)
}

func (e *Extractor) ExtractEmpireJSON(pageHTML []byte) (any, error) {
	return ExtractEmpireJSON(pageHTML)
}

func (e *Extractor) ExtractCharacterClass(pageHTML []byte) (ogame.CharacterClass, error) {
	return 0, errors.New("character class not supported in ")
}

func (e *Extractor) ExtractAuction(pageHTML []byte) (ogame.Auction, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return ogame.Auction{}, err
	}
	return extractAuctionFromDoc(doc)
}

func (e *Extractor) ExtractHighscore(pageHTML []byte) (ogame.Highscore, error) {
	panic("not implemented")
}

func (e *Extractor) ExtractHighscoreFromDoc(doc *goquery.Document) (ogame.Highscore, error) {
	panic("not implemented")
}

func (e *Extractor) ExtractAllResources(pageHTML []byte) (map[ogame.CelestialID]ogame.Resources, error) {
	panic("not implemented")
}

func (e *Extractor) ExtractDMCosts(pageHTML []byte) (ogame.DMCosts, error) {
	panic("not implemented")
}

func (e *Extractor) ExtractBuffActivation(pageHTML []byte) (string, []ogame.Item, error) {
	panic("not implemented")
}

func (e *Extractor) ExtractIsMobile(pageHTML []byte) (bool, error) {
	panic("not implemented")
}

func (e *Extractor) ExtractIsMobileFromDoc(doc *goquery.Document) bool {
	panic("not implemented")
}

func (e *Extractor) ExtractLfBuildings(pageHTML []byte) (ogame.LfBuildings, error) {
	panic("not implemented")
}

func (e *Extractor) ExtractLfBuildingsFromDoc(doc *goquery.Document) (ogame.LfBuildings, error) {
	panic("not implemented")
}

func (e *Extractor) ExtractLfResearch(pageHTML []byte) (ogame.LfResearches, error) {
	panic("not implemented")
}

func (e *Extractor) ExtractLfResearchFromDoc(doc *goquery.Document) (ogame.LfResearches, error) {
	panic("not implemented")
}

func (e *Extractor) ExtractAvailableDiscoveries(pageHTML []byte) (int64, error) {
	panic("not implemented")
}

func (e *Extractor) ExtractLfSlotsFromDoc(doc *goquery.Document) [18]ogame.LfSlot {
	panic("not implemented")
}

func (e *Extractor) ExtractArtefactsFromDoc(doc *goquery.Document) (int64, int64) {
	panic("not implemented")
}

func (e *Extractor) ExtractChapter(pageHTML []byte) (ogame.Chapter, error) {
	panic("implement me")
}

func (e *Extractor) ExtractChapterFromDoc(document *goquery.Document) (ogame.Chapter, error) {
	panic("implement me")
}
