package v9

import (
	"bytes"
	"encoding/json"
	"strings"

	"github.com/PuerkitoBio/goquery"
	"github.com/alaingilbert/clockwork"
	v874 "github.com/alaingilbert/ogame/pkg/extractor/v874"
	"github.com/alaingilbert/ogame/pkg/ogame"
)

type Extractor struct {
	v874.Extractor
}

func NewExtractor() *Extractor {
	return &Extractor{}
}

func (e *Extractor) ExtractTechnologyDetailsFromDoc(doc *goquery.Document) (ogame.TechnologyDetails, error) {
	return extractTechnologyDetailsFromDoc(doc)
}

type technologyDetailsStruct struct {
	Target  string `json:"target"`
	Content struct {
		Technologydetails string `json:"technologydetails"`
	} `json:"content"`
	Files struct {
		Js  []string `json:"js"`
		CSS []string `json:"css"`
	} `json:"files"`
	Page struct {
		StateObj string `json:"stateObj"`
		Title    string `json:"title"`
		URL      string `json:"url"`
	} `json:"page"`
	ServerTime int `json:"serverTime"`
}

func (e *Extractor) ExtractTechnologyDetails(pageHTML []byte) (out ogame.TechnologyDetails, err error) {
	var technologyDetails technologyDetailsStruct
	if err := json.Unmarshal(pageHTML, &technologyDetails); err != nil {
		return out, err
	}
	doc, err := goquery.NewDocumentFromReader(strings.NewReader(technologyDetails.Content.Technologydetails))
	if err != nil {
		return out, err
	}
	return e.ExtractTechnologyDetailsFromDoc(doc)
}

func (e *Extractor) ExtractCancelLfBuildingInfos(pageHTML []byte) (token string, id, listID int64, err error) {
	return extractCancelLfBuildingInfos(pageHTML)
}

func (e *Extractor) ExtractCancelResearchInfos(pageHTML []byte) (token string, id, listID int64, err error) {
	return extractCancelResearchInfos(pageHTML, e.GetLifeformEnabled())
}

func (e *Extractor) ExtractEmpire(pageHTML []byte) ([]ogame.EmpireCelestial, error) {
	return extractEmpire(pageHTML)
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

func (e *Extractor) ExtractOverviewProductionFromDoc(doc *goquery.Document) ([]ogame.Quantifiable, error) {
	return extractOverviewProductionFromDoc(doc, e.GetLifeformEnabled())
}

func (e *Extractor) ExtractEspionageReport(pageHTML []byte) (ogame.EspionageReport, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return ogame.EspionageReport{}, err
	}
	return e.ExtractEspionageReportFromDoc(doc)
}

func (e *Extractor) ExtractEspionageReportFromDoc(doc *goquery.Document) (ogame.EspionageReport, error) {
	return extractEspionageReportFromDoc(doc, e.GetLocation())
}

func (e *Extractor) ExtractResources(pageHTML []byte) (ogame.Resources, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return ogame.Resources{}, err
	}
	return e.ExtractResourcesFromDoc(doc), nil
}

func (e *Extractor) ExtractResourcesFromDoc(doc *goquery.Document) ogame.Resources {
	return extractResourcesFromDoc(doc)
}

func (e *Extractor) ExtractResourcesDetailsFromFullPage(pageHTML []byte) (ogame.ResourcesDetails, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return ogame.ResourcesDetails{}, err
	}
	return e.ExtractResourcesDetailsFromFullPageFromDoc(doc), nil
}

func (e *Extractor) ExtractResourcesDetailsFromFullPageFromDoc(doc *goquery.Document) ogame.ResourcesDetails {
	return extractResourcesDetailsFromFullPageFromDoc(doc)
}

func (e *Extractor) ExtractConstructions(pageHTML []byte) (ogame.Constructions, error) {
	return ExtractConstructions(pageHTML, clockwork.NewRealClock()), nil
}

func (e *Extractor) ExtractResourceSettings(pageHTML []byte) (ogame.ResourceSettings, string, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return ogame.ResourceSettings{}, "", err
	}
	return e.ExtractResourceSettingsFromDoc(doc)
}

func (e *Extractor) ExtractLfBuildings(pageHTML []byte) (ogame.LfBuildings, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return ogame.LfBuildings{}, err
	}
	return e.ExtractLfBuildingsFromDoc(doc)
}

func (e *Extractor) ExtractLfBuildingsFromDoc(doc *goquery.Document) (ogame.LfBuildings, error) {
	return extractLfBuildingsFromDoc(doc)
}

func (e *Extractor) ExtractLfResearch(pageHTML []byte) (ogame.LfResearches, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return ogame.LfResearches{}, err
	}
	return e.ExtractLfResearchFromDoc(doc)
}

func (e *Extractor) ExtractLfResearchFromDoc(doc *goquery.Document) (ogame.LfResearches, error) {
	return extractLfResearchFromDoc(doc)
}

func (e *Extractor) ExtractLfSlotsFromDoc(doc *goquery.Document) [18]ogame.LfSlot {
	return extractLfSlotsFromDoc(doc)
}

func (e *Extractor) ExtractArtefactsFromDoc(doc *goquery.Document) (int64, int64) {
	return extractArtefactsFromDoc(doc)
}

func (e *Extractor) ExtractTearDownButtonEnabled(pageHTML []byte) (bool, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return false, err
	}
	return e.ExtractTearDownButtonEnabledFromDoc(doc), nil
}

func (e *Extractor) ExtractTearDownButtonEnabledFromDoc(doc *goquery.Document) bool {
	return extractTearDownButtonEnabledFromDoc(doc)
}

func (e *Extractor) ExtractAvailableDiscoveries(pageHTML []byte) (int64, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return 0, err
	}
	return e.extractAvailableDiscoveriesFromDoc(doc), nil
}

func (e *Extractor) extractAvailableDiscoveriesFromDoc(doc *goquery.Document) int64 {
	return extractAvailableDiscoveriesFromDoc(doc)
}
