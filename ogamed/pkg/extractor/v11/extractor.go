package v11

import (
	"github.com/PuerkitoBio/goquery"
	v104 "github.com/alaingilbert/ogame/pkg/extractor/v104"
	"github.com/alaingilbert/ogame/pkg/ogame"
)

type Extractor struct {
	v104.Extractor
}

func NewExtractor() *Extractor {
	return &Extractor{}
}

func (e Extractor) ExtractResourceSettings(pageHTML []byte) (ogame.ResourceSettings, string, error) {
	return extractResourceSettingsFromPage(pageHTML)
}

func (e Extractor) ExtractCancelBuildingInfos(pageHTML []byte) (token string, techID, listID int64, err error) {
	return extractCancelBuildingInfos(pageHTML)
}

func (e Extractor) ExtractCancelResearchInfos(pageHTML []byte) (token string, techID, listID int64, err error) {
	return extractCancelResearchInfos(pageHTML)
}

func (e Extractor) ExtractCancelLfBuildingInfos(pageHTML []byte) (token string, id, listID int64, err error) {
	return extractCancelLfBuildingInfos(pageHTML)
}

func (e Extractor) ExtractLifeformTypeFromDoc(doc *goquery.Document) ogame.LifeformType {
	return extractLifeformTypeFromDoc(doc)
}

func (e *Extractor) ExtractJumpGate(pageHTML []byte) (ogame.ShipsInfos, string, []ogame.MoonID, int64, error) {
	return extractJumpGate(pageHTML)
}

func (e *Extractor) ExtractPreferencesFromDoc(doc *goquery.Document) ogame.Preferences {
	return extractPreferencesFromDoc(doc)
}
