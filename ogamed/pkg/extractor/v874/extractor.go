package v874

import (
	"bytes"
	"github.com/PuerkitoBio/goquery"
	"github.com/alaingilbert/ogame/pkg/extractor/v8"
	"github.com/alaingilbert/ogame/pkg/ogame"
)

type Extractor struct {
	v8.Extractor
}

func NewExtractor() *Extractor {
	return &Extractor{}
}

func (e *Extractor) ExtractOfferOfTheDay(pageHTML []byte) (int64, string, ogame.PlanetResources, ogame.Multiplier, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return 0, "", ogame.PlanetResources{}, ogame.Multiplier{}, err
	}
	return e.ExtractOfferOfTheDayFromDoc(doc)
}

func (e *Extractor) ExtractOfferOfTheDayFromDoc(doc *goquery.Document) (price int64, importToken string, planetResources ogame.PlanetResources, multiplier ogame.Multiplier, err error) {
	return extractOfferOfTheDayFromDoc(doc)
}

func (e *Extractor) ExtractAuction(pageHTML []byte) (ogame.Auction, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return ogame.Auction{}, err
	}
	return extractAuctionFromDoc(doc)
}

func (e *Extractor) ExtractBuffActivation(pageHTML []byte) (string, []ogame.Item, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return "", nil, err
	}
	return e.ExtractBuffActivationFromDoc(doc)
}

func (e *Extractor) ExtractBuffActivationFromDoc(doc *goquery.Document) (string, []ogame.Item, error) {
	return extractBuffActivationFromDoc(doc)
}
